namespace Z.Contracts;

/// <summary>
/// Cross-project callback contract surface consumed by the A.Caller project. The contract and its
/// target method live in this referenced project so caller-first solution order can never resolve
/// them unless the product preloads every project context before callback collection.
/// </summary>
public static class ZCallbackContracts
{
    /// <summary>Internal counter mutated by the public target method; intentionally not a public field.</summary>
    internal static int CallbackCount { get; private set; }

    /// <summary>Invokes the callback exactly once.</summary>
    public static void RunOnce(Action callback)
    {
        callback();
    }

    /// <summary>Public static method-group target the caller passes into <see cref="RunOnce"/>.</summary>
    public static void CallbackTarget()
    {
        CallbackCount++;
    }
}
