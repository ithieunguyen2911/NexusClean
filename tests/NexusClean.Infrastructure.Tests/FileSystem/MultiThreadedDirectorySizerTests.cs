using System.Diagnostics;
using NexusClean.Infrastructure.FileSystem;
using NexusClean.Infrastructure.Tests.Fixtures;
using Xunit;

namespace NexusClean.Infrastructure.Tests.FileSystem;

public sealed class MultiThreadedDirectorySizerTests
{
    [Fact]
    public async Task ComputeSizeAsync_AccuratelySumsNormalFilesAndSkipsOfflineCloudPlaceholders()
    {
        using var sandbox = new TempSandboxFixture();
        sandbox.CreateFileWithSize(@"normal\a.bin", 4096);
        sandbox.CreateFileWithSize(@"normal\sub\b.bin", 2048);
        var offlineCloudFile = sandbox.CreateFileWithSize(@"normal\cloud_placeholder.docx", 50_000);
        File.SetAttributes(offlineCloudFile, File.GetAttributes(offlineCloudFile) | FileAttributes.Offline);

        var sizer = new MultiThreadedDirectorySizer();
        var snap = await sizer.ComputeSizeAsync(Path.Combine(sandbox.RootPath, "normal"));

        Assert.Equal(4096 + 2048, snap.SizeInBytes);
        Assert.Equal(2, snap.FileCount);
        Assert.True(snap.SkippedReparseOrCloudCount >= 1);
    }

    [Fact]
    public async Task ComputeSizeAsync_SkipsNtfsJunctionLoopWithoutInfiniteRecursion()
    {
        using var sandbox = new TempSandboxFixture();
        var realDir = sandbox.CreateDirectory("target_data");
        sandbox.CreateFileWithSize(@"target_data\payload.dat", 8192);

        var scanDir = sandbox.CreateDirectory("scan_root");
        sandbox.CreateFileWithSize(@"scan_root\local.dat", 1024);

        var junctionPath = Path.Combine(scanDir, "junction_loop");
        // Create NTFS directory junction via mklink /J (requires zero Admin privileges on Windows!)
        using (var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{realDir}\"")
               {
                   CreateNoWindow = true,
                   UseShellExecute = false
               }))
        {
            p?.WaitForExit(3000);
        }

        var sizer = new MultiThreadedDirectorySizer();
        var snap = await sizer.ComputeSizeAsync(scanDir);

        // Only local.dat (1024 B) is counted; junction_loop (8192 B) is skipped!
        Assert.Equal(1024, snap.SizeInBytes);
        Assert.Equal(1, snap.FileCount);
        Assert.True(snap.SkippedReparseOrCloudCount >= 1);
    }

    [Fact]
    public async Task ComputeSizeAsync_CancelsPromptlyUnderTwoSeconds()
    {
        using var sandbox = new TempSandboxFixture();
        for (var i = 0; i < 25; i++)
        {
            sandbox.CreateFileWithSize($@"sub_{i}\file.bin", 512);
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sizer = new MultiThreadedDirectorySizer();
        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sizer.ComputeSizeAsync(sandbox.RootPath, cts.Token));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2));
    }
}
