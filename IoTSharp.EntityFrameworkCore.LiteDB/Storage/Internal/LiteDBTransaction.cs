namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed class LiteDBTransaction(LiteDBTransactionManager manager) : IDbContextTransaction
{
    public Guid TransactionId { get; } = Guid.NewGuid();
    public bool IsCompleted { get; set; }
    public void Commit() { CheckActive(); manager.CommitTransaction(); }
    public void Rollback() { CheckActive(); manager.RollbackTransaction(); }
    public Task CommitAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); Commit(); return Task.CompletedTask; }
    public Task RollbackAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); Rollback(); return Task.CompletedTask; }
    public void Dispose() { if (!IsCompleted) Rollback(); }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    private void CheckActive() { if (IsCompleted) throw new InvalidOperationException("This LiteDB transaction is complete."); }
}
