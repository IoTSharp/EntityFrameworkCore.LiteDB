using IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

// Provider infrastructure intentionally extends EF's internal detector; rerun the
// complex tracking contracts whenever the pinned EF Core version changes.
#pragma warning disable EF1001

namespace IoTSharp.EntityFrameworkCore.LiteDB.ChangeTracking.Internal;

/// <summary>Tracks optional complex presence separately from nullable scalar leaves.</summary>
internal sealed class LiteDBChangeDetector(IDiagnosticsLogger<DbLoggerCategory.ChangeTracking> logger, ILoggingOptions loggingOptions)
    : ChangeDetector(logger, loggingOptions)
{
    public override void DetectChanges(IStateManager stateManager)
    {
        foreach (var entry in stateManager.Entries)
            UpdatePresence(entry);
        base.DetectChanges(stateManager);
    }

    public override void DetectChanges(InternalEntityEntry entry)
    {
        UpdatePresence(entry);
        base.DetectChanges(entry);
    }

    private static void UpdatePresence(InternalEntityEntry entry)
    {
        if (entry.EntityState is EntityState.Detached or EntityState.Deleted) return;
        foreach (var property in entry.EntityType.GetProperties().Where(LiteDBComplexPresence.IsPresenceProperty))
            entry.SetProperty(property, LiteDBComplexPresence.IsPresent(property, entry.Entity), isMaterialization: false);
    }
}
#pragma warning restore EF1001
