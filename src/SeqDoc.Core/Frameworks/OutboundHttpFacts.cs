using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Frameworks;

/// <summary>
/// The compiler-proven outbound HTTP request method a supported direct
/// <c>System.Net.Http.HttpClient</c> call boundary represents. <see cref="Unknown"/> is the
/// fail-closed default and never admits boundary wording.
/// </summary>
public enum OutboundHttpRequestKind
{
    Unknown = 0,
    Get = 1,
    Post = 2,
}

/// <summary>
/// Evidenced outbound HTTP request boundary: an exact compiler <c>IInvocationOperation</c> whose
/// original definition is a supported <c>System.Net.Http.HttpClient</c> request overload
/// (<c>GetAsync(string)</c> or <c>PostAsync(string, HttpContent)</c>) admitted atomically for the
/// active compilation profile's <c>System.Net.Http</c> assembly version. This fact proves the
/// call's own compiler identity and request method only; it never carries the URI argument and
/// never claims a remote request completed, a response was received, or a status/fault occurred.
/// </summary>
/// <remarks>
/// The stable identity is derived through the shared <see cref="StableIdentity.CreateBehaviorFactId"/>
/// helper with a <see cref="BehaviorFactIdentityDescriptor"/> whose fact kind encodes the admitted
/// request row (<c>outbound-http-request:get</c> / <c>outbound-http-request:post</c>) and whose
/// operation anchor carries the caller method and invocation operation. The active compilation profile
/// pins the single admitted assembly version, so profile + fact kind + operation anchor fully
/// determine the admitted row. No checkout path, URI value, source text, scheduling, timestamp, or
/// encounter order participates.
/// </remarks>
public sealed record OutboundHttpRequestFact : BehaviorFact
{
    /// <summary>The exact method whose body contains the admitted invocation (the scenario-root candidate).</summary>
    public required MethodId CallerMethod { get; init; }

    /// <summary>The exact compiler operation identity of the admitted invocation.</summary>
    public required OperationId InvocationOperation { get; init; }

    /// <summary>The compiler-proven request method the admitted overload represents.</summary>
    public required OutboundHttpRequestKind RequestKind { get; init; }

    /// <summary>The complete compiler-projected identity of the admitted <c>HttpClient</c> overload.</summary>
    public required FrameworkMethodIdentity FrameworkMethodIdentity { get; init; }
}
