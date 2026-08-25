namespace Fake.ServiceModel
{
    [AttributeUsage(AttributeTargets.Interface)]
    public sealed class ServiceContractAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class OperationContractAttribute : Attribute
    {
    }
}

namespace CoreWcfServices
{
    // Same-shaped negative: an admitted [OperationContract] operation on an interface that never
    // carries [ServiceContract] must never admit a service operation entry point.
    public interface IUtility
    {
        [CoreWCF.OperationContract]
        string Ping();
    }

    public sealed class UtilityHelper : IUtility
    {
        public string Ping() => "pong";
    }

    // Lookalike shape: fully qualified attribute identities from a foreign namespace. The model must
    // never recognize these from simple attribute names alone.
    [Fake.ServiceModel.ServiceContract]
    public interface IFakeService
    {
        [Fake.ServiceModel.OperationContract]
        string Echo(string value);
    }

    public sealed class FakeService : IFakeService
    {
        public string Echo(string value) => value;
    }

    // Mixed-family negative: the interface carries the CoreWCF ServiceContract attribute, but the
    // operation is decorated with the classic System.ServiceModel OperationContract attribute instead of
    // CoreWCF's own. Both are real, exact, admitted-namespace attributes individually, but never as a
    // mixed pair; the model must reject this rather than treat the families as interchangeable.
    [CoreWCF.ServiceContract]
    public interface IMixedFamilyService
    {
        [System.ServiceModel.OperationContract]
        string Ping();
    }

    public sealed class MixedFamilyService : IMixedFamilyService
    {
        public string Ping() => "pong";
    }
}
