extern alias OutboundHttpLookalike;

namespace BehaviorDocumentation.OutboundHttp;

/// <summary>
/// Calls the foreign assembly's <c>System.Net.Http.HttpClient</c> (identical qualified type name,
/// method names and signatures) resolved through an explicit <c>extern alias</c>. The foreign
/// assembly name and public key differ from the framework <c>System.Net.Http</c>, so exact-identity
/// admission must stay completely silent: no fact, no scenario node, no diagnostic.
/// </summary>
public sealed class LookalikeCalls
{
    private const string ResourceUri = "https://example.test/resource";

    private readonly OutboundHttpLookalike::System.Net.Http.HttpClient client = new();

    public System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> Get()
    {
        return this.client.GetAsync(ResourceUri);
    }

    public System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> Post()
    {
        var content = new System.Net.Http.StringContent("{}");
        return this.client.PostAsync(ResourceUri, content);
    }
}
