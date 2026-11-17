using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using NexusClean.Application.Models;
using NexusClean.Domain.Interfaces;

namespace NexusClean.Infrastructure.OS;

/// <summary>
/// Inspects active OS processes and locked file handles using Win32 Restart Manager (rstrtmgr.dll).
/// </summary>
public sealed class WindowsProcessInspector : IProcessLockInspector
{
    private const int RmRebootReasonNone = 0;
    private const int ErrorMoreData = 234;
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;

    public IReadOnlySet<string> GetRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return names;
        }

        foreach (var proc in processes)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(proc.ProcessName))
                {
                    names.Add(proc.ProcessName);
                }
            }
            catch
            {
                // Ignore protected system processes.
            }
            finally
            {
                proc.Dispose();
            }
        }

        return names;
    }

    public IReadOnlyList<string> GetLockingProcesses(IReadOnlyList<string> targetPaths)
    {
        if (!OperatingSystem.IsWindows() || targetPaths is null || targetPaths.Count == 0)
        {
            return Array.Empty<string>();
        }

        var candidateFiles = CollectSampleCandidateFiles(targetPaths, maxFiles: 64);
        if (candidateFiles.Count == 0)
        {
            return Array.Empty<string>();
        }

        var lockers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sessionKey = Guid.NewGuid().ToString("N");

        var res = RmStartSession(out var sessionHandle, 0, sessionKey);
        if (res != 0)
        {
            return Array.Empty<string>();
        }

        try
        {
            var fileArray = candidateFiles.ToArray();
            res = RmRegisterResources(
                sessionHandle,
                (uint)fileArray.Length,
                fileArray,
                0,
                null,
                0,
                null);

            if (res != 0)
            {
                return Array.Empty<string>();
            }

            uint pnProcInfoNeeded = 0;
            uint pnProcInfo = 0;
            uint lpdwRebootReasons = RmRebootReasonNone;

            res = RmGetList(
                sessionHandle,
                out pnProcInfoNeeded,
                ref pnProcInfo,
                null,
                ref lpdwRebootReasons);

            if (res == ErrorMoreData && pnProcInfoNeeded > 0)
            {
                var processInfo = new RmProcessInfo[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;

                res = RmGetList(
                    sessionHandle,
                    out pnProcInfoNeeded,
                    ref pnProcInfo,
                    processInfo,
                    ref lpdwRebootReasons);

                if (res == 0)
                {
                    for (var i = 0; i < pnProcInfo; i++)
                    {
                        var appName = processInfo[i].strAppName;
                        if (!string.IsNullOrWhiteSpace(appName))
                        {
                            lockers.Add(appName.Trim());
                        }
                        else
                        {
                            try
                            {
                                using var p = Process.GetProcessById(processInfo[i].Process.dwProcessId);
                                if (!string.IsNullOrWhiteSpace(p.ProcessName))
                                {
                                    lockers.Add(p.ProcessName);
                                }
                            }
                            catch
                            {
                                // Process exited between RmGetList and lookup.
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Never fail scan on Restart Manager edge cases.
        }
        finally
        {
            _ = RmEndSession(sessionHandle);
        }

        return lockers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static List<string> CollectSampleCandidateFiles(IReadOnlyList<string> targetPaths, int maxFiles)
    {
        var files = new List<string>(maxFiles);
        foreach (var path in targetPaths)
        {
            if (files.Count >= maxFiles) break;
            if (string.IsNullOrWhiteSpace(path)) continue;

            try
            {
                if (File.Exists(path))
                {
                    files.Add(path);
                    continue;
                }

                if (Directory.Exists(path))
                {
                    foreach (var f in Directory.EnumerateFiles(path, "*", new EnumerationOptions
                             {
                                 IgnoreInaccessible = true,
                                 RecurseSubdirectories = true,
                                 MaxRecursionDepth = 2,
                                 AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline
                             }))
                    {
                        files.Add(f);
                        if (files.Count >= maxFiles) break;
                    }
                }
            }
            catch
            {
                // Skip inaccessible folder.
            }
        }

        return files;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    private enum RmAppType
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string strServiceShortName;
        public RmAppType ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[]? rgsFilenames,
        uint nApplications,
        RmUniqueProcess[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RmProcessInfo[]? rgAffectedApps,
        ref uint lpdwRebootReasons);
}

/// <summary>
/// Captures current Windows elevation state, running processes, and fixed NTFS volumes.
/// </summary>
public sealed class WindowsSystemSnapshotProvider : ISystemSnapshotProvider
{
    private readonly IProcessLockInspector _lockInspector;

    public WindowsSystemSnapshotProvider(IProcessLockInspector lockInspector)
    {
        _lockInspector = lockInspector ?? throw new ArgumentNullException(nameof(lockInspector));
    }

    public ISystemSnapshot CaptureSnapshot(
        IReadOnlyList<string>? additionalProtectedRoots = null,
        IReadOnlyList<string>? additionalProtectedFileNames = null)
    {
        var isElevated = false;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                isElevated = false;
            }
        }

        var volumes = new List<VolumeSnapshot>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType == DriveType.Fixed && d.IsReady)
                {
                    volumes.Add(new VolumeSnapshot(
                        d.Name,
                        d.TotalSize,
                        d.AvailableFreeSpace,
                        d.DriveFormat,
                        d.IsReady));
                }
            }
        }
        catch
        {
            // Ignore transient drive enumeration errors.
        }

        return new LiveWindowsSnapshot(
            isElevated,
            _lockInspector.GetRunningProcessNames(),
            additionalProtectedRoots ?? Array.Empty<string>(),
            additionalProtectedFileNames ?? Array.Empty<string>(),
            volumes,
            _lockInspector);
    }

    private sealed class LiveWindowsSnapshot(
        bool isElevated,
        IReadOnlySet<string> runningProcesses,
        IReadOnlyList<string> roots,
        IReadOnlyList<string> files,
        IReadOnlyList<VolumeSnapshot> volumes,
        IProcessLockInspector lockInspector) : ISystemSnapshot
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<string>> _lockCache =
            new(StringComparer.OrdinalIgnoreCase);

        public bool IsCurrentProcessElevated => isElevated;
        public IReadOnlySet<string> RunningProcessNames => runningProcesses;
        public IReadOnlyList<string> AdditionalProtectedRoots => roots;
        public IReadOnlyList<string> AdditionalProtectedFileNames => files;
        public IReadOnlyList<VolumeSnapshot> FixedVolumes => volumes;

        public IReadOnlyList<string> GetLockingProcesses(IReadOnlyList<string> candidatePaths)
        {
            if (candidatePaths is null || candidatePaths.Count == 0)
            {
                return Array.Empty<string>();
            }

            var key = string.Join("|", candidatePaths);
            return _lockCache.GetOrAdd(key, _ => lockInspector.GetLockingProcesses(candidatePaths));
        }
    }
}
