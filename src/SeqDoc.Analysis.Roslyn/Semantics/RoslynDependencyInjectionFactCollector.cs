using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Semantics;

/// <summary>
/// Accumulates dependency-injection companion fact drafts during one Roslyn compilation/extraction
/// session and builds the Roslyn-neutral, memory-only <see cref="DependencyInjectionFactSet"/>.
/// Admission is exact Microsoft symbol identity: the original generic extension method must live on
/// <c>Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions</c> in the
/// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> assembly, carry exactly two type
/// arguments, and take only the <c>IServiceCollection</c> receiver. Factory, instance, non-generic,
/// open-generic, collection, keyed, TryAdd, wrapper, and lookalike-helper forms fail closed by
/// producing no fact. Bindings match constructor parameter types to registration service types by
/// exact compiler symbol equality, and every matching registration produces its own binding so no
/// single implementation is ever selected when several exist.
/// </summary>
internal sealed class RoslynDependencyInjectionFactCollector
{
    private const string ProducerVersion = "0.1.0-alpha";

    /// <summary>
    /// Metadata names used to resolve the authoritative Microsoft DI symbols from each loaded
    /// compilation. Admission compares compiler symbols with <see cref="SymbolEqualityComparer"/>;
    /// these strings are never used as an identity check on their own.
    /// </summary>
    internal const string ServiceCollectionServiceExtensionsMetadataName =
        "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions";
    internal const string ServiceCollectionInterfaceMetadataName =
        "Microsoft.Extensions.DependencyInjection.IServiceCollection";

    private readonly List<RegistrationDraft> _registrations = [];
    private readonly List<ConstructorParameterDraft> _constructorParameters = [];
    private readonly Dictionary<StableProjectId, AuthoritativeDiSymbols> _authoritativeByProject = [];

    /// <summary>
    /// Records the authoritative Microsoft DI symbols resolved from one loaded compilation. The
    /// compiler-proven boundary is exact symbol identity against these symbols; lookalike helpers in
    /// other assemblies and same-simple-name helpers never match. Without the authoritative symbols a
    /// project fails closed and admits nothing.
    /// </summary>
    public void SetAuthoritativeSymbols(
        StableProjectId project,
        INamedTypeSymbol? serviceCollection,
        INamedTypeSymbol? extensionsClass)
    {
        _authoritativeByProject[project] = new AuthoritativeDiSymbols(serviceCollection, extensionsClass);
    }

    /// <summary>
    /// Records one admitted registration when the invocation is an exact Microsoft
    /// <c>AddScoped/AddSingleton/AddTransient&lt;TService, TImplementation&gt;</c> receiver-only call;
    /// all other shapes are ignored so unsupported forms fail closed.
    /// </summary>
    public void AddRegistration(
        StableProjectId project,
        MethodId methodId,
        IInvocationOperation call,
        OperationId operationId,
        ImmutableArray<EvidenceRef> evidence)
    {
        if (!_authoritativeByProject.TryGetValue(project, out var authoritative)
            || authoritative.ServiceCollection is null
            || authoritative.ExtensionsClass is null
            || !TryAdmitRegistration(call, authoritative, out var serviceType, out var implementationType, out var lifetime))
        {
            return;
        }

        _registrations.Add(new RegistrationDraft(
            methodId,
            operationId,
            DisplayType(serviceType),
            DisplayType(implementationType),
            serviceType,
            implementationType,
            lifetime,
            evidence));
    }

    /// <summary>
    /// Records every non-collection constructor parameter of one source constructor. Collection-typed
    /// parameters fail closed because collection injection is unsupported and never binds to a single
    /// registration.
    /// </summary>
    public void AddConstructorParameters(
        IMethodSymbol constructor,
        MethodId constructorMethodId,
        ImmutableArray<EvidenceRef> evidence)
    {
        foreach (var parameter in constructor.Parameters)
        {
            if (IsCollectionType(parameter.Type))
            {
                continue;
            }

            _constructorParameters.Add(new ConstructorParameterDraft(
                constructorMethodId,
                parameter.Ordinal,
                parameter.Name,
                DisplayType(parameter.Type),
                parameter.Type,
                evidence));
        }
    }

    public DependencyInjectionFactSet Build(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint, nameof(programIndexFingerprint));

        var registrations = _registrations
            .DistinctBy(registration => registration.Operation.Value)
            .OrderBy(registration => registration.Method.Value, StringComparer.Ordinal)
            .ThenBy(registration => registration.Operation.Value, StringComparer.Ordinal)
            .Select(draft => ProjectRegistration(profile.Id, draft))
            .ToImmutableArray();
        var bindings = ProjectBindings(profile.Id, registrations);
        var debugProjection = BuildDebugProjection(
            profile,
            programIndexFingerprint,
            registrations,
            bindings,
            diagnostics.Length);
        return new DependencyInjectionFactSet(
            1,
            ProducerVersion,
            profile,
            programIndexFingerprint,
            registrations,
            bindings,
            diagnostics,
            debugProjection);
    }

    /// <summary>
    /// Binds every constructor parameter to every exact matching admitted registration by compiler
    /// symbol equality. Distinct matching registrations produce distinct binding facts, so callers
    /// can always see that no single implementation was selected.
    /// </summary>
    private ImmutableArray<DependencyInjectionBindingFact> ProjectBindings(
        CompilationProfileId profileId,
        ImmutableArray<DependencyInjectionRegistrationFact> registrations)
    {
        var byOperation = registrations.ToDictionary(
            registration => registration.Operation.Value,
            StringComparer.Ordinal);
        var bindings = new List<DependencyInjectionBindingFact>();
        foreach (var parameter in _constructorParameters)
        {
            foreach (var draft in _registrations.Where(draft =>
                         SymbolEqualityComparer.Default.Equals(parameter.ParameterType, draft.ServiceTypeSymbol)))
            {
                var registration = byOperation[draft.Operation.Value];
                // Every binding carries the canonical union of the controller-constructor source
                // anchor and the registration source anchor, deduplicated by evidence id so both
                // anchors are always visible without duplication.
                var combinedEvidence = parameter.Evidence
                    .Concat(registration.Evidence)
                    .DistinctBy(item => item.Id)
                    .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                    .ToImmutableArray();
                bindings.Add(new DependencyInjectionBindingFact(
                    CreateBindingId(profileId, registration, parameter),
                    parameter.ConstructorMethod,
                    parameter.Ordinal,
                    parameter.Name,
                    parameter.ParameterTypeName,
                    registration.Id,
                    registration.ServiceType,
                    registration.ImplementationType,
                    registration.Lifetime,
                    combinedEvidence,
                    registration.Certainty));
            }
        }

        return bindings
            .OrderBy(binding => binding.ConstructorMethod.Value, StringComparer.Ordinal)
            .ThenBy(binding => binding.ParameterOrdinal)
            .ThenBy(binding => binding.RegistrationId.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static DependencyInjectionRegistrationFact ProjectRegistration(
        CompilationProfileId profileId,
        RegistrationDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "di-registration",
            draft.Method,
            draft.Operation,
            $"{draft.Lifetime.ToString()}|{draft.ServiceTypeName}"));
        return new DependencyInjectionRegistrationFact(
            id,
            draft.Method,
            draft.Operation,
            draft.ServiceTypeName,
            draft.ImplementationTypeName,
            draft.Lifetime,
            draft.Evidence,
            CertaintyLevel.Exact);
    }

    private static SemanticFactId CreateBindingId(
        CompilationProfileId profileId,
        DependencyInjectionRegistrationFact registration,
        ConstructorParameterDraft parameter)
    {
        return StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "di-binding",
            registration.SourceMethod,
            registration.Operation,
            $"{parameter.ConstructorMethod.Value}|{parameter.Ordinal.ToString(CultureInfo.InvariantCulture)}"));
    }

    private static bool TryAdmitRegistration(
        IInvocationOperation call,
        AuthoritativeDiSymbols authoritative,
        out ITypeSymbol serviceType,
        out ITypeSymbol implementationType,
        out DependencyInjectionLifetime lifetime)
    {
        serviceType = null!;
        implementationType = null!;
        lifetime = default;

        var target = call.TargetMethod;
        if (target is null || !target.IsExtensionMethod)
        {
            return false;
        }

        // Instance-syntax calls report the reduced extension method; static-syntax calls report the
        // original. The unreduced method is the authority for method kind, arity, containing type,
        // and parameter shape, so unwrap the reduction before checking identity against the
        // authoritative symbol resolved from the same compilation.
        var unreduced = target.ReducedFrom ?? target;
        var original = unreduced.OriginalDefinition ?? unreduced;
        if (original.MethodKind != MethodKind.Ordinary
            || original.Arity != 2
            || original.ContainingType is null
            || !SymbolEqualityComparer.Default.Equals(original.ContainingType.OriginalDefinition, authoritative.ExtensionsClass))
        {
            return false;
        }

        var admittedLifetime = original.Name switch
        {
            "AddScoped" => DependencyInjectionLifetime.Scoped,
            "AddSingleton" => DependencyInjectionLifetime.Singleton,
            "AddTransient" => DependencyInjectionLifetime.Transient,
            _ => (DependencyInjectionLifetime?)null,
        };
        if (admittedLifetime is null)
        {
            return false;
        }

        // The admitted shape is the receiver-only two-type-argument extension. The unreduced method
        // takes exactly the IServiceCollection 'this' parameter; a second parameter is a factory or
        // instance form and fails closed. The non-generic overloads have arity zero and already
        // failed above.
        if (unreduced.Parameters.Length != 1
            || !SymbolEqualityComparer.Default.Equals(unreduced.Parameters[0].Type, authoritative.ServiceCollection))
        {
            return false;
        }

        // A receiver-only call carries at most one explicit argument: instance-syntax calls hold the
        // receiver on Instance (and some Roslyn representations duplicate it as the sole explicit
        // argument), while static-syntax calls hold it as the first explicit argument. A second
        // explicit argument is always a factory or instance form.
        var explicitArguments = call.Arguments.IsDefaultOrEmpty ? [] : call.Arguments;
        if (explicitArguments.Length > 1)
        {
            return false;
        }

        var receiverType = call.Instance?.Type;
        if (receiverType is null)
        {
            receiverType = explicitArguments.IsEmpty ? null : explicitArguments[0].Value?.Type;
        }

        if (receiverType is null
            || !SymbolEqualityComparer.Default.Equals(receiverType, authoritative.ServiceCollection))
        {
            return false;
        }

        // In instance syntax an explicit argument must be the receiver itself, never a factory or
        // instance value supplied after the receiver.
        if (call.Instance is not null
            && !explicitArguments.IsEmpty
            && (explicitArguments[0].Value?.Type is not { } explicitReceiverType
                || !SymbolEqualityComparer.Default.Equals(explicitReceiverType, authoritative.ServiceCollection)))
        {
            return false;
        }

        if (target.TypeArguments.Length != 2)
        {
            return false;
        }

        var service = target.TypeArguments[0];
        var implementation = target.TypeArguments[1];
        if (IsOpenGenericType(service)
            || IsOpenGenericType(implementation)
            || IsCollectionType(service)
            || IsCollectionType(implementation))
        {
            // Open generic and collection/array registrations are unsupported and fail closed.
            return false;
        }

        serviceType = service;
        implementationType = implementation;
        lifetime = admittedLifetime.Value;
        return true;
    }

    private static bool IsOpenGenericType(ITypeSymbol type)
        => type is ITypeParameterSymbol
            || (type is INamedTypeSymbol named
                && (named.IsUnboundGenericType || named.TypeArguments.Any(IsOpenGenericType)));

    /// <summary>
    /// Rejects the collection-injection shapes SeqDoc does not support: arrays and the BCL
    /// <c>IEnumerable&lt;T&gt;</c> constructed interface. User-defined <c>IEnumerable&lt;T&gt;</c>
    /// lookalikes are not BCL special types and remain ordinary service types.
    /// </summary>
    private static bool IsCollectionType(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Array)
        {
            return true;
        }

        return type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T;
    }

    private static string DisplayType(ITypeSymbol type)
        => type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);

    private static string BuildDebugProjection(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<DependencyInjectionRegistrationFact> registrations,
        ImmutableArray<DependencyInjectionBindingFact> bindings,
        int diagnosticCount)
    {
        var lines = new List<(string Id, string Line)>();
        foreach (var registration in registrations)
        {
            lines.Add((
                registration.Id.Value,
                $"registration {registration.Id.Value} method={registration.SourceMethod.Value} lifetime={registration.Lifetime.ToString()} service={registration.ServiceType} implementation={registration.ImplementationType} operation={registration.Operation.Value}"));
        }

        foreach (var binding in bindings)
        {
            lines.Add((
                binding.Id.Value,
                $"binding {binding.Id.Value} constructor={binding.ConstructorMethod.Value} parameterOrdinal={binding.ParameterOrdinal.ToString(CultureInfo.InvariantCulture)} parameterType={binding.ParameterType} registration={binding.RegistrationId.Value} service={binding.ServiceType} implementation={binding.ImplementationType} lifetime={binding.Lifetime.ToString()}"));
        }

        var builder = new StringBuilder();
        builder.Append("dependency-injection:v1").Append('\n');
        builder.Append("producer=").Append(ProducerVersion).Append('\n');
        builder.Append("profile=").Append(profile.Id.Value).Append('\n');
        builder.Append("programIndexFingerprint=").Append(programIndexFingerprint).Append('\n');
        builder.Append("diagnosticCount=").Append(diagnosticCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var line in lines.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.Append(line.Line).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    private sealed record RegistrationDraft(
        MethodId Method,
        OperationId Operation,
        string ServiceTypeName,
        string ImplementationTypeName,
        ITypeSymbol ServiceTypeSymbol,
        ITypeSymbol ImplementationTypeSymbol,
        DependencyInjectionLifetime Lifetime,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record ConstructorParameterDraft(
        MethodId ConstructorMethod,
        int Ordinal,
        string Name,
        string ParameterTypeName,
        ITypeSymbol ParameterType,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record AuthoritativeDiSymbols(
        INamedTypeSymbol? ServiceCollection,
        INamedTypeSymbol? ExtensionsClass);
}
