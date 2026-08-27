namespace AdvancedAnalysis.CallbackBoundaries;

/// <summary>
/// Static callback shapes used to exercise callback-boundary analysis.
/// The fixture intentionally provides callbacks that capture, mutate state, cross
/// overloaded method groups, cross metadata-only boundaries, and throw without
/// logging values.
/// </summary>
public static class CallbackContracts
{
    /// <summary>Internal mutable counter incremented by callbacks; intentionally not a public field.</summary>
    internal static int CallbackCount { get; private set; }

    /// <summary>Invokes the callback exactly once.</summary>
    public static void RunOnce(Action callback)
    {
        callback();
    }

    /// <summary>Invokes the callback only when enabled is true.</summary>
    public static void RunWhen(bool enabled, Action callback)
    {
        if (enabled)
        {
            callback();
        }
    }

    /// <summary>Invokes the callback twice through a counted loop.</summary>
    public static void RunRepeated(Action callback)
    {
        for (int i = 0; i < 2; i++)
        {
            callback();
        }
    }

    /// <summary>Invokes the callback twice with a straight-line body.</summary>
    public static void RunTwice(Action callback)
    {
        callback();
        callback();
    }

    /// <summary>Invokes the callback only on the selected switch branch.</summary>
    public static void RunInSwitch(int choice, Action callback)
    {
        switch (choice)
        {
            case 1:
                callback();
                break;
        }
    }

    /// <summary>Accepts a System.Delegate callback without invoking it; the class parameter is not a supported Action contract.</summary>
    public static void RunUnsupported(Delegate callback)
    {
        _ = callback;
    }

    /// <summary>Invokes the callback only when skip is false; the early return precedes the invoke.</summary>
    public static void RunAfterEarlyReturn(bool skip, Action callback)
    {
        if (skip)
        {
            return;
        }

        callback();
    }

    /// <summary>Invokes an asynchronous callback without awaiting its completion.</summary>
    public static void RunAsync(Func<Task> callback)
    {
        _ = callback();
    }

    /// <summary>Passes a capturing lambda that reads a local sentinel and mutates the counter.</summary>
    public static void InvokeCapturingLambda()
    {
        const int sentinel = 987654321;
        RunOnce(() =>
        {
            if (sentinel == 987654321)
            {
                CallbackCount++;
            }
        });
    }

    /// <summary>Passes a local function as the callback.</summary>
    public static void InvokeLocalFunction()
    {
        void LocalCallback()
        {
            CallbackCount++;
        }

        RunOnce(LocalCallback);
    }

    /// <summary>Passes a method group where an incompatible overload is declared first.</summary>
    public static void InvokeOverloadedMethodGroup()
    {
        RunOnce(CallbackTarget);
    }

    /// <summary>
    /// Passes a method group whose accepted body exposes no flattenable member operations; the
    /// boundary must fail closed without projecting an identity instead of failing analysis.
    /// </summary>
    public static void InvokeEmptyMethodGroup()
    {
        RunOnce(EmptyCallbackTarget);
    }

    private static void EmptyCallbackTarget()
    {
    }

    private static void CallbackTarget(int value)
    {
        // Incompatible overload: an int parameter does not match the parameterless Action target.
        _ = value;
    }

    private static void CallbackTarget()
    {
        CallbackCount++;
    }

    /// <summary>Invokes the callback once through a conditional source contract.</summary>
    public static void InvokeRunWhen()
    {
        RunWhen(true, () => CallbackCount++);
    }

    /// <summary>Invokes the callback twice through a repeated source contract.</summary>
    public static void InvokeTwice()
    {
        RunTwice(() => CallbackCount++);
    }

    /// <summary>Passes a callback held in a delegate variable; the variable makes the target unresolvable.</summary>
    public static void InvokeDelegateVariable()
    {
        Action callback = () => CallbackCount++;
        RunOnce(callback);
    }

    /// <summary>Passes a callback to a metadata-only target whose body is not visible.</summary>
    public static void InvokeMetadataOnly()
    {
        // Task.Run is metadata-only; the callback crosses a boundary with no visible body.
        Task.Run(() => { CallbackCount++; }).Wait();
    }

    /// <summary>Passes an explicitly Action-typed callback into an unsupported System.Delegate source contract.</summary>
    public static void InvokeUnsupported()
    {
        Action typedCallback = () => CallbackCount++;
        RunUnsupported(typedCallback);
    }

    /// <summary>Invokes a local delegate directly; Behavior delegate dispatch stays Unknown.</summary>
    public static void InvokeBehaviorDelegate()
    {
        Action behaviorDelegate = () => CallbackCount++;
        behaviorDelegate();
    }

    /// <summary>Event dispatched directly; Behavior event dispatch stays Unknown.</summary>
    private static event Action? BehaviorCallback = () => { };

    /// <summary>Invokes the private static event; Behavior event dispatch stays Unknown.</summary>
    public static void InvokeBehaviorEvent()
    {
        BehaviorCallback?.Invoke();
    }

    /// <summary>Passes a callback whose local return rejoins the outer caller.</summary>
    public static void InvokeReturningCallback()
    {
        RunOnce(() =>
        {
            CallbackCount++;
            return;
        });
    }

    /// <summary>Invokes a callback that always throws; completion stays Unknown.</summary>
    public static void InvokeThrowingCallback()
    {
        RunOnce(() => throw new InvalidOperationException("CallbackBoundaries throwing callback"));
    }

    /// <summary>Passes a lambda to a base-typed virtual contract whose runtime dispatch is a sealed override.</summary>
    public static void InvokeVirtualContract()
    {
        CallbackContractBase contract = new TwiceOrNeverCallbackOverride(runTwice: true);
        contract.Run(() => CallbackCount++);
    }

    /// <summary>Passes a lambda into a contract with an early return before the invoke; cardinality is not ExactlyOnce.</summary>
    public static void InvokeAfterEarlyReturn()
    {
        RunAfterEarlyReturn(skip: false, () => CallbackCount++);
    }

    /// <summary>Passes an async lambda into an unawaited async contract; completion stays Unknown.</summary>
    public static void InvokeAsyncCallback()
    {
        RunAsync(async () =>
        {
            await Task.Yield();
            CallbackCount++;
        });
    }

    /// <summary>Passes a lambda containing a try/finally mutation into RunOnce; completion stays Unknown.</summary>
    public static void InvokeTryFinallyCallback()
    {
        RunOnce(() =>
        {
            try
            {
                CallbackCount++;
            }
            finally
            {
                CallbackCount++;
            }
        });
    }
}

/// <summary>
/// Dispatchable base contract whose virtual Run invokes the callback exactly once. Runtime dispatch
/// may execute a sealed override instead, so the base body alone must never prove exact cardinality.
/// </summary>
public class CallbackContractBase
{
    /// <summary>Invokes the callback exactly once; overrides may change that behavior.</summary>
    public virtual void Run(Action callback)
    {
        callback();
    }
}

/// <summary>
/// Sealed override that invokes the callback twice or not at all depending on an instance flag; a
/// base-typed caller cannot prove which body executes at runtime.
/// </summary>
public sealed class TwiceOrNeverCallbackOverride : CallbackContractBase
{
    private readonly bool _runTwice;

    /// <summary>Creates an override that invokes the callback twice when runTwice is true and not at all otherwise.</summary>
    public TwiceOrNeverCallbackOverride(bool runTwice)
    {
        _runTwice = runTwice;
    }

    /// <inheritdoc />
    public override void Run(Action callback)
    {
        if (_runTwice)
        {
            callback();
            callback();
        }
    }
}
