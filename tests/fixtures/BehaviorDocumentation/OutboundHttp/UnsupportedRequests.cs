namespace BehaviorDocumentation.OutboundHttp;

/// <summary>
/// Recognized-but-unsupported BCL siblings. Assembly name, public key token, containing type,
/// method name and arity agree with the recognizable family, but the overload/shape is not an
/// admitted row. For an applicable net9.0/net10.0 profile each method must yield exactly one ordered
/// <c>SEQHTTP001</c> diagnostic and no fact, node, wording or Mermaid message.
/// </summary>
public sealed class UnsupportedRequests
{
    private const string ResourceUri = "https://example.test/resource";

    public System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsyncSibling()
    {
        var client = new System.Net.Http.HttpClient();
        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, ResourceUri);
        return client.SendAsync(request);
    }

    public System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> GetAsyncUriOverload()
    {
        var client = new System.Net.Http.HttpClient();
        return client.GetAsync(new System.Uri(ResourceUri));
    }

    public System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> GetAsyncCancellationTokenOverload()
    {
        var client = new System.Net.Http.HttpClient();
        return client.GetAsync(ResourceUri, System.Threading.CancellationToken.None);
    }

    public System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> GetAsyncCompletionOptionOverload()
    {
        var client = new System.Net.Http.HttpClient();
        return client.GetAsync(ResourceUri, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
    }
}
