using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SeqDoc.Analysis.Roslyn.Behavior;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.Semantics;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Frameworks;

/// <summary>
/// Projects Roslyn-neutral <see cref="FrameworkAnalysisRequest"/> inputs from compiler symbols during
/// the existing compilation/extraction session. Symbol descriptors carry the controlled eligibility
/// shape projected by <see cref="FrameworkSymbolEligibilityProjector"/>; operation descriptors carry
/// the exact target identity, compiler-proven constant arguments, and additive query receiver-chain
/// and predicate anchors required by the accepted ASP.NET Core controller model and the translation
/// alpha Entity Framework query model. Neither projector decides framework meaning; framework-model
/// rules own that decision. Inputs are produced in the same operation traversal that produced the
/// accepted behavior input, so every descriptor reuses the exact stable MethodId and OperationId
/// anchors.
/// </summary>
internal static class FrameworkAnalysisRequestProjector
{
    private const string EntityFrameworkQueryableExtensionsMetadataName =
        "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";

    public static OperationDescriptor ProjectOperationDescriptor(
        IInvocationOperation call,
        MethodId methodId,
        OperationId operationId,
        ImmutableArray<EvidenceRef> evidence,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
        => ProjectOperationDescriptor(
            call,
            methodId,
            operationId,
            evidence,
            operationById,
            documents,
            models,
            dispatchCancellationToken: default);

    /// <summary>
    /// Projects one source method symbol into a symbol descriptor. Returns null when the method has no
    /// usable containing type, which callers must treat as incomplete eligibility input.
    /// </summary>
    public static SymbolDescriptor? ProjectMethodSymbol(
        IMethodSymbol method,
        StableProjectId project,
        ImmutableArray<EvidenceRef> evidence)
    {
        ArgumentNullException.ThrowIfNull(method);
        var shape = FrameworkSymbolEligibilityProjector.ProjectMethodShape(method, project);
        if (shape is null)
        {
            return null;
        }

        return new SymbolDescriptor(
            shape.MethodSymbol,
            "Method",
            method.MetadataName,
            Document: null,
            SourceStart: 0,
            SourceLength: 0,
            evidence,
            CertaintyLevel.Exact,
            shape);
    }

    /// <summary>
    /// Projects one invocation operation into an operation descriptor carrying the exact target
    /// identity, compiler-proven constant arguments, the additive query receiver-chain and
    /// predicate anchors, and the canonical compiler declaration ordinals actually supplied at the
    /// invocation. The descriptor anchors to the same stable operation id the behavior
    /// traversal assigned, so framework facts and Method Flow operations always agree. The operation
    /// identity map is the exact map built by the accepted behavior flattening pass, so the projected
    /// anchors reuse the same stable OperationIds.
    /// </summary>
    public static OperationDescriptor ProjectOperationDescriptor(
        IInvocationOperation call,
        MethodId methodId,
        OperationId operationId,
        ImmutableArray<EvidenceRef> evidence,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        StableProjectId? project = null,
        CompilationProfileId? profile = null,
        CallbackBoundaryFactSet? callbackFacts = null,
        IReadOnlyDictionary<ILocalSymbol, IOperation>? localInitializers = null,
        CancellationToken dispatchCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        var target = call.TargetMethod;
        var anchor = ProjectSourceAnchor(call.Syntax, documents);
        var constructedTypeArgument = target.TypeArguments.Length == 1
            && target.TypeArguments[0] is INamedTypeSymbol namedTypeArgument
            && IsCanonicalConstructedTypeArgument(namedTypeArgument)
            ? namedTypeArgument
            : null;
        return new OperationDescriptor(
            operationId,
            methodId,
            "Invocation",
            anchor.Document,
            anchor.SourceStart,
            anchor.SourceLength,
            evidence,
            CertaintyLevel.Exact,
            ProjectTargetIdentity(target),
            ProjectConstantArguments(call.Arguments),
            ProjectQueryChain(call, operationById, models),
            ProjectPredicateShape(call, methodId, operationById, documents, models),
            ProjectSuppliedParameterOrdinals(call),
            ProjectCallbackTarget(call, methodId, operationId, operationById, ProjectSuppliedParameterOrdinals(call), documents, project, profile, callbackFacts),
            ProjectRouteGroup(call, operationById, models, localInitializers),
            project is null ? null : ProjectDispatchShape(call, project.Value, evidence, documents, models, dispatchCancellationToken),
            ConstructedType: constructedTypeArgument is null
                ? null
                : FrameworkSymbolEligibilityProjector.ProjectTypeIdentity(constructedTypeArgument),
            ConstructedTypeSymbol: project is { } projectId
                && constructedTypeArgument is not null
                ? RoslynProgramIndexExtractor.CreateSymbolId(constructedTypeArgument, projectId)
                : null,
            ServiceEndpointShape: ProjectServiceEndpointShape(call));
    }

    /// <summary>
    /// Projects the compiler-proven shape of an exact
    /// <c>CoreWCF.Configuration.IServiceBuilder.AddServiceEndpoint&lt;TService, TContract&gt;(Binding, string)</c>
    /// invocation (assembly <c>CoreWCF.Primitives</c>, version 1.9.0.0). Only this exact two-generic,
    /// two-parameter overload is recognized; every other <c>AddServiceEndpoint</c> overload (additional
    /// address/behavior parameters, <c>Uri</c>-typed address, or the non-generic <c>Type</c>-parameter
    /// forms) is an unsupported shape and returns null rather than approximating registration evidence.
    /// The address is captured only when the compiler proves it as a constant string.
    /// </summary>
    private static FrameworkServiceEndpointShapeDescriptor? ProjectServiceEndpointShape(IInvocationOperation call)
    {
        var target = call.TargetMethod;
        if (!IsExactAddServiceEndpoint(target))
        {
            return null;
        }

        var bindingArgument = call.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
        var addressArgument = call.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 1)?.Value;
        var bindingType = bindingArgument is null ? null : UnwrapAllConversionsAndParentheses(bindingArgument).Type;
        if (bindingType is null)
        {
            return null;
        }

        var address = addressArgument is not null
            && UnwrapAllConversionsAndParentheses(addressArgument).ConstantValue is { HasValue: true, Value: string constantAddress }
            ? constantAddress
            : null;

        return new FrameworkServiceEndpointShapeDescriptor(
            target.TypeArguments[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
            target.TypeArguments[1].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
            bindingType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
            address);
    }

    private static bool IsExactAddServiceEndpoint(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return definition.ContainingAssembly?.Identity.Name == "CoreWCF.Primitives"
            && definition.ContainingAssembly.Identity.Version?.ToString() == "1.9.0.0"
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == "CoreWCF.Configuration.IServiceBuilder"
            && definition.MetadataName == "AddServiceEndpoint"
            && definition.Arity == 2
            && definition.Parameters.Length == 2
            && definition.Parameters[0].Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "CoreWCF.Channels.Binding"
            && definition.Parameters[1].Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "System.String";
    }

    /// <summary>
    /// Projects one compiler-proven object creation into the same framework-operation stream as
    /// invocations. Constructor identity and callback binding remain compiler-owned; framework
    /// models decide whether the exact shape is supported.
    /// </summary>
    public static OperationDescriptor ProjectOperationDescriptor(
        IObjectCreationOperation creation,
        MethodId methodId,
        OperationId operationId,
        ImmutableArray<EvidenceRef> evidence,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        StableProjectId? project = null,
        CompilationProfileId? profile = null)
    {
        ArgumentNullException.ThrowIfNull(creation);
        if (creation.Constructor is null)
        {
            throw new ArgumentException("Object creation must have an exact constructor.", nameof(creation));
        }

        var anchor = ProjectSourceAnchor(creation.Syntax, documents);
        return new OperationDescriptor(
            operationId,
            methodId,
            "ObjectCreation",
            anchor.Document,
            anchor.SourceStart,
            anchor.SourceLength,
            evidence,
            CertaintyLevel.Exact,
            ProjectTargetIdentity(creation.Constructor),
            ConstantArguments: ProjectConstantArguments(creation.Arguments),
            CallbackTarget: ProjectCallbackTarget(
                creation.Arguments,
                methodId,
                operationId,
                project,
                profile,
                documents),
            ConstructedType: creation.Type is INamedTypeSymbol constructed
                ? FrameworkSymbolEligibilityProjector.ProjectTypeIdentity(constructed)
                : null,
            ConstructedTypeSymbol: project is { } projectId && creation.Type is INamedTypeSymbol constructedType
                ? RoslynProgramIndexExtractor.CreateSymbolId(constructedType, projectId)
                : null);
    }

    private static FrameworkDispatchShapeDescriptor? ProjectDispatchShape(
        IInvocationOperation call,
        StableProjectId project,
        ImmutableArray<EvidenceRef> operationEvidence,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = call.TargetMethod;
        if (!IsExactMediatRSend(target))
        {
            return null;
        }

        var requestArgument = call.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
        var requestValue = requestArgument is null ? null : UnwrapAllConversionsAndParentheses(requestArgument);
        var requestTypeSymbol = requestValue switch
        {
            IParameterReferenceOperation parameter => parameter.Parameter.Type,
            _ => requestValue?.Type,
        };
        if (requestArgument?.Syntax is { } requestSyntax
            && models.TryGetValue(requestSyntax.SyntaxTree, out var requestModel))
        {
            requestTypeSymbol = requestModel.GetTypeInfo(requestSyntax, cancellationToken).Type ?? requestTypeSymbol;
        }
        var responseTypeSymbol = UnwrapTaskSymbol(call.Type);
        if (requestTypeSymbol is null || responseTypeSymbol is null
            || ContainsTypeParameter(requestTypeSymbol)
            || ContainsTypeParameter(responseTypeSymbol))
        {
            return null;
        }

        var requestType = requestTypeSymbol.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
        var responseType = responseTypeSymbol.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
        if (requestTypeSymbol is not INamedTypeSymbol requestNamedType
            || !TryGetExactRequestContract(requestNamedType, responseType, out var requestContractType))
        {
            return null;
        }

        var tokenSupplied = call.Arguments.Any(argument => argument.Parameter?.Ordinal == 1
            && argument.ArgumentKind != ArgumentKind.DefaultValue);
        var candidates = FindSourceHandlers(
            call.Syntax.SyntaxTree,
            requestType!,
            responseType,
            project,
            operationEvidence,
            documents,
            models,
            cancellationToken);
        return new FrameworkDispatchShapeDescriptor(requestType, responseType, requestContractType, true, tokenSupplied, candidates);
    }

    private static bool TryGetExactRequestContract(
        INamedTypeSymbol requestType,
        string responseType,
        out string requestContractType)
    {
        requestContractType = string.Empty;
        if (requestType.TypeKind is not (TypeKind.Class or TypeKind.Struct)
            || requestType.IsUnboundGenericType
            || ContainsTypeParameter(requestType))
        {
            return false;
        }

        var requestInterface = requestType.AllInterfaces
            .Where(item => item.OriginalDefinition.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "MediatR.IRequest<TResponse>")
            .Where(item => item.TypeArguments.Length == 1 && !ContainsTypeParameter(item.TypeArguments[0]))
            .Where(item => item.TypeArguments[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == responseType)
            .ToArray();
        if (requestInterface.Length != 1)
        {
            return false;
        }

        requestContractType = requestInterface[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
        return true;
    }

    private static bool IsExactMediatRSend(IMethodSymbol target)
        => target.ContainingAssembly?.Identity.Name == "MediatR"
            && target.ContainingAssembly.Identity.Version?.ToString() == "13.0.0.0"
            && RoslynProgramIndexExtractor.GetMetadataName(target.ContainingType) == "MediatR.ISender"
            && target.MetadataName == "Send"
            && target.Arity == 1
            && target.Parameters.Length == 2
            && target.Parameters[0].Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat).StartsWith("MediatR.IRequest<", StringComparison.Ordinal)
            && target.Parameters[1].Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "System.Threading.CancellationToken"
            && UnwrapTask(target.ReturnType) == target.TypeArguments[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);

    private static string UnwrapTask(ITypeSymbol? type)
        => type is INamedTypeSymbol named
            && named.IsGenericType
            && named.OriginalDefinition.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "System.Threading.Tasks.Task<TResult>"
            ? named.TypeArguments[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat)
            : string.Empty;

    private static ITypeSymbol? UnwrapTaskSymbol(ITypeSymbol? type)
        => type is INamedTypeSymbol named
            && named.IsGenericType
            && named.OriginalDefinition.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "System.Threading.Tasks.Task<TResult>"
            ? named.TypeArguments[0]
            : null;

    private static bool ContainsTypeParameter(ITypeSymbol type)
        => type.TypeKind == TypeKind.TypeParameter
            || type switch
            {
                INamedTypeSymbol named => named.IsUnboundGenericType || named.TypeArguments.Any(ContainsTypeParameter),
                IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
                IPointerTypeSymbol pointer => ContainsTypeParameter(pointer.PointedAtType),
                _ => false,
            };

    private static IOperation UnwrapAllConversionsAndParentheses(IOperation operation)
    {
        var current = operation;
        while (current is IConversionOperation conversion)
        {
            current = conversion.Operand;
        }
        while (current is IParenthesizedOperation parenthesized)
        {
            current = parenthesized.Operand;
        }
        return current;
    }

    private static ImmutableArray<FrameworkDispatchCandidateDescriptor> FindSourceHandlers(
        SyntaxTree callTree,
        string requestType,
        string responseType,
        StableProjectId project,
        ImmutableArray<EvidenceRef> operationEvidence,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!documents.TryGetValue(callTree, out var document)
            || !models.TryGetValue(callTree, out var semanticModel))
        {
            return [];
        }

        var handlerInterface = semanticModel.Compilation.GetTypeByMetadataName("MediatR.IRequestHandler`2");
        if (handlerInterface is null)
        {
            return [];
        }

        var exactCandidates = new List<FrameworkDispatchCandidateDescriptor>();
        foreach (var type in EnumerateTypes(semanticModel.Compilation.Assembly.GlobalNamespace, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var implemented in type.AllInterfaces.Where(item => SymbolEqualityComparer.Default.Equals(item.OriginalDefinition, handlerInterface)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var args = implemented.TypeArguments;
                var closed = args.Length == 2
                    && args[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == requestType
                    && args[1].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == responseType;
                if (!closed || !IsClosedSourceType(type))
                {
                    continue;
                }

                var handleInterface = implemented.GetMembers("Handle").OfType<IMethodSymbol>().FirstOrDefault();
                var handle = handleInterface is null ? null : type.FindImplementationForInterfaceMember(handleInterface) as IMethodSymbol;
                if (handle is null || !handle.Locations.Any(location => location.IsInSource))
                {
                    continue;
                }

                var location = handle.Locations.First(item => item.IsInSource);
                if (!documents.TryGetValue(location.SourceTree!, out var handlerDocument))
                {
                    continue;
                }

                var sourceEvidence = RoslynProgramIndexExtractor.CreateSourceEvidence(
                    handlerDocument.Document.Id,
                    "roslyn:mediatR-handler",
                    handlerDocument.Text,
                    location.SourceSpan,
                    handle.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
                    generated: false);
                var methodId = StableIdentity.CreateMethodId(
                    RoslynProgramIndexExtractor.CreateMethodDescriptor(handle, project, documents));
                var bodyAvailable = handle.DeclaringSyntaxReferences.Length > 0
                    && !handle.IsAbstract;
                var candidate = new FrameworkDispatchCandidateDescriptor(
                    methodId,
                    $"{handle.ContainingType.Name}.{handle.Name}",
                    bodyAvailable,
                    [sourceEvidence],
                    CertaintyLevel.Exact,
                    IsOpenGeneric: false);
                exactCandidates.Add(candidate);
            }
        }

        return exactCandidates
            .GroupBy(candidate => candidate.Method.Value, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.Method.Value, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.Ordinal)
            .ToImmutableArray();

        static bool IsClosedSourceType(INamedTypeSymbol type)
        {
            for (var current = type; current is not null; current = current.ContainingType)
            {
                if (current.IsUnboundGenericType
                    || current.TypeParameters.Length != 0
                    || current.TypeArguments.Any(ContainsTypeParameter))
                {
                    return false;
                }
            }

            return true;
        }

        static bool ContainsTypeParameter(ITypeSymbol type)
            => type.TypeKind == TypeKind.TypeParameter
                || type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsTypeParameter);

        static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol space, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var type in space.GetTypeMembers().OrderBy(item => item.ToDisplayString(), StringComparer.Ordinal))
            {
                foreach (var nested in EnumerateType(type, cancellationToken))
                {
                    yield return nested;
                }
            }
            foreach (var child in space.GetNamespaceMembers().OrderBy(item => item.ToDisplayString(), StringComparer.Ordinal))
            {
                foreach (var type in EnumerateTypes(child, cancellationToken))
                {
                    yield return type;
                }
            }
        }

        static IEnumerable<INamedTypeSymbol> EnumerateType(INamedTypeSymbol type, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return type;
            foreach (var nested in type.GetTypeMembers().OrderBy(item => item.ToDisplayString(), StringComparer.Ordinal))
            {
                foreach (var item in EnumerateType(nested, cancellationToken))
                {
                    yield return item;
                }
            }
        }
    }

    private static (SeqDoc.Core.Identity.DocumentId? Document, int SourceStart, int SourceLength) ProjectSourceAnchor(
        SyntaxNode? syntax,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents)
    {
        if (syntax is null || !documents.TryGetValue(syntax.SyntaxTree, out var document))
        {
            return (null, 0, 0);
        }

        return (document.Document.Id, syntax.SpanStart, syntax.Span.Length);
    }

    private static CallbackTargetDescriptor? ProjectCallbackTarget(
        IInvocationOperation call,
        MethodId methodId,
        OperationId operationId,
        Dictionary<IOperation, OperationId> operationById,
        ImmutableArray<int> supplied,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        StableProjectId? project,
        CompilationProfileId? profile,
        CallbackBoundaryFactSet? facts)
    {
        if (facts is not null)
        {
            var boundary = facts.Boundaries.FirstOrDefault(candidate => candidate.OuterInvocationOperation == operationId
                && supplied.Contains(candidate.ParameterOrdinal));
            if (boundary is not null)
            {
                return new CallbackTargetDescriptor(boundary.TargetKind, boundary.TargetMethod, boundary.TargetBodyOperation, boundary.Id);
            }
        }

        return ProjectCallbackTarget(call.Arguments, methodId, operationId, project, profile, documents);
    }

    private static CallbackTargetDescriptor? ProjectCallbackTarget(
        IEnumerable<IArgumentOperation> arguments,
        MethodId methodId,
        OperationId operationId,
        StableProjectId? project,
        CompilationProfileId? profile,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter is null || argument.ArgumentKind == ArgumentKind.DefaultValue
                || argument.Parameter.Type is not INamedTypeSymbol parameterType
                || (!IsExactSystemDelegate(parameterType) && !IsExactTimerCallback(parameterType)))
            {
                continue;
            }

            var value = UnwrapImplicitConversionsAndParentheses(argument.Value);
            if (value is IDelegateCreationOperation creation)
            {
                value = UnwrapImplicitConversionsAndParentheses(creation.Target);
            }

            var anonymous = value as IAnonymousFunctionOperation;
            if (anonymous is not null)
            {
                var bodyOperation = RoslynBehaviorExtractor.CreateOperationId(anonymous, methodId, "AnonymousFunction", 0, 0, 0, documents);
                return CreateBoundary(CallbackTargetKind.AnonymousFunction, null, bodyOperation, methodId, operationId, argument.Parameter.Ordinal, profile);
            }

            if (value is IMethodReferenceOperation reference
                && reference.Method.Locations.Any(location => location.IsInSource))
            {
                if (project is null)
                {
                    return null;
                }

                var target = StableIdentity.CreateMethodId(RoslynProgramIndexExtractor.CreateMethodDescriptor(reference.Method, project.Value));
                var kind = reference.Method.MethodKind == MethodKind.LocalFunction ? CallbackTargetKind.LocalFunction : CallbackTargetKind.MethodGroup;
                return CreateBoundary(kind, target, null, methodId, operationId, argument.Parameter.Ordinal, profile);
            }
        }

        static bool IsExactSystemDelegate(INamedTypeSymbol type)
        {
            return type.SpecialType == SpecialType.System_Delegate
                && string.Equals(type.MetadataName, "Delegate", StringComparison.Ordinal)
                && string.Equals(type.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal);
        }

        static bool IsExactTimerCallback(INamedTypeSymbol type)
            => type.MetadataName == "TimerCallback"
                && type.ContainingNamespace?.ToDisplayString() == "System.Threading"
                && type.ContainingAssembly?.Identity.Name is "System.Runtime" or "System.Private.CoreLib";

        return null;

        CallbackTargetDescriptor CreateBoundary(CallbackTargetKind kind, MethodId? targetMethod, OperationId? body, MethodId caller, OperationId outer, int ordinal, CompilationProfileId? profileId)
        {
            if (profileId is null)
            {
                return new CallbackTargetDescriptor(kind, targetMethod, body, null);
            }
            var id = StableIdentity.CreateCallbackBoundaryId(new CallbackBoundaryIdentityDescriptor(
                profileId.Value, caller, outer, ordinal, kind, targetMethod, body, null, null,
                CallbackCardinality.Unknown, CallbackTriggerKind.Unknown, null,
                CallbackCompletionKind.Unknown, CallbackContractProvenance.Unknown, "<empty>"));
            return new CallbackTargetDescriptor(kind, targetMethod, body, id);
        }
    }

    private static FrameworkRouteGroupDescriptor? ProjectRouteGroup(
        IInvocationOperation call,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        IReadOnlyDictionary<ILocalSymbol, IOperation>? localInitializers)
    {
        var receiver = call.Instance;
        if (receiver is null && call.TargetMethod.IsExtensionMethod && call.Arguments.Length > 0)
        {
            receiver = call.Arguments[0].Value;
        }

        var steps = new List<FrameworkRouteGroupStepDescriptor>();
        var visitedLocals = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        while (receiver is not null)
        {
            receiver = UnwrapImplicitConversionsAndParentheses(receiver);
            if (receiver is ILocalReferenceOperation localReference)
            {
                if (!visitedLocals.Add(localReference.Local)
                    || localInitializers is null
                    || !localInitializers.TryGetValue(localReference.Local, out receiver))
                {
                    return null;
                }

                continue;
            }

            if (receiver is IInvocationOperation convention
                && IsExactHasApiVersion(convention.TargetMethod))
            {
                if (convention.Instance is null
                    || convention.Instance.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat)
                        != "Microsoft.AspNetCore.Routing.RouteGroupBuilder")
                {
                    return null;
                }

                receiver = UnwrapImplicitConversionsAndParentheses(convention.Instance);
                continue;
            }

            if (receiver is not IInvocationOperation group || group.TargetMethod.MetadataName != "MapGroup")
            {
                break;
            }

            var patternArgument = group.TargetMethod.IsExtensionMethod ? 1 : 0;
            if (group.TargetMethod.ContainingType is null
                || RoslynProgramIndexExtractor.GetMetadataName(group.TargetMethod.ContainingType) != "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions"
                || group.Arguments.Length <= patternArgument
                || group.Arguments[patternArgument].Value.ConstantValue.Value is not string prefix)
            {
                return null;
            }

            steps.Add(new FrameworkRouteGroupStepDescriptor(prefix, ProjectTargetIdentity(group.TargetMethod)));
            receiver = group.Instance ?? (group.TargetMethod.IsExtensionMethod && group.Arguments.Length > 0 ? group.Arguments[0].Value : null);
        }

        if (steps.Count == 0)
        {
            return null;
        }

        steps.Reverse();
        return new FrameworkRouteGroupDescriptor(steps.ToImmutableArray());
    }

    private static bool IsExactHasApiVersion(IMethodSymbol target)
        // The compiler-bound reduced generated extension shape reports IsExtensionMethod=false; keep the
        // exact generated type and version-pinned checks as the eligibility boundary.
        => target.ContainingAssembly?.Identity.Name == "Asp.Versioning.Http"
            && target.ContainingAssembly.Identity.Version?.ToString() == "10.0.0.0"
            && RoslynProgramIndexExtractor.GetMetadataName(target.ContainingType)
                == "Microsoft.AspNetCore.Builder.IEndpointConventionBuilderExtensions+<M>$6CF36377BC6C4E40082B7DD571549918"
            && target.MetadataName == "HasApiVersion"
            && target.Arity == 0
            && target.Parameters.Length == 2
            && target.Parameters[0].RefKind == RefKind.None
            && target.Parameters[0].Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat)
                == "System.Double"
            && target.Parameters[1].RefKind == RefKind.None
            && target.Parameters[1].Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat)
                == "System.String"
            && target.ReturnType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat)
                == "Microsoft.AspNetCore.Routing.RouteGroupBuilder";

    /// <summary>
    /// Projects the receiver chain of an exact Entity Framework queryable extension call. Returns null
    /// for every other invocation so non-EF callers remain unchanged. The chain records the base
    /// member (DbContext DbSet with its entity type) and the ordered invocation steps from source
    /// order, each reusing the stable operation identity the behavior flattening pass assigned.
    /// </summary>
    private static FrameworkQueryChainDescriptor? ProjectQueryChain(
        IInvocationOperation call,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
    {
        var target = call.TargetMethod;
        if (target is null
            || target.ContainingType is null
            || !string.Equals(
                RoslynProgramIndexExtractor.GetMetadataName(target.ContainingType),
                EntityFrameworkQueryableExtensionsMetadataName,
                StringComparison.Ordinal))
        {
            return null;
        }

        var steps = new List<FrameworkChainStepDescriptor>();
        IOperation? current = ResolveExtensionReceiver(call);
        IOperation? baseMember = null;
        while (current is not null)
        {
            if (current is IInvocationOperation step && step.TargetMethod is { } stepTarget)
            {
                if (operationById.TryGetValue(current, out var stepId))
                {
                    steps.Add(new FrameworkChainStepDescriptor(
                        stepId,
                        ProjectTargetIdentity(stepTarget),
                        ResolveNavigationMember(step, models)));
                }

                current = ResolveExtensionReceiver(step);
                continue;
            }

            if (current is IPropertyReferenceOperation or IFieldReferenceOperation)
            {
                baseMember = current;
            }

            break;
        }

        if (baseMember is null)
        {
            return null;
        }

        var receiverType = baseMember switch
        {
            IPropertyReferenceOperation property => property.Type,
            IFieldReferenceOperation field => field.Type,
            _ => null,
        };
        var containingType = baseMember switch
        {
            IPropertyReferenceOperation property => property.Property.ContainingType,
            IFieldReferenceOperation field => field.Field.ContainingType,
            _ => null,
        };
        var memberName = baseMember switch
        {
            IPropertyReferenceOperation property => property.Property.Name,
            IFieldReferenceOperation field => field.Field.Name,
            _ => null,
        };
        if (receiverType is null || containingType is null || memberName is null)
        {
            return null;
        }

        steps.Reverse();
        return new FrameworkQueryChainDescriptor(
            receiverType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
            containingType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
            memberName,
            ResolveEntityType(receiverType),
            steps.ToImmutableArray());
    }

    /// <summary>
    /// Resolves the receiver expression of one invocation. Instance syntax stores the receiver in
    /// <see cref="IInvocationOperation.Instance"/>; compiler forms of extension-method calls store it
    /// as the first argument instead. In both forms the receiver can be wrapped in an implicit
    /// conversion (for example a DbSet to IQueryable) that must be unwrapped before the chain walk.
    /// </summary>
    private static IOperation? ResolveExtensionReceiver(IInvocationOperation call)
    {
        if (call.Instance is not null)
        {
            return UnwrapImplicitConversionsAndParentheses(call.Instance);
        }

        if (call.TargetMethod.IsExtensionMethod && call.Arguments.Length > 0)
        {
            return UnwrapImplicitConversionsAndParentheses(call.Arguments[0].Value);
        }

        return null;
    }

    private static IOperation UnwrapImplicitConversionsAndParentheses(IOperation operation)
    {
        IOperation current = operation;
        while (current is IConversionOperation { IsImplicit: true } conversion)
        {
            current = conversion.Operand;
        }

        while (current is IParenthesizedOperation parenthesized)
        {
            current = parenthesized.Operand;
        }

        return current;
    }

    /// <summary>
    /// Resolves the entity element type of a DbSet receiver. Only a constructed DbSet with exactly one
    /// type argument yields an entity type; every other receiver fails closed with an empty value so
    /// the model can never guess an entity from an unproven shape.
    /// </summary>
    private static string ResolveEntityType(ITypeSymbol receiverType)
    {
        if (receiverType is INamedTypeSymbol named
            && named.IsGenericType
            && named.TypeArguments.Length == 1
            && named.OriginalDefinition is { } original
            && string.Equals(original.Name, "DbSet", StringComparison.Ordinal)
            && string.Equals(
                original.ContainingNamespace?.ToDisplayString(),
                "Microsoft.EntityFrameworkCore",
                StringComparison.Ordinal))
        {
            return named.TypeArguments[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
        }

        return string.Empty;
    }

    /// <summary>
    /// Resolves the canonical navigation-member identity selected by an Include-style step. Only a
    /// single member access inside the lambda body yields a navigation identity; every other shape
    /// fails closed with null so the model never invents a navigation from an unproven expression.
    /// </summary>
    private static string? ResolveNavigationMember(
        IInvocationOperation includeCall,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
    {
        foreach (var argument in includeCall.Arguments)
        {
            if (argument.Value is null)
            {
                continue;
            }

            var member = FindSingleMemberAccess(ResolveLambdaValue(argument.Value, models));
            if (member is not null && member.ContainingType is not null)
            {
                // FullyQualifiedFormat displays members without their containing type, so the
                // canonical navigation identity combines the declaring type with the member name.
                return $"{member.ContainingType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat)}.{member.Name}";
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the predicate shape of one invocation. When the call carries an anonymous-function
    /// argument, the lambda body is inspected for exactly one equality binary operation; the shape and
    /// the stable comparison operation anchor are projected from compiler operations. No argument
    /// yields None; an unproven or non-equality body yields Unknown or NotEqualityComparison so the
    /// model fails closed.
    /// </summary>
    private static PredicateShapeDescriptor? ProjectPredicateShape(
        IInvocationOperation call,
        MethodId methodId,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
    {
        var hasLambda = false;
        IOperation? bodyValue = null;
        foreach (var argument in call.Arguments)
        {
            var candidate = ResolveLambdaValue(argument.Value, models);
            if (candidate is not null)
            {
                hasLambda = true;
                bodyValue = candidate;
                break;
            }
        }

        if (!hasLambda)
        {
            return new PredicateShapeDescriptor(PredicateShapeKind.None, null);
        }

        if (bodyValue is IBinaryOperation binary)
        {
            var kind = binary.OperatorKind switch
            {
                BinaryOperatorKind.Equals => PredicateShapeKind.EqualityComparison,
                BinaryOperatorKind.NotEquals => PredicateShapeKind.NotEqualityComparison,
                _ => PredicateShapeKind.Unknown,
            };
            // The comparison anchor is the exact operation id the semantic collector used. Lambda
            // bodies are hidden in the control-flow graph, so the collector and this projector both
            // create span-anchored ids with zero traversal ordinals; direct comparisons reuse the
            // flattened operation id so both representations remain joinable.
            var comparisonOperation = operationById.TryGetValue(binary, out var comparisonId)
                ? comparisonId
                : RoslynBehaviorExtractor.CreateOperationId(binary, methodId, "Binary", 0, 0, 0, documents);
            return new PredicateShapeDescriptor(kind, comparisonOperation);
        }

        return new PredicateShapeDescriptor(PredicateShapeKind.Unknown, null);
    }

    /// <summary>
    /// Resolves the single value expression of one lambda argument. The control-flow graph hides
    /// lambda bodies inside <see cref="IFlowAnonymousFunctionOperation"/> whose body is not exposed
    /// publicly, so the body is resolved through its declaring syntax and the semantic model. A plain
    /// <see cref="IAnonymousFunctionOperation"/> fallback keeps non-CFG operation trees working.
    /// </summary>
    private static IOperation? ResolveLambdaValue(
        IOperation? value,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
    {
        if (value is not null && value.Syntax is not null
            && models.TryGetValue(value.Syntax.SyntaxTree, out var model)
            && model.GetOperation(value.Syntax) is IAnonymousFunctionOperation anonymous)
        {
            return ResolveLambdaReturnValue(anonymous.Body);
        }

        var plainBody = UnwrapAnonymousFunction(value);
        return plainBody is null ? null : ResolveLambdaReturnValue(plainBody);
    }

    private static IOperation? UnwrapAnonymousFunction(IOperation? value)
    {
        if (value is null)
        {
            return null;
        }

        IOperation current = value;
        while (true)
        {
            if (current is IAnonymousFunctionOperation anonymous)
            {
                return anonymous.Body;
            }

            if (current is IConversionOperation { Operand: { } operand })
            {
                current = operand;
                continue;
            }

            if (current is IDelegateCreationOperation { Target: { } target })
            {
                current = target;
                continue;
            }

            if (current is IParenthesizedOperation { Operand: { } parenthesized })
            {
                current = parenthesized;
                continue;
            }

            return null;
        }
    }

    private static IOperation? ResolveLambdaReturnValue(IOperation lambdaBody)
    {
        var pending = new Stack<IOperation>();
        pending.Push(lambdaBody);
        while (pending.TryPop(out var operation))
        {
            if (operation is IReturnOperation { ReturnedValue: { } returned })
            {
                return UnwrapImplicitConversions(returned);
            }

            if (operation is IBlockOperation or IExpressionStatementOperation)
            {
                foreach (var child in operation.ChildOperations)
                {
                    pending.Push(child);
                }
            }
        }

        return null;
    }

    private static ISymbol? FindSingleMemberAccess(IOperation? lambdaBody)
    {
        if (lambdaBody is null)
        {
            return null;
        }

        ISymbol? member = null;
        var pending = new Stack<IOperation>();
        pending.Push(lambdaBody);
        while (pending.TryPop(out var operation))
        {
            switch (operation)
            {
                case IPropertyReferenceOperation property:
                    if (member is not null)
                    {
                        return null;
                    }

                    member = property.Property;
                    break;
                case IFieldReferenceOperation field:
                    if (member is not null)
                    {
                        return null;
                    }

                    member = field.Field;
                    break;
            }

            foreach (var child in operation.ChildOperations)
            {
                pending.Push(child);
            }
        }

        return member;
    }

    private static IOperation UnwrapImplicitConversions(IOperation operation)
    {
        IOperation current = operation;
        while (current is IConversionOperation { IsImplicit: true } conversion)
        {
            current = conversion.Operand;
        }

        return current;
    }

    private static FrameworkMethodIdentity ProjectTargetIdentity(IMethodSymbol target)
    {
        var assembly = target.ContainingAssembly;
        return new FrameworkMethodIdentity(
            assembly?.Identity.Name ?? string.Empty,
            RoslynProgramIndexExtractor.GetMetadataName(target.ContainingType),
            target.MetadataName,
            target.Arity,
            target.Parameters
                .Select(parameter => new ParameterIdentityDescriptor(
                    RoslynProgramIndexExtractor.ToParameterRefKind(parameter.RefKind),
                    parameter.Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat)))
                .ToImmutableArray(),
            target.ReturnType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
            assembly?.Identity.Version?.ToString());
    }

    private static bool IsCanonicalConstructedTypeArgument(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Error
            || type.IsAnonymousType
            || type.IsTupleType
            || type.IsImplicitlyDeclared
            || type.IsUnboundGenericType
            || ContainsTypeParameter(type)
            || type.ContainingAssembly?.Identity is not { Name.Length: > 0, Version: not null })
        {
            return false;
        }

        var metadataName = RoslynProgramIndexExtractor.GetMetadataName(type);
        return !string.IsNullOrWhiteSpace(metadataName)
            && !metadataName.Any(char.IsWhiteSpace)
            && !metadataName.Contains('<')
            && !metadataName.Contains('>');
    }

    private static ImmutableArray<CompilerProvenArgument> ProjectConstantArguments(ImmutableArray<IArgumentOperation> arguments)
    {
        if (arguments.IsDefaultOrEmpty)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<CompilerProvenArgument>();
        foreach (var argument in arguments)
        {
            if (argument.Parameter is null
                || argument.Value is null
                || !argument.Value.ConstantValue.HasValue
                || argument.Value.Type is null)
            {
                continue;
            }

            var fullyQualifiedType = argument.Value.Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
            if (string.IsNullOrWhiteSpace(fullyQualifiedType))
            {
                continue;
            }

            var isNull = argument.Value.ConstantValue.Value is null;
            builder.Add(new CompilerProvenArgument(
                argument.Parameter.Ordinal,
                fullyQualifiedType,
                isNull ? null : Convert.ToString(argument.Value.ConstantValue.Value, CultureInfo.InvariantCulture),
                isNull));
        }

        return builder
            .OrderBy(argument => argument.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    /// Projects the canonical, ascending, distinct compiler declaration ordinals of the arguments
    /// actually supplied at the invocation. Only compiler-bound arguments with a bound declaration
    /// parameter count, using the exact declaration <see cref="IParameterSymbol.Ordinal"/>: the
    /// admitted supported call projects [1,2,3] and a tags-supplying call projects [1,2,3,4].
    /// Synthesized <see cref="ArgumentKind.DefaultValue"/> arguments for omitted optional parameters
    /// are excluded so the source-supplied contract never counts an argument the source did not
    /// write. Arguments without a bound declaration parameter are excluded so models never guess a
    /// parameter from a positional shape.
    /// </summary>
    private static ImmutableArray<int> ProjectSuppliedParameterOrdinals(IInvocationOperation call)
    {
        if (call.Arguments.IsDefaultOrEmpty)
        {
            return [];
        }

        return call.Arguments
            .Where(argument => argument.Parameter is not null
                && argument.ArgumentKind != ArgumentKind.DefaultValue)
            .Select(argument => argument.Parameter!.Ordinal)
            .Distinct()
            .OrderBy(ordinal => ordinal)
            .ToImmutableArray();
    }
}
