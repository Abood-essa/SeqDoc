using ZiggyCreatures.Caching.Fusion;

namespace AdvancedAnalysis.FusionCacheCallbacks;

/// <summary>
/// Neutral cached payload used by the fixture; intentionally unrelated to any application domain.
/// </summary>
public sealed record CacheRecord(int Id, string Name);

/// <summary>
/// Source-backed lookup used as the single invocation inside the admitted cache-miss factory.
/// </summary>
public static class RecordStore
{
    /// <summary>Resolves one record; the cancellation token keeps the source invocation async-shaped.</summary>
    public static Task<CacheRecord?> FindAsync(int id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(id == 0 ? null : new CacheRecord(id, $"record-{id}"));
    }
}

/// <summary>
/// Admitted and unsupported FusionCache 2.6.0 <c>GetOrSetAsync</c> call shapes. Only
/// <see cref="GetByIdAsync"/> matches the exact supported declaration: key, anonymous
/// cancellation-token factory (declaration ordinal 2) with one source invocation, and an options
/// callback that sets the entry duration. The tagged, duration, fallback-value, and factory-context
/// overloads are separate metadata declarations and must never be presented as the exact contract.
/// </summary>
public static class CacheCallbacks
{
    private static readonly string[] RecordTags = ["records"];

    /// <summary>
    /// Exact supported source call: key + anonymous async cancellation-token factory whose body
    /// performs one source invocation and returns its value + options callback setting a duration.
    /// </summary>
    public static async Task<CacheRecord?> GetByIdAsync(IFusionCache cache, int id)
    {
        var key = $"record:{id}";
        return await cache.GetOrSetAsync(
            key,
            async cancellationToken =>
            {
                var record = await RecordStore.FindAsync(id, cancellationToken);
                return record;
            },
            options => options.SetDuration(TimeSpan.FromMinutes(5)));
    }

    /// <summary>Tagged overload: adds a tag collection after the options callback.</summary>
    public static async Task<CacheRecord?> GetWithTagsAsync(IFusionCache cache, int id)
    {
        var key = $"record:{id}";
        return await cache.GetOrSetAsync(
            key,
            async cancellationToken =>
            {
                var record = await RecordStore.FindAsync(id, cancellationToken);
                return record;
            },
            options => options.SetDuration(TimeSpan.FromMinutes(5)),
            tags: RecordTags);
    }

    /// <summary>Duration overload: replaces the options callback with a plain TimeSpan.</summary>
    public static async Task<CacheRecord?> GetWithDurationAsync(IFusionCache cache, int id)
    {
        var key = $"record:{id}";
        return await cache.GetOrSetAsync(
            key,
            async cancellationToken =>
            {
                var record = await RecordStore.FindAsync(id, cancellationToken);
                return record;
            },
            TimeSpan.FromMinutes(1));
    }

    /// <summary>Fallback-value overload: adds a failback value next to the factory.</summary>
    public static async Task<CacheRecord?> GetWithFallbackAsync(IFusionCache cache, int id)
    {
        var key = $"record:{id}";
        return await cache.GetOrSetAsync(
            key,
            async cancellationToken =>
            {
                var record = await RecordStore.FindAsync(id, cancellationToken);
                return record;
            },
            new CacheRecord(id, "fallback"));
    }

    /// <summary>Factory-context overload: the factory receives a factory execution context.</summary>
    public static async Task<CacheRecord?> GetWithFactoryContextAsync(IFusionCache cache, int id)
    {
        var key = $"record:{id}";
        return await cache.GetOrSetAsync(
            key,
            async (FusionCacheFactoryExecutionContext<CacheRecord?> context, CancellationToken cancellationToken) =>
            {
                _ = context;
                var record = await RecordStore.FindAsync(id, cancellationToken);
                return record;
            });
    }

    /// <summary>
    /// Arbitrary delegate variable bound to the supported overload; the collector must never invent
    /// an exact target or boundary from a variable.
    /// </summary>
    public static async Task<CacheRecord?> GetFromDelegateVariableAsync(IFusionCache cache, int id)
    {
        var key = $"record:{id}";
        Func<CancellationToken, Task<CacheRecord?>> factory = async cancellationToken =>
        {
            var record = await RecordStore.FindAsync(id, cancellationToken);
            return record;
        };
        return await cache.GetOrSetAsync(key, factory, options => options.SetDuration(TimeSpan.FromMinutes(5)));
    }
}
