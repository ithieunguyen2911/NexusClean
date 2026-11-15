namespace NexusClean.Domain.Enums;

/// <summary>
/// Canonical cleanup actions unifying docs/02-OOD-CLEAN-ARCHITECTURE-SPEC.md and docs/03-SEED-SIGNATURE-RULES.json.
/// </summary>
public enum CleanupActionType
{
    DeleteContents = 0,
    DeleteUnlockedItems = 1,
    SelectiveSubfolderPurge = 2,
    SemVerDuplicatePrune = 3,
    RecommendRelocateToSecondaryDrive = 4,
    RelocateOrUnregisterAdmin = 5,
    ResizeShadowStorageAdmin = 6,
    BlockedNoOp = 7
}
