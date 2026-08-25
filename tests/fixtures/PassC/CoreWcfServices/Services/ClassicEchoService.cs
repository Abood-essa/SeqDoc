namespace CoreWcfServices;

// Classic System.ServiceModel family, entirely separate from the CoreWCF family used by
// ICalculatorService. Proves the classic-WCF admission path through the real compiler pipeline.
// Deliberately unregistered (no Startup.cs endpoint), so it also exercises the
// capability-without-registration boundary for the classic family.
[System.ServiceModel.ServiceContract]
public interface IClassicEchoService
{
    [System.ServiceModel.OperationContract]
    string Echo(string value);
}

public sealed class ClassicEchoService : IClassicEchoService
{
    public string Echo(string value) => value;
}
