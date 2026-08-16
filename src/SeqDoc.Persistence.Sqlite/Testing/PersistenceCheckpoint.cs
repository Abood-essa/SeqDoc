namespace SeqDoc.Persistence.Sqlite.Testing;

internal enum PersistenceCheckpoint
{
    AfterStaging,
    AfterValidation,
    AfterFirstPointerReplaced,
    BeforeActivationCommit,
}

internal interface IPersistenceCheckpointObserver
{
    ValueTask ReachedAsync(PersistenceCheckpoint stage, CancellationToken cancellationToken);
}

internal sealed class NoOpPersistenceCheckpointObserver : IPersistenceCheckpointObserver
{
    public ValueTask ReachedAsync(PersistenceCheckpoint stage, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
