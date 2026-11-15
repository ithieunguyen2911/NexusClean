using NexusClean.Domain.Enums;

namespace NexusClean.Domain.Entities;

/// <summary>
/// Immutable inspection verdict emitted by an ISafetyGuard implementation.
/// </summary>
public sealed record GuardVerdict
{
    public string GuardName { get; init; }
    public GuardSeverity Severity { get; init; }
    public string ReasonCode { get; init; }
    public string MessageVi { get; init; }

    public GuardVerdict(string guardName, GuardSeverity severity, string reasonCode, string messageVi = "")
    {
        if (string.IsNullOrWhiteSpace(guardName))
        {
            throw new ArgumentException("GuardName is required.", nameof(guardName));
        }

        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("ReasonCode is required.", nameof(reasonCode));
        }

        if (severity != GuardSeverity.Allow && string.IsNullOrWhiteSpace(messageVi))
        {
            throw new ArgumentException("MessageVi is required when Severity is not Allow.", nameof(messageVi));
        }

        GuardName = guardName.Trim();
        Severity = severity;
        ReasonCode = reasonCode.Trim();
        MessageVi = messageVi?.Trim() ?? string.Empty;
    }

    public static GuardVerdict Allow(string guardName, string reasonCode = "SAFE_OK") =>
        new(guardName, GuardSeverity.Allow, reasonCode, "Đạt kiểm tra an toàn.");

    public static GuardVerdict Warn(string guardName, string reasonCode, string messageVi) =>
        new(guardName, GuardSeverity.Warn, reasonCode, messageVi);

    public static GuardVerdict RequireElevation(string guardName, string reasonCode, string messageVi) =>
        new(guardName, GuardSeverity.RequireElevation, reasonCode, messageVi);

    public static GuardVerdict Block(string guardName, string reasonCode, string messageVi) =>
        new(guardName, GuardSeverity.Block, reasonCode, messageVi);
}

/// <summary>
/// Secondary-drive relocation plan with +20% free-space buffer verification.
/// </summary>
public sealed record RelocationPlan
{
    public string FindingId { get; init; }
    public string SourcePath { get; init; }
    public long SourceSizeInBytes { get; init; }
    public string? RecommendedTargetDrive { get; init; }
    public string? RecommendedTargetPath { get; init; }
    public long RequiredBytesWithBuffer { get; init; }
    public long AvailableTargetBytes { get; init; }
    public bool HasSufficientSpace { get; init; }
    public IReadOnlyList<string> StepByStepGuideVi { get; init; }

    public RelocationPlan(
        string findingId,
        string sourcePath,
        long sourceSizeInBytes,
        string? recommendedTargetDrive,
        string? recommendedTargetPath,
        long availableTargetBytes,
        IReadOnlyList<string> stepByStepGuideVi)
    {
        if (string.IsNullOrWhiteSpace(findingId))
        {
            throw new ArgumentException("FindingId is required.", nameof(findingId));
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("SourcePath is required.", nameof(sourcePath));
        }

        if (sourceSizeInBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSizeInBytes));
        }

        FindingId = findingId.Trim();
        SourcePath = NormalizeWindowsPath(sourcePath);
        SourceSizeInBytes = sourceSizeInBytes;
        RecommendedTargetDrive = string.IsNullOrWhiteSpace(recommendedTargetDrive) ? null : recommendedTargetDrive.Trim();
        RecommendedTargetPath = string.IsNullOrWhiteSpace(recommendedTargetPath) ? null : recommendedTargetPath.Trim();
        RequiredBytesWithBuffer = (long)Math.Ceiling(sourceSizeInBytes * 1.20d);
        AvailableTargetBytes = Math.Max(0, availableTargetBytes);
        HasSufficientSpace = RecommendedTargetDrive is not null && AvailableTargetBytes >= RequiredBytesWithBuffer;
        StepByStepGuideVi = stepByStepGuideVi?.ToArray() ?? Array.Empty<string>();
    }

    internal static string NormalizeWindowsPath(string rawPath)
    {
        var full = Path.GetFullPath(rawPath.Trim());
        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return full;
        }

        return Path.TrimEndingDirectorySeparator(full);
    }
}
