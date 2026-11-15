namespace NexusClean.Infrastructure.Tests.Fixtures;

/// <summary>
/// Ephemeral sandbox directory under %TEMP%\NexusClean.Tests\&lt;guid&gt; enforcing Constitution Principle VI.
/// Never touches real host AppData or system directories.
/// </summary>
public sealed class TempSandboxFixture : IDisposable
{
    public string RootPath { get; }

    public TempSandboxFixture()
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "NexusClean.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string CreateDirectory(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        EnsureInsideSandbox(fullPath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public string CreateFileWithSize(string relativePath, int sizeInBytes, byte fillByte = 0x42)
    {
        var fullPath = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        EnsureInsideSandbox(fullPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var buffer = new byte[sizeInBytes];
        if (sizeInBytes > 0)
        {
            Array.Fill(buffer, fillByte);
        }

        File.WriteAllBytes(fullPath, buffer);
        return fullPath;
    }

    public string CreateTextFile(string relativePath, string content)
    {
        var fullPath = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        EnsureInsideSandbox(fullPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private void EnsureInsideSandbox(string fullPath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootPath)) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.TrimEndingDirectorySeparator(fullPath), Path.TrimEndingDirectorySeparator(RootPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path '{fullPath}' escaped sandbox '{RootPath}'.");
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(RootPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var attr = File.GetAttributes(entry);
                        if ((attr & FileAttributes.ReparsePoint) != 0 && Directory.Exists(entry))
                        {
                            Directory.Delete(entry, false);
                        }
                        else if ((attr & FileAttributes.ReadOnly) != 0)
                        {
                            File.SetAttributes(entry, attr & ~FileAttributes.ReadOnly);
                        }
                    }
                    catch
                    {
                        // Best-effort cleanup for sandbox teardown.
                    }
                }

                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch
        {
            // Ignore transient teardown locks in %TEMP%.
        }
    }
}
