using System.ServiceModel;
using System.ServiceModel.Channels;

namespace CoreWcfServices;

// A hand-written client boundary: derives from the exact System.ServiceModel.ClientBase<TContract>
// marker for the admitted ICalculatorService contract, with real (non-generated) source bodies that
// forward to the WCF-supplied Channel. This is the "source client" classification.
public sealed class CalculatorSourceClient : ClientBase<ICalculatorService>, ICalculatorService
{
    public CalculatorSourceClient(Binding binding, EndpointAddress address)
        : base(binding, address)
    {
    }

    public double Add(double n1, double n2) => Channel.Add(n1, n2);

    public double Subtract(double n1, double n2) => Channel.Subtract(n1, n2);

    public double Multiply(double n1, double n2) => Channel.Multiply(n1, n2);

    public double Divide(double n1, double n2) => Channel.Divide(n1, n2);

    public double SquareRoot(double n1) => Channel.SquareRoot(n1);

    public double Modulo(double n1, double n2) => Channel.Modulo(n1, n2);
}

// The same client boundary shape, but marked exactly the way tools like dotnet-svcutil mark their
// generated proxy output. A real svcutil run is not available in this offline environment, so this
// is a minimal, clearly-labeled stand-in carrying the same exact compiler-provable marker
// ([System.CodeDom.Compiler.GeneratedCodeAttribute]) a real generated proxy would carry, rather than
// a full generated file. This is the "generated/metadata-only client" classification.
[System.CodeDom.Compiler.GeneratedCodeAttribute("dotnet-svcutil", "2.0.0.0")]
public sealed class CalculatorGeneratedClient : ClientBase<ICalculatorService>, ICalculatorService
{
    public CalculatorGeneratedClient(Binding binding, EndpointAddress address)
        : base(binding, address)
    {
    }

    public double Add(double n1, double n2) => Channel.Add(n1, n2);

    public double Subtract(double n1, double n2) => Channel.Subtract(n1, n2);

    public double Multiply(double n1, double n2) => Channel.Multiply(n1, n2);

    public double Divide(double n1, double n2) => Channel.Divide(n1, n2);

    public double SquareRoot(double n1) => Channel.SquareRoot(n1);

    public double Modulo(double n1, double n2) => Channel.Modulo(n1, n2);
}
