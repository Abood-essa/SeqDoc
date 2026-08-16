namespace A.Caller;

/// <summary>
/// Caller project that invokes the cross-project contract with an exact source method group. The
/// project is listed before the contract in the solution, so order-independent preloading is the only
/// way to resolve the contract body and target method.
/// </summary>
public static class Caller
{
    /// <summary>Calls the Z contract RunOnce with the exact public static method group CallbackTarget.</summary>
    public static void InvokeCrossProject()
    {
        Z.Contracts.ZCallbackContracts.RunOnce(Z.Contracts.ZCallbackContracts.CallbackTarget);
    }
}
