using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Core.Frameworks;

/// <summary>Admitted HTTP methods for attribute-routed ASP.NET Core controller actions.</summary>
public enum HttpMethodKind
{
    Unknown = -1,
    Get = 0,
    Post = 1,
    Put = 2,
    Delete = 3,
}

/// <summary>Binding source the ASP.NET Core controller model can prove for one action parameter.</summary>
public enum HttpBindingKind
{
    Route,
    Query,
    Body,
    Service,
    CancellationToken,
    Unknown,
}

/// <summary>Direct ControllerBase result helper admitted by the C-1 model version table.</summary>
public enum HttpOutcomeHelperKind
{
    Ok,
    CreatedAtAction,
    BadRequest,
    NotFound,
    Conflict,
    StatusCode,
}

/// <summary>
/// Evidenced HTTP entry point for one attribute-routed controller action. The root is the exact
/// Program Index method; the canonical route and operation key identify the route/operation identity,
/// and the entry-point identity is scoped by compilation profile, root method, HTTP method, and
/// canonical route.
/// </summary>
public sealed record HttpEntryPointFact : BehaviorFact
{
    public required EntryPointId EntryPointId { get; init; }

    public required MethodId RootMethod { get; init; }

    public required HttpMethodKind HttpMethod { get; init; }

    public required string CanonicalRoute { get; init; }

    public required string OperationKey { get; init; }
}

public enum MinimalApiHandlerKind
{
    NamedMethod,
    LocalFunction,
    AnonymousFunction,
}

/// <summary>Exact compiler-evidenced ASP.NET Core Minimal API registration.</summary>
public sealed record MinimalApiRouteFact : BehaviorFact
{
    public required EntryPointId EntryPointId { get; init; }
    public required MethodId HandlerRoot { get; init; }
    public required MinimalApiHandlerKind HandlerKind { get; init; }
    public required HttpMethodKind HttpMethod { get; init; }
    public required string CanonicalRoute { get; init; }
    public required string OperationKey { get; init; }
    public CallbackBoundaryId? CallbackBoundaryId { get; init; }
}

/// <summary>
/// Binding evidence for one controller action parameter on one entry point. Route is proven only when
/// that specific entry point's canonical route contains an exact placeholder for the parameter; the
/// same parameter on another route stays Unknown because body/query/header inference is not
/// authoritative without exact evidence.
/// </summary>
public sealed record HttpRequestBindingFact : BehaviorFact
{
    public required EntryPointId EntryPointId { get; init; }

    public required MethodId RootMethod { get; init; }

    public required string ParameterName { get; init; }

    public required HttpBindingKind BindingKind { get; init; }

    public string? RoutePlaceholder { get; init; }
}

/// <summary>
/// Direct ControllerBase outcome with an exact status code and the exact invocation operation that
/// produced it. Status codes are admitted only from the supported helper version table or a
/// compiler-proven constant integer argument; non-constant or unsupported values never produce a
/// guessed exact status. The operation identity lets scenario joins pair status arms and structural
/// decision paths to the exact helper call rather than guessing by helper kind alone.
/// </summary>
public sealed record HttpDirectOutcomeFact : BehaviorFact
{
    public required MethodId RootMethod { get; init; }

    /// <summary>Exact invocation operation identity that produced the outcome.</summary>
    public required OperationId Operation { get; init; }

    public required HttpOutcomeHelperKind HelperKind { get; init; }

    public required int StatusCode { get; init; }
}

/// <summary>
/// Canonical uppercase HTTP method tokens used by identity serialization and operation keys. The
/// token is the single source of truth so identity serialization and <see cref="HttpEntryPointFact"/>
/// operation keys always agree, and callers can never create different identities for <c>Get</c>
/// versus <c>GET</c>.
/// </summary>
public static class HttpMethodCanonicalToken
{
    public static string Get(HttpMethodKind method) => method switch
    {
        HttpMethodKind.Get => "GET",
        HttpMethodKind.Post => "POST",
        HttpMethodKind.Put => "PUT",
        HttpMethodKind.Delete => "DELETE",
        _ => throw new ArgumentOutOfRangeException(
            nameof(method),
            $"Unsupported HTTP method '{method}'."),
    };
}
