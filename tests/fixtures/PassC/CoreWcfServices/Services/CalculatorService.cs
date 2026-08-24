using System.Runtime.Serialization;
using CoreWCF;

namespace CoreWcfServices;

[DataContract]
public sealed class NegativeSquareRootFault
{
    [DataMember]
    public string Text { get; set; } = string.Empty;
}

[ServiceContract]
public interface ICalculatorService
{
    [OperationContract]
    double Add(double n1, double n2);

    [OperationContract]
    double Subtract(double n1, double n2);

    [OperationContract]
    double Multiply(double n1, double n2);

    [OperationContract]
    double Divide(double n1, double n2);

    [OperationContract]
    [FaultContract(typeof(NegativeSquareRootFault))]
    double SquareRoot(double n1);

    // Deliberately not marked [OperationContract]: a same-shaped sibling operation that must never
    // admit a service operation entry point even though CalculatorService implements it.
    double Modulo(double n1, double n2);
}

public sealed class CalculatorService : ICalculatorService
{
    public double Add(double n1, double n2) => n1 + n2;

    public double Subtract(double n1, double n2) => n1 - n2;

    public double Multiply(double n1, double n2) => n1 * n2;

    public double Divide(double n1, double n2) => n1 / n2;

    public double SquareRoot(double n1) => n1 < 0
        ? throw new FaultException<NegativeSquareRootFault>(new NegativeSquareRootFault { Text = "negative input" })
        : Math.Sqrt(n1);

    public double Modulo(double n1, double n2) => n1 % n2;
}

// Explicit interface implementation shape: the operation is reachable only through the interface,
// never as a public member of the declaring type. Deliberately never registered by Startup.cs, so
// this proves the unregistered-capability boundary: full compiler-proven capability, no hosting.
public sealed class ExplicitCalculatorService : ICalculatorService
{
    double ICalculatorService.Add(double n1, double n2) => n1 + n2;

    double ICalculatorService.Subtract(double n1, double n2) => n1 - n2;

    double ICalculatorService.Multiply(double n1, double n2) => n1 * n2;

    double ICalculatorService.Divide(double n1, double n2) => n1 / n2;

    double ICalculatorService.SquareRoot(double n1) => Math.Sqrt(n1);

    double ICalculatorService.Modulo(double n1, double n2) => n1 % n2;
}
