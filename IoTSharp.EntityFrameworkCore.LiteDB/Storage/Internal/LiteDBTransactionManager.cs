using System.Transactions;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed class LiteDBTransactionManager(ILiteDBStoreProvider provider) : IDbContextTransactionManager, ITransactionEnlistmentManager, IDisposable
{
    public IDbContextTransaction CurrentTransaction { get; private set; }
    public Transaction EnlistedTransaction => null;

    public IDbContextTransaction BeginTransaction()
    {
        if (CurrentTransaction != null) throw new InvalidOperationException("A LiteDB transaction is already active.");
        provider.Store.BeginTransaction(provider.SessionId);
        return CurrentTransaction = new LiteDBTransaction(this);
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BeginTransaction());
    }

    public void CommitTransaction()
    {
        if (CurrentTransaction == null) throw new InvalidOperationException("No active LiteDB transaction.");
        provider.Store.CommitTransaction(provider.SessionId);
        Complete();
    }

    public void RollbackTransaction()
    {
        if (CurrentTransaction == null) throw new InvalidOperationException("No active LiteDB transaction.");
        provider.Store.RollbackTransaction(provider.SessionId);
        Complete();
    }

    private void Complete()
    {
        ((LiteDBTransaction)CurrentTransaction).IsCompleted = true;
        CurrentTransaction = null;
    }

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CommitTransaction();
        return Task.CompletedTask;
    }

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RollbackTransaction();
        return Task.CompletedTask;
    }

    public void EnlistTransaction(Transaction transaction)
    {
        if (transaction != null) throw new NotSupportedException("LiteDB does not support System.Transactions enlistment. See SUPPORT.md (LDB-002).");
    }

    public void ResetState()
    {
        if (CurrentTransaction != null) RollbackTransaction();
    }

    public Task ResetStateAsync(CancellationToken cancellationToken = default)
    {
        ResetState();
        return Task.CompletedTask;
    }

    public void Dispose() => ResetState();
}
