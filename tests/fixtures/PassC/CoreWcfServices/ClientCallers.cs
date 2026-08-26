using System.Globalization;

namespace CoreWcfServices;

// Real, compilable, guarded call sites exercising every supported service-client invocation result
// claim (Discarded/ResultAssigned/ResultReturned/Unclaimed), the source/generated client-boundary
// split, the fault-declaring operation join, and the negative lookalikes (ambiguous interface-typed
// receiver, mismatched-contract client, field store, discard assignment, argument pass-through) that
// must all fail closed rather than admit an invocation. Every client is received as a parameter
// (never constructed here) so these call sites stay small and do not need a real Binding/EndpointAddress.
public sealed class CalculatorClientCaller
{
    private double _storedResult;

    // Positive: Discarded — the call is the entire expression statement; no response claim.
    public void CallDiscarded(CalculatorSourceClient client, double n1, double n2)
    {
        client.Add(n1, n2);
    }

    // Positive: ResultAssigned — the call result is assigned to a local variable.
    public double CallAssigned(CalculatorSourceClient client, double n1, double n2)
    {
        var sum = client.Add(n1, n2);
        return sum;
    }

    // Positive: ResultReturned — the call result is the value of a return statement.
    public double CallReturned(CalculatorSourceClient client, double n1, double n2)
        => client.Add(n1, n2);

    // Positive: Unclaimed — chained member access on the call result.
    public string CallUnclaimed(CalculatorSourceClient client, double n1, double n2)
        => client.Add(n1, n2).ToString(CultureInfo.InvariantCulture);

    // Positive: multiplicity/chronology — two distinct call occurrences to the same operation with
    // different arguments must both admit independent invocations in source order.
    public double CallTwice(CalculatorSourceClient client, double a, double b, double c, double d)
    {
        var first = client.Add(a, b);
        var second = client.Add(c, d);
        return first + second;
    }

    // Positive: exercises the GeneratedClient boundary classification through the same admitted
    // contract operation.
    public double CallGeneratedClient(CalculatorGeneratedClient client, double n1, double n2)
        => client.Add(n1, n2);

    // Positive: fault-declaring operation. SquareRoot carries
    // [FaultContract(typeof(NegativeSquareRootFault))] on the admitted contract, so its invocation
    // must join a declared-fault claim; this remains a declaration join only, never a thrown/observed
    // fault claim.
    public double CallFaultDeclaringOperation(CalculatorSourceClient client, double n1)
        => client.SquareRoot(n1);

    // Negative: ambiguous receiver. The receiver is statically typed as the interface, not the
    // concrete client type, so TargetMethod resolves to the interface member (never a class), which
    // must never admit an invocation despite calling a real, admitted client instance at runtime.
    public double CallThroughInterfaceTypedReceiver(ICalculatorService client, double n1, double n2)
        => client.Add(n1, n2);

    // Negative: mismatched-contract call. MismatchedContractClient (Services/HostChainNegatives.cs)
    // derives ClientBase<ICalculatorService> but separately implements the unrelated, independently
    // admitted classic-family IClassicEchoService directly. Echo is not part of the contract
    // ClientBase was constructed with, so calling it must never admit a client invocation even though
    // the receiver type does carry a client boundary for a different operation.
    public string CallThroughMismatchedContractClient(MismatchedContractClient client, string value)
        => client.Echo(value);

    // Negative: field store. The result is stored to a field rather than assigned to a local or
    // returned; must classify as Unclaimed, never as ResultAssigned.
    public void CallStoredToField(CalculatorSourceClient client, double n1, double n2)
    {
        _storedResult = client.Add(n1, n2);
    }

    // Negative: discard assignment. `_ = ...` must classify as Unclaimed, never as Discarded or
    // ResultAssigned.
    public void CallDiscardAssignment(CalculatorSourceClient client, double n1, double n2)
    {
        _ = client.Add(n1, n2);
    }

    // Negative: passed as an argument. Must classify as Unclaimed.
    public string CallPassedAsArgument(CalculatorSourceClient client, double n1, double n2)
        => Describe(client.Add(n1, n2));

    private static string Describe(double value) => value.ToString(CultureInfo.InvariantCulture);
}
