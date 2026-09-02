namespace BehaviorDocumentation.OutboundHttp;

/// <summary>
/// Each method is an admitted scenario-root candidate (selected by exact method id through
/// configured roots) whose Method Flow contains exactly one supported direct
/// <see cref="System.Net.Http.HttpClient"/> request call:
/// <c>GetAsync(string)</c> for <see cref="Get"/> and <c>PostAsync(string, HttpContent)</c> for
/// <see cref="Post"/>. The credential-shaped constants below are never allowed to appear in the
/// typed fact, the scenario node, the wording, the Mermaid, or any generated file.
/// </summary>
public sealed class SupportedRequests
{
    private const string ResourceUri = "https://example.test/resource";

    private const string CredentialShapedResourceUri =
        "https://example.test/resource?access_token=" + "AKIA" + "IOSFODNN7EXAMPLE";

    private const string CredentialShapedAuthorizationHeader =
        "Bearer " + "sk_" + "live_" + "51H8xExAmPlEtOkEnValue0123456789abcdef";

    private const string RequestBody = "{\"ping\":true}";

    private readonly string endpoint = ResourceUri;

    public System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> Get()
    {
        var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", CredentialShapedAuthorizationHeader);
        return client.GetAsync(this.endpoint);
    }

    public System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> Post()
    {
        var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", CredentialShapedAuthorizationHeader);
        var content = new System.Net.Http.StringContent(RequestBody);
        return client.PostAsync(CredentialShapedResourceUri, content);
    }

    /// <summary>
    /// A plain non-HTTP scenario-root candidate with a single in-fixture direct call. It exists only
    /// so a regression test can prove that enabling the outbound-HTTP framework model changes nothing
    /// for operations that are not the supported HttpClient call.
    /// </summary>
    public string Describe()
    {
        return this.Format();
    }

    private string Format()
    {
        return this.endpoint.Trim();
    }
}
