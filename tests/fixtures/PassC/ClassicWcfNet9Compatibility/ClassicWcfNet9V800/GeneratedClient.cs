using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Channels;

namespace ClassicWcfNet9V800;

// Classic System.ServiceModel family pinned to the measured net9.0 / System.ServiceModel.Primitives
// 8.0.0.0 compatibility tuple. Mirrors the shape dotnet-svcutil emits: a [ServiceContract] interface
// with [OperationContract] (and one [FaultContract]) members, and a partial ClientBase<TContract>
// proxy carrying the exact [System.CodeDom.Compiler.GeneratedCodeAttribute] marker.

[DataContract]
public sealed class NegativeInputFault
{
    [DataMember]
    public string Message { get; set; } = string.Empty;
}

[System.ServiceModel.ServiceContract(ConfigurationName = "ClassicWcfNet9V800.ICalculatorClient")]
public interface ICalculatorClient
{
    [System.ServiceModel.OperationContract(Action = "urn:ICalculatorClient/Add", ReplyAction = "*")]
    double Add(double n1, double n2);

    [System.ServiceModel.OperationContract(Action = "urn:ICalculatorClient/SquareRoot", ReplyAction = "*")]
    [System.ServiceModel.FaultContract(typeof(NegativeInputFault))]
    double SquareRoot(double n1);
}

[System.CodeDom.Compiler.GeneratedCodeAttribute("dotnet-svcutil", "2.1.0")]
public partial class CalculatorClient : System.ServiceModel.ClientBase<ICalculatorClient>, ICalculatorClient
{
    public CalculatorClient(Binding binding, EndpointAddress remoteAddress)
        : base(binding, remoteAddress)
    {
    }

    public double Add(double n1, double n2)
        => Channel.Add(n1, n2);

    public double SquareRoot(double n1)
        => Channel.SquareRoot(n1);
}

// Real, compilable call site with constant arguments: exercises the visible outbound-client message
// and the result-claim classification through the production Roslyn -> CoreWcfServiceModel ->
// ScenarioGraphBuilder -> DocumentationPlanner path.
public sealed class CalculatorCaller
{
    private double _lastResult;

    public double CallAdd(CalculatorClient client)
    {
        var sum = client.Add(2d, 3d);
        _lastResult = sum;
        return sum;
    }

    public double LastResult => _lastResult;
}

// Same-shaped real-compilable negative for issue #41 R2: a contract interface with NO [ServiceContract]
// never resolves to an admitted tuple, so nothing on UnattributedClient may admit — no client boundary,
// no client invocation, no capability, and no ClientOperationInvocation scenario node — even though the
// ClientBase<TContract> base type is the real 8.0.0.0 identity (HasClientBase is true).
public interface IUnattributedContract
{
    [System.ServiceModel.OperationContract(Action = "urn:IUnattributedContract/Describe", ReplyAction = "*")]
    int Describe(int value);
}

public partial class UnattributedClient : System.ServiceModel.ClientBase<IUnattributedContract>, IUnattributedContract
{
    public UnattributedClient(Binding binding, EndpointAddress remoteAddress)
        : base(binding, remoteAddress)
    {
    }

    public int Describe(int value)
        => Channel.Describe(value);
}

public sealed class UnattributedCaller
{
    public int Call(UnattributedClient client)
        => client.Describe(7);
}
