using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexusClean.Application.Models;

namespace NexusClean.Infrastructure.Persistence;

/// <summary>
/// Bounded append-only JSONL audit logger (5 MB per file x max 3 archives = 15 MB ceiling)
/// ensuring safe operation even on drives with &lt; 100 MB free space.
/// </summary>
public sealed class JsonlAuditLogRepository : IAuditLogRepository
{
    public const long DefaultMaxBytesPerFile = 5L * 1024 * 1024;
    public const int DefaultMaxArchives = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    private readonly long _maxBytesPerFile;
    private readonly int _maxArchives;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string LogFilePath { get; }

    public JsonlAuditLogRepository(
        string? customLogFilePath = null,
        long maxBytesPerFile = DefaultMaxBytesPerFile,
        int maxArchives = DefaultMaxArchives)
    {
        if (!string.IsNullOrWhiteSpace(customLogFilePath))
        {
            LogFilePath = Path.GetFullPath(customLogFilePath);
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            LogFilePath = Path.Combine(localAppData, "NexusClean", "logs", "audit.jsonl");
        }

        _maxBytesPerFile = Math.Max(1024, maxBytesPerFile);
        _maxArchives = Math.Max(1, maxArchives);
    }

    public async Task AppendBatchReportAsync(
        CleanupBatchReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var line = JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine;
            var lineBytes = Encoding.UTF8.GetByteCount(line);

            RollOverIfNeeded(lineBytes);
            await File.AppendAllTextAsync(LogFilePath, line, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Never crash user cleanup if disk is literally 0 bytes free prior to first delete.
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<CleanupBatchReport>> ReadRecentReportsAsync(
        int maxCount = 20,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CleanupBatchReport>();
        if (!File.Exists(LogFilePath))
        {
            return results;
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lines = await File.ReadAllLinesAsync(LogFilePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            for (var i = lines.Length - 1; i >= 0 && results.Count < maxCount; i--)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                try
                {
                    var item = JsonSerializer.Deserialize<CleanupBatchReport>(lines[i], JsonOptions);
                    if (item is not null)
                    {
                        results.Add(item);
                    }
                }
                catch
                {
                    // Skip malformed historical line.
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }

        return results;
    }

    private void RollOverIfNeeded(int incomingBytes)
    {
        if (!File.Exists(LogFilePath))
        {
            return;
        }

        var info = new FileInfo(LogFilePath);
        if (info.Length + incomingBytes <= _maxBytesPerFile)
        {
            return;
        }

        var dir = Path.GetDirectoryName(LogFilePath)!;
        var baseName = Path.GetFileNameWithoutExtension(LogFilePath);
        var ext = Path.GetExtension(LogFilePath);

        var oldestArchive = Path.Combine(dir, $"{baseName}.{_maxArchives}{ext}");
        if (File.Exists(oldestArchive))
        {
            File.Delete(oldestArchive);
        }

        for (var i = _maxArchives - 1; i >= 1; i--)
        {
            var src = Path.Combine(dir, $"{baseName}.{i}{ext}");
            var dst = Path.Combine(dir, $"{baseName}.{i + 1}{ext}");
            if (File.Exists(src))
            {
                File.Move(src, dst, overwrite: true);
            }
        }

        var firstArchive = Path.Combine(dir, $"{baseName}.1{ext}");
        File.Move(LogFilePath, firstArchive, overwrite: true);
    }
}
