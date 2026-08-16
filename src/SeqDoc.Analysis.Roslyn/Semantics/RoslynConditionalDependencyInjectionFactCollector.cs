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
/// Accumulates conditional dependency-injection companion fact drafts during one Roslyn
/// compilation/extraction session and builds the Roslyn-neutral, memory-only
/// <see cref="ConditionalDependencyInjectionFactSet"/>. Admission is the exact Microsoft generic
/// <c>AddScoped/AddSingleton/AddTransient&lt;TService, TImplementation&gt;</c> receiver-only shape
/// resolved by compiler symbol identity against the authoritative <c>IServiceCollection</c> and
/// <c>ServiceCollectionServiceExtensions</c> symbols; keyed, TryAdd, factory, instance, non-generic,
/// collection, and open-generic forms fail closed with no arm fact. The collector never changes the
/// accepted dependency-injection collector: arm facts are bound to the completed
/// <see cref="DependencyInjectionFactSet"/> registrations by exact operation identity during build.
/// Only the synthesized top-level method is a control authority for arm membership; extracted
/// methods never feed this collector because accepted Method Flow remains their sole control
/// authority.
/// </summary>
internal sealed class RoslynConditionalDependencyInjectionFactCollector
{
    private const string ProducerVersion = "0.1.0-alpha";

    private readonly Dictionary<StableProjectId, AuthoritativeConditionalDiSymbols> _authoritativeByProject = [];
    private readonly List<ArmDraft> _arms = [];

    /// <summary>
    /// Records the authoritative Microsoft DI symbols resolved from one loaded compilation. The
    /// compiler-proven boundary is exact symbol identity against these symbols; lookalike helpers in
    /// other assemblies and same-simple-name helpers never match. Without the authoritative symbols a
    /// project fails closed and admits no arm.
    /// </summary>
    public void SetAuthoritativeSymbols(
        StableProjectId project,
        INamedTypeSymbol? serviceCollection,
        INamedTypeSymbol? extensionsClass)
    {
        _authoritativeByProject[project] = new AuthoritativeConditionalDiSymbols(serviceCollection, extensionsClass);
    }

    /// <summary>
    /// Records one configuration-arm membership draft when the invocation is an exact admitted
    /// Microsoft <c>AddScoped/AddSingleton/AddTransient&lt;TService, TImplementation&gt;</c> call; all
    /// other shapes are ignored so unsupported forms fail closed. The arm anchors to the exact
    /// condition/read operations and the canonical configuration key supplied by the top-level
    /// traversal; the registration identity is bound during build from the completed DI fact set.
    /// </summary>
    public void AddArm(
        StableProjectId project,
        MethodId methodId,
        IInvocationOperation call,
        OperationId registrationOperation,
        OperationId conditionOperation,
        OperationId readOperation,
        string key,
        ImmutableArray<EvidenceRef> evidence,
        bool isTrueArm)
    {
        if (!_authoritativeByProject.TryGetValue(project, out var authoritative)
            || authoritative.ServiceCollection is null
            || authoritative.ExtensionsClass is null
            || !TryAdmitRegistration(call, authoritative, out var serviceType, out var implementationType, out var lifetime))
        {
            return;
        }

        _arms.Add(new ArmDraft(
            methodId,
            registrationOperation,
            conditionOperation,
            readOperation,
            key,
            DisplayType(serviceType),
            DisplayType(implementationType),
            lifetime,
            isTrueArm,
            evidence));
    }

    public ConditionalDependencyInjectionFactSet Build(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        ImmutableArray<DependencyInjectionRegistrationFact> registrations)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint, nameof(programIndexFingerprint));

        var registrationByOperation = registrations
            .GroupBy(registration => registration.Operation.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var arms = _arms
            .DistinctBy(draft => draft.RegistrationOperation.Value)
            .OrderBy(draft => draft.RegistrationOperation.Value, StringComparer.Ordinal)
            .Select(draft => ProjectArm(profile.Id, draft, registrationByOperation))
            .Where(arm => arm is not null)
            .Select(arm => arm!)
            .ToImmutableArray();
        var groups = BuildGroups(profile.Id, arms, registrations);
        var debugProjection = BuildDebugProjection(
            profile,
            programIndexFingerprint,
            arms,
            groups,
            diagnostics.Length);
        return new ConditionalDependencyInjectionFactSet(
            1,
            ProducerVersion,
            profile,
            programIndexFingerprint,
            arms,
            groups,
            diagnostics,
            debugProjection);
    }

    /// <summary>
    /// Projects one arm draft into a Roslyn-neutral arm fact bound to the exact completed DI
    /// registration identity by operation. A draft whose registration the completed set does not
    /// carry fails closed (the arm cannot be exact without its registration anchor). The arm identity
    /// canonically includes the profile, the top-level method, the condition/read operations, the
    /// key, the service type, the semantic polarity, the registration operation, and the exact bound
    /// registration identity — never an implementation display-only substitute (regression).
    /// </summary>
    private static ConditionalDependencyInjectionRegistrationArmFact? ProjectArm(
        CompilationProfileId profileId,
        ArmDraft draft,
        Dictionary<string, DependencyInjectionRegistrationFact> registrationByOperation)
    {
        if (!registrationByOperation.TryGetValue(draft.RegistrationOperation.Value, out var registration))
        {
            return null;
        }

        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "conditional-di-arm",
            draft.ProgramMethod,
            draft.RegistrationOperation,
            $"{draft.IsTrueArm.ToString()}|{draft.ConditionOperation.Value}|{draft.ReadOperation.Value}|{draft.Key}|{draft.ServiceTypeName}|{draft.RegistrationOperation.Value}|{registration.Id.Value}"));
        return new ConditionalDependencyInjectionRegistrationArmFact(
            id,
            draft.ProgramMethod,
            draft.RegistrationOperation,
            draft.ConditionOperation,
            draft.ReadOperation,
            draft.Key,
            registration.Id,
            draft.ServiceTypeName,
            draft.ImplementationTypeName,
            draft.Lifetime,
            draft.IsTrueArm,
            draft.Evidence,
            draft.Evidence.Max(item => item.Certainty));
    }

    /// <summary>
    /// Builds an alternative group only when one service type has exactly one admitted registration
    /// in each opposite arm of the same condition operation, both arms share a supported lifetime and
    /// the same read/key/program-method, and the admitted registration set for that service type is
    /// exactly the two arm registrations. Two independent <c>if</c> statements, a missing else,
    /// same-polarity registrations, overlapping/additional registrations, unresolved conditions, and
    /// unsupported registrations produce no group (accepted contract requirement 4).
    /// </summary>
    private static ImmutableArray<ConditionalDependencyInjectionGroupFact> BuildGroups(
        CompilationProfileId profileId,
        ImmutableArray<ConditionalDependencyInjectionRegistrationArmFact> arms,
        ImmutableArray<DependencyInjectionRegistrationFact> registrations)
    {
        var groups = new List<ConditionalDependencyInjectionGroupFact>();
        foreach (var serviceType in registrations
                     .Select(registration => registration.ServiceType)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            var serviceRegistrations = registrations
                .Where(registration => registration.ServiceType == serviceType)
                .Select(registration => registration.Id.Value)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var serviceArms = arms
                .Where(arm => arm.ServiceType == serviceType)
                .ToArray();
            if (serviceArms.Length != 2
                || serviceRegistrations.Length != 2
                || serviceArms.Count(arm => arm.IsTrueArm) != 1
                || serviceArms.Count(arm => !arm.IsTrueArm) != 1)
            {
                continue;
            }

            var trueArm = serviceArms.First(arm => arm.IsTrueArm);
            var falseArm = serviceArms.First(arm => !arm.IsTrueArm);
            if (trueArm.ProgramMethod != falseArm.ProgramMethod
                || trueArm.ConditionOperation != falseArm.ConditionOperation
                || trueArm.ReadOperation != falseArm.ReadOperation
                || !string.Equals(trueArm.Key, falseArm.Key, StringComparison.Ordinal)
                || trueArm.Lifetime != falseArm.Lifetime)
            {
                continue;
            }

            var armRegistrationIds = new[] { trueArm.RegistrationId.Value, falseArm.RegistrationId.Value }
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!serviceRegistrations.SequenceEqual(armRegistrationIds))
            {
                continue;
            }

            var combinedEvidence = CombineEvidence(trueArm.Evidence, falseArm.Evidence);
            // The group identity canonically includes the profile, the top-level method, the
            // condition/read operations, the key, the service type/lifetime, and the exact true and
            // false registration identities — never an implementation display-only substitute
            // (regression). The registration identities churn when the bound registrations change.
            var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
                profileId,
                "conditional-di-group",
                trueArm.ProgramMethod,
                trueArm.ConditionOperation,
                $"{trueArm.ReadOperation.Value}|{trueArm.Key}|{serviceType}|{trueArm.Lifetime.ToString()}|{trueArm.RegistrationId.Value}|{falseArm.RegistrationId.Value}"));
            groups.Add(new ConditionalDependencyInjectionGroupFact(
                id,
                trueArm.ProgramMethod,
                trueArm.ConditionOperation,
                trueArm.ReadOperation,
                trueArm.Key,
                serviceType,
                trueArm.RegistrationId,
                falseArm.RegistrationId,
                trueArm.ImplementationType,
                falseArm.ImplementationType,
                trueArm.Lifetime,
                combinedEvidence,
                combinedEvidence.Max(item => item.Certainty)));
        }

        return groups
            .OrderBy(group => group.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    /// Exact admission mirror of the accepted DI projection: the unreduced original extension method
    /// must live on the authoritative <c>ServiceCollectionServiceExtensions</c> type, carry exactly
    /// two type arguments, take only the <c>IServiceCollection</c> receiver, and use a supported
    /// lifetime name. Keyed, TryAdd, factory, instance, non-generic, collection, and open-generic
    /// forms fail closed. This mirrors the accepted collector without modifying it; both admit the
    /// identical invocation set because they resolve the same authoritative symbols from the same
    /// compilation.
    /// </summary>
    private static bool TryAdmitRegistration(
        IInvocationOperation call,
        AuthoritativeConditionalDiSymbols authoritative,
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

        if (unreduced.Parameters.Length != 1
            || !SymbolEqualityComparer.Default.Equals(unreduced.Parameters[0].Type, authoritative.ServiceCollection))
        {
            return false;
        }

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

    private static ImmutableArray<EvidenceRef> CombineEvidence(
        ImmutableArray<EvidenceRef> first,
        ImmutableArray<EvidenceRef> second)
        => first
            .AddRange(second)
            .DistinctBy(item => item.Id.Value, StringComparer.Ordinal)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();

    private static string BuildDebugProjection(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<ConditionalDependencyInjectionRegistrationArmFact> arms,
        ImmutableArray<ConditionalDependencyInjectionGroupFact> groups,
        int diagnosticCount)
    {
        var lines = new List<(string Id, string Line)>();
        foreach (var arm in arms)
        {
            lines.Add((
                arm.Id.Value,
                $"arm {arm.Id.Value} method={arm.ProgramMethod.Value} registration={arm.RegistrationOperation.Value} condition={arm.ConditionOperation.Value} read={arm.ReadOperation.Value} key={arm.Key} registrationId={arm.RegistrationId.Value} service={arm.ServiceType} implementation={arm.ImplementationType} lifetime={arm.Lifetime.ToString()} isTrue={arm.IsTrueArm.ToString()} certainty={arm.Certainty.ToString()}"));
        }

        foreach (var group in groups)
        {
            lines.Add((
                group.Id.Value,
                $"group {group.Id.Value} method={group.ProgramMethod.Value} condition={group.ConditionOperation.Value} read={group.ReadOperation.Value} key={group.Key} service={group.ServiceType} true={group.TrueRegistrationId.Value} false={group.FalseRegistrationId.Value} lifetime={group.Lifetime.ToString()} certainty={group.Certainty.ToString()}"));
        }

        var builder = new StringBuilder();
        builder.Append("conditional-dependency-injection:v1").Append('\n');
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

    private sealed record ArmDraft(
        MethodId ProgramMethod,
        OperationId RegistrationOperation,
        OperationId ConditionOperation,
        OperationId ReadOperation,
        string Key,
        string ServiceTypeName,
        string ImplementationTypeName,
        DependencyInjectionLifetime Lifetime,
        bool IsTrueArm,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record AuthoritativeConditionalDiSymbols(
        INamedTypeSymbol? ServiceCollection,
        INamedTypeSymbol? ExtensionsClass);
}
