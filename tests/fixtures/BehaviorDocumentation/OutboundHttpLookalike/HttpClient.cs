namespace System.Net.Http;

/// <summary>
/// Foreign lookalike: same namespace, simple name, method names and signatures as
/// <c>System.Net.Http.HttpClient</c> in the framework, but compiled into a different assembly with a
/// different assembly name and public key. Exact-identity admission must reject every call bound to
/// this type; only the framework <c>System.Net.Http</c> assembly identity is supported.
/// </summary>
public sealed class HttpClient
{
    public global::System.Threading.Tasks.Task<HttpResponseMessage> GetAsync(string requestUri)
    {
        _ = requestUri;
        return global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage());
    }

    public global::System.Threading.Tasks.Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content)
    {
        _ = requestUri;
        _ = content;
        return global::System.Threading.Tasks.Task.FromResult(new HttpResponseMessage());
    }
}
