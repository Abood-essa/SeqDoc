using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;

namespace SeqDoc.FrameworkModels.AspNetCore;

/// <summary>Exact ASP.NET Core 10 Minimal API registration model.</summary>
public sealed class AspNetCoreMinimalApiModel : IFrameworkBehaviorModel
{
    public const string ModelIdValue = "seqdoc.aspnetcore.minimal-api";
    public const string ModelVersionValue = "1.0.0";
    public FrameworkModelDescriptor Descriptor { get; } = new(ModelIdValue, ModelVersionValue, "ASP.NET Core Minimal APIs", 101);

    public bool IsApplicable(FrameworkDetectionContext context) => context.ProgramIndex.References.Any(reference =>
        reference.Identity.Contains("Microsoft.AspNetCore", StringComparison.Ordinal));

    public ValueTask<ModelResult> AnalyzeSymbolAsync(SymbolDescriptor symbol, FrameworkAnalysisContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(ModelResult.Unrecognized);

    public ValueTask<ModelResult> AnalyzeOperationAsync(OperationDescriptor operation, FrameworkAnalysisContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRecognizable(operation.TargetIdentity))
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        var target = operation.TargetIdentity!;
        var reason = UnsupportedReason(target);
        if (reason is not null)
        {
            return ValueTask.FromResult(new ModelResult(false,
                diagnostics: [AspNetCoreMinimalApiModelDiagnostics.UnsupportedShape(context.Profile.Id, operation.Id, reason)]));
        }

        foreach (var step in operation.RouteGroup?.Steps ?? [])
        {
            if (!IsExactMapGroup(step.TargetIdentity))
            {
                return ValueTask.FromResult(new ModelResult(false,
                    diagnostics: [AspNetCoreMinimalApiModelDiagnostics.UnsupportedShape(
                        context.Profile.Id, operation.Id, "contains an invalid MapGroup receiver step")]));
            }
        }

        if (operation.CallbackTarget is not { } callback || !HasExactCallbackShape(callback))
        {
            return ValueTask.FromResult(Unsupported(context, operation, "has no exact compiler-bound callback"));
        }

        var routeArguments = operation.ConstantArguments
            .Where(argument => argument.Ordinal == 1 && argument.FullyQualifiedType == "System.String")
            .ToArray();
        var route = routeArguments.Length == 1 ? routeArguments[0].Value : null;
        if (string.IsNullOrWhiteSpace(route))
        {
            return ValueTask.FromResult(Unsupported(context, operation, "has no compiler-proven literal route"));
        }

        var method = target.MethodMetadataName switch { "MapGet" => HttpMethodKind.Get, "MapPost" => HttpMethodKind.Post, "MapPut" => HttpMethodKind.Put, _ => HttpMethodKind.Delete };
        var effective = CombineRoute(operation.RouteGroup, route);
        var root = callback.TargetMethod ?? new MethodId($"method:v1:minimal:{callback.TargetBodyOperation!.Value.Value}");
        var entry = StableIdentity.CreateEntryPointId(new HttpEntryPointIdentityDescriptor(context.Profile.Id, root, method, effective));
        var fact = new MinimalApiRouteFact
        {
            Id = StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
                context.Profile.Id,
                Descriptor.ModelId,
                Descriptor.Version,
                "minimal-api-route",
                new OperationBehaviorFactAnchor(operation.Method, operation.Id),
                0)),
            EntryPointId = entry,
            HandlerRoot = root,
            HandlerKind = callback.Kind switch
            {
                CallbackTargetKind.LocalFunction => MinimalApiHandlerKind.LocalFunction,
                CallbackTargetKind.AnonymousFunction => MinimalApiHandlerKind.AnonymousFunction,
                _ => MinimalApiHandlerKind.NamedMethod,
            },
            HttpMethod = method,
            CanonicalRoute = effective,
            OperationKey = $"{HttpMethodCanonicalToken.Get(method)} {effective.Trim('/')}",
            CallbackBoundaryId = callback.CallbackBoundaryId,
            Evidence = Evidence(operation, context),
            Certainty = operation.Certainty,
        };
        return ValueTask.FromResult(new ModelResult(true, [fact]));
    }

    private static bool HasExactCallbackShape(CallbackTargetDescriptor callback)
        => callback.Kind switch
        {
            CallbackTargetKind.AnonymousFunction => callback.TargetMethod is null && callback.TargetBodyOperation is not null,
            CallbackTargetKind.MethodGroup or CallbackTargetKind.LocalFunction => callback.TargetMethod is not null && callback.TargetBodyOperation is null,
            _ => false,
        };

    private static ModelResult Unsupported(FrameworkAnalysisContext context, OperationDescriptor operation, string reason)
        => new(false, diagnostics: [AspNetCoreMinimalApiModelDiagnostics.UnsupportedShape(context.Profile.Id, operation.Id, reason)]);

    private static bool IsRecognizable(FrameworkMethodIdentity? target)
        => target is not null
            && target.AssemblyIdentity == "Microsoft.AspNetCore.Routing"
            && target.ContainingMetadataType == "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions"
            && target.MethodMetadataName is "MapGet" or "MapPost" or "MapPut" or "MapDelete";

    private static string? UnsupportedReason(FrameworkMethodIdentity target)
    {
        if (target.GenericArity != 0 || target.Parameters.Length != 3)
        {
            return "does not have the supported Map* arity or parameter count";
        }
        if (target.Parameters[0] != new ParameterIdentityDescriptor(ParameterRefKind.None, "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder"))
        {
            return "does not have the supported IEndpointRouteBuilder receiver parameter";
        }
        if (target.Parameters[1] != new ParameterIdentityDescriptor(ParameterRefKind.None, "System.String"))
        {
            return "does not have the supported string route parameter";
        }
        if (target.Parameters[2] != new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Delegate"))
        {
            return "does not have the supported delegate callback parameter";
        }
        if (target.ReturnType != "Microsoft.AspNetCore.Builder.RouteHandlerBuilder")
        {
            return "does not return RouteHandlerBuilder";
        }
        if (target.AssemblyVersion != "10.0.0.0")
        {
            return "has an unsupported ASP.NET Core assembly version";
        }
        return null;
    }

    private static bool IsExactMapGroup(FrameworkMethodIdentity target)
        => target.AssemblyIdentity == "Microsoft.AspNetCore.Routing"
            && target.ContainingMetadataType == "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions"
            && target.MethodMetadataName == "MapGroup"
            && target.GenericArity == 0
            && target.Parameters.Length == 2
            && target.Parameters[0] == new ParameterIdentityDescriptor(ParameterRefKind.None, "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder")
            && target.Parameters[1] == new ParameterIdentityDescriptor(ParameterRefKind.None, "System.String")
            && target.ReturnType == "Microsoft.AspNetCore.Routing.RouteGroupBuilder"
            && target.AssemblyVersion == "10.0.0.0";

    private ImmutableArray<EvidenceRef> Evidence(OperationDescriptor operation, FrameworkAnalysisContext context)
    {
        var artifact = $"{Descriptor.ModelId}:{Descriptor.Version}";
        var id = StableIdentity.CreateEvidenceIdV2(new EvidenceIdentityDescriptor(EvidenceKind.FrameworkModel, artifact, null, null, null, null, operation.Certainty, Descriptor.ModelId, Descriptor.Version, operation.Id.Value));
        return [new EvidenceRef(id, EvidenceKind.FrameworkModel, artifact, null, null, operation.Id.Value, operation.Certainty, operation.Evidence, Descriptor.ModelId, Descriptor.Version)];
    }

    private static string CombineRoute(FrameworkRouteGroupDescriptor? group, string route)
    {
        var parts = (group?.Prefixes ?? []).SelectMany(prefix => prefix.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .Concat(route.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
        if (parts.Length == 0)
        {
            return "/";
        }
        return "/" + string.Join("/", parts);
    }
}
