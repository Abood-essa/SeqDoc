using CoreWCF;

namespace CoreWcfServices;

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

    public double Modulo(double n1, double n2) => n1 % n2;
}

// Explicit interface implementation shape: the operation is reachable only through the interface,
// never as a public member of the declaring type.
public sealed class ExplicitCalculatorService : ICalculatorService
{
    double ICalculatorService.Add(double n1, double n2) => n1 + n2;

    double ICalculatorService.Subtract(double n1, double n2) => n1 - n2;

    double ICalculatorService.Multiply(double n1, double n2) => n1 * n2;

    double ICalculatorService.Divide(double n1, double n2) => n1 / n2;

    double ICalculatorService.Modulo(double n1, double n2) => n1 % n2;
}
