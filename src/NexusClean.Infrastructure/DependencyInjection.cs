using Microsoft.Extensions.DependencyInjection;
using NexusClean.Application.Models;
using NexusClean.Application.Orchestration;
using NexusClean.Application.UseCases;
using NexusClean.Domain.Guards;
using NexusClean.Domain.Interfaces;
using NexusClean.Infrastructure.FileSystem;
using NexusClean.Infrastructure.OS;
using NexusClean.Infrastructure.Persistence;
using NexusClean.Infrastructure.Strategies;

namespace NexusClean.Infrastructure;

/// <summary>
/// Centralized Clean Architecture Dependency Injection registration shared by NexusClean.Cli and NexusClean.UI.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddNexusCleanCore(
        this IServiceCollection services,
        string? customCatalogPath = null,
        string? customAuditLogPath = null)
    {
        // 1. Domain Safety Guards (Chain of Responsibility)
        services.AddSingleton<ISafetyGuard, RootSystemFolderGuard>();
        services.AddSingleton<ISafetyGuard, BrowserProfilePreservationGuard>();
        services.AddSingleton<ISafetyGuard, RunningProcessLockGuard>();
        services.AddSingleton<ISafetyGuard, AdminElevationGuard>();

        // 2. Infrastructure FileSystem, OS & Persistence Adapters
        services.AddSingleton<IDirectorySizer>(_ => new MultiThreadedDirectorySizer());
        services.AddSingleton<IProcessLockInspector, WindowsProcessInspector>();
        services.AddSingleton<ISystemSnapshotProvider, WindowsSystemSnapshotProvider>();
        services.AddSingleton<ISignatureRuleCatalogProvider>(_ => new JsonSignatureRuleProvider(customCatalogPath));
        services.AddSingleton<IAuditLogRepository>(_ => new JsonlAuditLogRepository(customAuditLogPath));
        services.AddSingleton<VssShadowStorageManager>();
        services.AddSingleton<IElevatedAdminExecutor, ElevatedAdminScriptRunner>();
        services.AddSingleton<IDiskGrowthWatcher, DiskGrowthFileSystemWatcher>();

        // 3. Application Use Cases & Orchestrator
        services.AddSingleton<EvaluateImpactUseCase>();
        services.AddSingleton<PlanSecondaryRelocationUseCase>();
        services.AddSingleton<AnalyzeDriveUseCase>();
        services.AddSingleton<SimulateCleanupUseCase>();
        services.AddSingleton<ExecuteCleanupUseCase>();
        services.AddSingleton<DiskAnalysisOrchestrator>();

        // 4. All 8 Canonical Cleanup Strategies
        services.AddSingleton<ICleanupStrategy, BlockedNoOpStrategy>();
        services.AddSingleton<ICleanupStrategy, FullDirectoryDeleteStrategy>();
        services.AddSingleton<ICleanupStrategy, DeleteUnlockedItemsStrategy>();
        services.AddSingleton<ICleanupStrategy, SelectiveSubfolderPurgeStrategy>();
        services.AddSingleton<ICleanupStrategy, SemVerDuplicatePruneStrategy>();
        services.AddSingleton<ICleanupStrategy, SecondaryDriveRelocateStrategy>();
        services.AddSingleton<ICleanupStrategy, ResizeShadowStorageAdminStrategy>();
        services.AddSingleton<ICleanupStrategy, RelocateWslAdminStrategy>();

        return services;
    }
}
