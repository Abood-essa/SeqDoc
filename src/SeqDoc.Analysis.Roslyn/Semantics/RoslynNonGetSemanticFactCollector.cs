using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SeqDoc.Analysis.Roslyn.Behavior;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Semantics;

/// <summary>
/// Accumulates non-Get semantic companion fact drafts during one Roslyn compilation/extraction session
/// and builds the Roslyn-neutral, memory-only <see cref="NonGetSemanticFactSet"/>. It projects
/// compiler-proven status-switch arms, exact property/enum state assignments, conservative relational
/// patterns and DateTime comparisons, evidenced source observations (never interactions), and the
/// ordered EF query/mutation sequence with exact EF mutation facts. Every draft reuses the stable
/// MethodId/OperationId anchors of the same operation traversal that produced the accepted behavior
/// input, and no draft adds or reinterprets Method Flow edges or fingerprints.
/// </summary>
internal sealed class RoslynNonGetSemanticFactCollector
{
    private const string ProducerVersion = "0.1.0-alpha";
    internal const string ControllerBaseMetadataName = "Microsoft.AspNetCore.Mvc.ControllerBase";

    private const string EntityFrameworkCoreAssembly = "Microsoft.EntityFrameworkCore";
    private const string EntityFrameworkCoreRelationalAssembly = "Microsoft.EntityFrameworkCore.Relational";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string LocalViewMetadataName = "Microsoft.EntityFrameworkCore.ChangeTracking.LocalView`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string QueryableExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string ListMetadataName = "System.Collections.Generic.List`1";
    private const string ICollectionMetadataName = "System.Collections.Generic.ICollection`1";

    internal static readonly ImmutableDictionary<string, HttpOutcomeHelperKind> OutcomeHelperNames =
        new Dictionary<string, HttpOutcomeHelperKind>(StringComparer.Ordinal)
        {
            ["Ok"] = HttpOutcomeHelperKind.Ok,
            ["CreatedAtAction"] = HttpOutcomeHelperKind.CreatedAtAction,
            ["BadRequest"] = HttpOutcomeHelperKind.BadRequest,
            ["NotFound"] = HttpOutcomeHelperKind.NotFound,
            ["Conflict"] = HttpOutcomeHelperKind.Conflict,
            ["StatusCode"] = HttpOutcomeHelperKind.StatusCode,
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private readonly List<StatusSwitchDraft> _statusSwitchArms = [];
    private readonly List<DirectTerminalDraft> _directTerminals = [];
    private readonly List<StateAssignmentDraft> _stateAssignments = [];
    private readonly List<RelationalTimeDraft> _relationalTimeFacts = [];
    private readonly List<ObservationDraft> _observations = [];
    private readonly List<EntityFrameworkMutationDraft> _mutations = [];
    private readonly List<EfSequenceDraft> _efSequence = [];
    private readonly Dictionary<StableProjectId, INamedTypeSymbol?> _controllerBaseByProject = [];
    private readonly HashSet<string> _admittedStatusArmMethods = new(StringComparer.Ordinal);
    private readonly Dictionary<MethodId, int> _sourceOrdinals = [];

    public void SetAuthoritativeSymbols(StableProjectId project, INamedTypeSymbol? controllerBase)
    {
        _controllerBaseByProject[project] = controllerBase;
    }

    public bool TryResolveControllerBase(StableProjectId project, out INamedTypeSymbol? controllerBase)
        => _controllerBaseByProject.TryGetValue(project, out controllerBase);

    /// <summary>
    /// Records one compiler-proven status-switch arm. The switch value must be a status-typed enum
    /// member access and the arm body must reach exactly one distinct admitted ASP.NET Core outcome
    /// helper; unsupported or ambiguous arms fail closed by producing no fact. A CreatedAtAction arm
    /// carries the compiler-bound target controller method identity.
    /// </summary>
    public void AddStatusSwitchArm(
        MethodId method,
        OperationId switchOperation,
        string statusEnumType,
        string statusMemberName,
        HttpOutcomeHelperKind helperKind,
        OperationId outcomeOperation,
        string? createdActionName,
        MethodId? createdTargetMethod,
        ImmutableArray<EvidenceRef> evidence)
    {
        _admittedStatusArmMethods.Add(method.Value);
        _statusSwitchArms.Add(new StatusSwitchDraft(
            method,
            switchOperation,
            statusEnumType,
            statusMemberName,
            helperKind,
            outcomeOperation,
            createdActionName,
            createdTargetMethod,
            evidence));
    }

    /// <summary>
    /// Records one compiler-proven direct terminal outcome: an admitted outcome helper invocation
    /// reached outside every status-switch arm of a method that already carries at least one admitted
    /// status arm. The exact invocation operation identity anchors the scenario join; the fact never
    /// synthesizes a status member and never claims a status mapping.
    /// </summary>
    public void AddDirectTerminalOutcome(
        MethodId method,
        OperationId operation,
        HttpOutcomeHelperKind helperKind,
        string? createdActionName,
        MethodId? createdTargetMethod,
        ImmutableArray<EvidenceRef> evidence)
    {
        int ordinal = NextSourceOrdinal(method);
        _directTerminals.Add(new DirectTerminalDraft(
            method,
            operation,
            helperKind,
            createdActionName,
            createdTargetMethod,
            ordinal,
            evidence));
    }

    /// <summary>True when the method already produced at least one admitted status-switch arm.</summary>
    public bool HasAdmittedStatusSwitchArm(MethodId method) => _admittedStatusArmMethods.Contains(method.Value);

    public void AddStateAssignment(
        MethodId method,
        OperationId operation,
        string targetMember,
        string targetType,
        StateAssignmentValueKind valueKind,
        string value,
        ImmutableArray<EvidenceRef> evidence)
    {
        int ordinal = NextSourceOrdinal(method);
        _stateAssignments.Add(new StateAssignmentDraft(
            method,
            operation,
            targetMember,
            targetType,
            valueKind,
            value,
            evidence,
            ordinal));
    }

    public void AddRelationalTimeFact(
        MethodId method,
        OperationId operation,
        RelationalTimeFactKind kind,
        ComparisonOperatorKind @operator,
        OperationId leftOperation,
        OperationId? rightOperation,
        string? thresholdValue,
        ImmutableArray<EvidenceRef> evidence) =>
        _relationalTimeFacts.Add(new RelationalTimeDraft(
            method,
            operation,
            kind,
            @operator,
            leftOperation,
            rightOperation,
            thresholdValue,
            evidence));

    public void AddSourceObservation(
        MethodId method,
        OperationId anchorOperation,
        SourceObservationKind kind,
        string text,
        ImmutableArray<EvidenceRef> evidence) =>
        _observations.Add(new ObservationDraft(method, anchorOperation, kind, text, evidence));

    /// <summary>
    /// Records one exact EF mutation and advances the method's deterministic source-order ordinal. The
    /// mutation must be an exact symbol of the EF mutation vocabulary; lookalikes never produce a fact.
    /// </summary>
    public void AddEfMutation(
        MethodId method,
        OperationId operation,
        EntityFrameworkMutationKind kind,
        string dbContextType,
        string entityType,
        OperationId? argumentOperation,
        string? targetMember,
        ImmutableArray<EvidenceRef> evidence)
    {
        if (kind == EntityFrameworkMutationKind.Unknown && dbContextType == "RawSql")
        {
            AddSourceObservation(method, operation, SourceObservationKind.Note,
                $"EF {targetMember ?? "relational SQL"} source boundary.", evidence);
            return;
        }
        int ordinal = NextSourceOrdinal(method);
        _mutations.Add(new EntityFrameworkMutationDraft(
            method,
            operation,
            kind,
            ordinal,
            dbContextType,
            entityType,
            argumentOperation,
            targetMember,
            evidence));
    }

    /// <summary>Records one EF query terminal in the method's deterministic source order.</summary>
    public void AddEfQueryTerminal(MethodId method, OperationId operation)
    {
        int ordinal = NextSourceOrdinal(method);
        _efSequence.Add(new EfSequenceDraft(method, operation, EfOperationSequenceKind.QueryTerminal, ordinal));
    }

    /// <summary>
    /// Advances the method's single source-order ordinal counter shared by state assignments, EF
    /// query terminals, and EF mutations. One counter makes the interleaved assignment/query/mutation
    /// order authoritative wherever semantics claim source order.
    /// </summary>
    private int NextSourceOrdinal(MethodId method)
    {
        int ordinal = _sourceOrdinals.GetValueOrDefault(method);
        _sourceOrdinals[method] = ordinal + 1;
        return ordinal;
    }

    public NonGetSemanticFactSet Build(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint, nameof(programIndexFingerprint));

        var statusSwitchArms = ProjectAndDeDuplicate(
            _statusSwitchArms.Select(draft => ProjectStatusSwitch(profile.Id, draft)),
            fact => fact.Id,
            "status-switch");
        var directTerminals = ProjectAndDeDuplicate(
            _directTerminals.Select(draft => ProjectDirectTerminal(profile.Id, draft)),
            fact => fact.Id,
            "direct-terminal-outcome");
        var stateAssignments = ProjectStateAssignmentsAndDeDuplicate(profile.Id, _stateAssignments);
        var relationalTimeFacts = ProjectAndDeDuplicate(
            _relationalTimeFacts.Select(draft => ProjectRelationalTime(profile.Id, draft)),
            fact => fact.Id,
            "relational-time");
        var observations = ProjectAndDeDuplicate(
            _observations.Select(draft => ProjectObservation(profile.Id, draft)),
            fact => fact.Id,
            "source-observation");
        var mutations = ProjectMutations(profile.Id, _mutations);
        var efSequence = _efSequence
            .OrderBy(draft => draft.Method.Value, StringComparer.Ordinal)
            .ThenBy(draft => draft.Ordinal)
            .Select(draft => new EfOperationSequenceFact(draft.Method, draft.Operation, draft.Kind, draft.Ordinal))
            .ToImmutableArray();
        var debugProjection = BuildDebugProjection(
            profile,
            programIndexFingerprint,
            statusSwitchArms,
            directTerminals,
            stateAssignments,
            relationalTimeFacts,
            observations,
            mutations,
            efSequence,
            diagnostics.Length);

        return new NonGetSemanticFactSet(
            1,
            ProducerVersion,
            profile,
            programIndexFingerprint,
            statusSwitchArms,
            directTerminals,
            stateAssignments,
            relationalTimeFacts,
            observations,
            mutations,
            efSequence,
            diagnostics,
            debugProjection);
    }

    private static ImmutableArray<T> ProjectAndDeDuplicate<T>(
        IEnumerable<T> facts,
        Func<T, SemanticFactId> idSelector,
        string kind)
    {
        var result = new List<T>();
        foreach (var group in facts.GroupBy(fact => idSelector(fact).Value, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ordered = group.ToArray();
            var duplicates = ordered.Skip(1).ToArray();
            if (duplicates.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Conflicting non-Get semantic-fact drafts projected onto identity '{idSelector(duplicates[0]).Value}' for kind '{kind}'.");
            }

            result.Add(ordered[0]);
        }

        return result.ToImmutableArray();
    }

    /// <summary>
    /// Projects state-assignment drafts and deduplicates identical compiler projections that share one
    /// semantic fact ID. The same compiler-proven assignment can be visited more than once under a
    /// shared operation identity; when every projected semantic field is identical the drafts are the
    /// same fact and exactly one survives, keeping the first deterministic source ordinal. A
    /// same-identity group whose projected content differs in any semantic field remains a genuine
    /// conflict and fails closed exactly like every other fact kind. The equivalence deliberately
    /// ignores only the fact ID (already the group key) and the sequence ordinal (deduplication keeps
    /// the first position); it never adds new assignment semantics.
    /// </summary>
    private static ImmutableArray<StateAssignmentSemanticFact> ProjectStateAssignmentsAndDeDuplicate(
        CompilationProfileId profileId,
        IEnumerable<StateAssignmentDraft> drafts)
    {
        var result = new List<StateAssignmentSemanticFact>();
        foreach (var group in drafts
                     .Select(draft => ProjectStateAssignment(profileId, draft))
                     .GroupBy(fact => fact.Id.Value, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ordered = group.ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (!AreSemanticallyEquivalentStateAssignments(ordered[0], ordered[index]))
                {
                    throw new InvalidOperationException(
                        $"Conflicting non-Get semantic-fact drafts projected onto identity '{ordered[0].Id.Value}' for kind 'state-assignment'.");
                }
            }

            // Identical projections keep the first source ordinal; ordinals advance monotonically per
            // method, so the earliest draft in the group carries the smallest position.
            result.Add(ordered.MinBy(fact => fact.SequenceOrdinal)!);
        }

        return result.ToImmutableArray();
    }

    /// <summary>
    /// Compares two state-assignment facts by every projected semantic field except the fact ID
    /// (already the group key) and the sequence ordinal (deduplication keeps the first position).
    /// Evidence arrays must be element-wise equal so a repeated visit never alters retained evidence.
    /// </summary>
    private static bool AreSemanticallyEquivalentStateAssignments(
        StateAssignmentSemanticFact left,
        StateAssignmentSemanticFact right)
        => left.Method == right.Method
            && left.Operation == right.Operation
            && string.Equals(left.TargetMember, right.TargetMember, StringComparison.Ordinal)
            && string.Equals(left.TargetType, right.TargetType, StringComparison.Ordinal)
            && left.ValueKind == right.ValueKind
            && string.Equals(left.Value, right.Value, StringComparison.Ordinal)
            && left.Certainty == right.Certainty
            && left.Evidence.SequenceEqual(right.Evidence);

    private static StatusSwitchArmFact ProjectStatusSwitch(CompilationProfileId profileId, StatusSwitchDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "status-switch-arm",
            draft.Method,
            draft.OutcomeOperation,
            $"{draft.StatusEnumType}|{draft.StatusMemberName}|{draft.HelperKind.ToString()}"));
        return new StatusSwitchArmFact(
            id,
            draft.Method,
            draft.SwitchOperation,
            draft.StatusEnumType,
            draft.StatusMemberName,
            draft.HelperKind,
            draft.OutcomeOperation,
            draft.CreatedActionName,
            draft.CreatedTargetMethod,
            draft.Evidence,
            CertaintyLevel.Exact);
    }

    private static DirectTerminalOutcomeFact ProjectDirectTerminal(CompilationProfileId profileId, DirectTerminalDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "direct-terminal-outcome",
            draft.Method,
            draft.Operation,
            $"{draft.HelperKind.ToString()}|{draft.CreatedActionName ?? string.Empty}|{draft.CreatedTargetMethod?.Value ?? string.Empty}"));
        return new DirectTerminalOutcomeFact(
            id,
            draft.Method,
            draft.Operation,
            draft.HelperKind,
            draft.CreatedActionName,
            draft.CreatedTargetMethod,
            draft.SequenceOrdinal,
            draft.Evidence,
            CertaintyLevel.Exact);
    }

    private static StateAssignmentSemanticFact ProjectStateAssignment(CompilationProfileId profileId, StateAssignmentDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "state-assignment",
            draft.Method,
            draft.Operation,
            $"{draft.TargetMember}|{draft.ValueKind.ToString()}|{draft.Value}"));
        return new StateAssignmentSemanticFact(
            id,
            draft.Method,
            draft.Operation,
            draft.TargetMember,
            draft.TargetType,
            draft.ValueKind,
            draft.Value,
            draft.Evidence,
            CertaintyLevel.Exact,
            draft.SequenceOrdinal);
    }

    private static RelationalTimeSemanticFact ProjectRelationalTime(CompilationProfileId profileId, RelationalTimeDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "relational-time",
            draft.Method,
            draft.Operation,
            $"{draft.Kind.ToString()}|{draft.Operator.ToString()}|{draft.ThresholdValue ?? draft.RightOperation?.Value ?? string.Empty}"));
        return new RelationalTimeSemanticFact(
            id,
            draft.Method,
            draft.Operation,
            draft.Kind,
            draft.Operator,
            draft.LeftOperation,
            draft.RightOperation,
            draft.ThresholdValue,
            draft.Evidence,
            CertaintyLevel.Conservative);
    }

    private static SourceObservationSemanticFact ProjectObservation(CompilationProfileId profileId, ObservationDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "source-observation",
            draft.Method,
            draft.AnchorOperation,
            $"{draft.Kind.ToString()}|{draft.Text}"));
        return new SourceObservationSemanticFact(
            id,
            draft.Method,
            draft.AnchorOperation,
            draft.Kind,
            draft.Text,
            draft.Evidence,
            CertaintyLevel.Conservative);
    }

    private static ImmutableArray<EntityFrameworkMutationFact> ProjectMutations(
        CompilationProfileId profileId,
        List<EntityFrameworkMutationDraft> drafts)
    {
        var result = new List<EntityFrameworkMutationFact>();
        foreach (var group in drafts
                     .GroupBy(draft => draft.Operation.Value)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var ordered = group.ToArray();
            var first = ordered[0];
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index].Kind != first.Kind
                    || ordered[index].Method != first.Method
                    || ordered[index].DbContextType != first.DbContextType
                    || ordered[index].EntityType != first.EntityType)
                {
                    throw new InvalidOperationException(
                        $"Conflicting EF mutation drafts projected onto operation '{first.Operation.Value}'.");
                }
            }

            result.Add(new EntityFrameworkMutationFact
            {
                Id = StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
                    profileId,
                    EntityFrameworkMutationModelId,
                    EntityFrameworkMutationModelVersion,
                    "ef-mutation",
                    new OperationBehaviorFactAnchor(first.Method, first.Operation),
                    0)),
                Method = first.Method,
                Operation = first.Operation,
                MutationKind = first.Kind,
                SequenceOrdinal = first.SequenceOrdinal,
                DbContextType = first.DbContextType,
                EntityType = first.EntityType,
                ArgumentOperation = first.ArgumentOperation,
                TargetMember = first.TargetMember,
                Evidence = first.Evidence,
                Certainty = CertaintyLevel.Exact,
            });
        }

        return result
            .OrderBy(fact => fact.Method.Value, StringComparer.Ordinal)
            .ThenBy(fact => fact.SequenceOrdinal)
            .ToImmutableArray();
    }

    private const string EntityFrameworkMutationModelId = "seqdoc.entityframework.mutations";
    private const string EntityFrameworkMutationModelVersion = "1.0.0";

    /// <summary>Recognizes one exact EF mutation call from compiler symbols and returns its shape.</summary>
    public static bool TryMatchMutation(
        IInvocationOperation call,
        out EntityFrameworkMutationKind kind,
        out string dbContextType,
        out string entityType,
        out string? targetMember)
    {
        kind = EntityFrameworkMutationKind.Unknown;
        dbContextType = string.Empty;
        entityType = string.Empty;
        targetMember = null;
        var target = call.TargetMethod;
        if (target is null
            || target.ContainingType is not INamedTypeSymbol containing)
        {
            return false;
        }

        var original = (target.ReducedFrom ?? target).OriginalDefinition;
        containing = original.ContainingType as INamedTypeSymbol ?? containing;

        var metadataName = RoslynProgramIndexExtractor.GetMetadataName(containing);
        if (original.MetadataName == "Clear")
        {
            // A tracked-collection clear is admitted on the EF LocalView (Local.Clear) and on the BCL
            // collection navigations (List<T>.Clear / ICollection<T>.Clear) that EF change tracking
            // observes. LocalView is the change-tracked local view of a DbSet by type identity; a BCL
            // collection clear is admitted only when the receiver is a navigation member
            // (property/field reference) of an entity, never an unrelated local collection.
            if (metadataName == LocalViewMetadataName)
            {
                if (original.ContainingAssembly?.Identity.Name != EntityFrameworkCoreAssembly
                    || !IsEfCoreVersion(original.ContainingAssembly)
                    || original.Arity != 0
                    || original.Parameters.Length != 0
                    || original.ReturnType.SpecialType != SpecialType.System_Void
                    || !TryResolveCollectionEntity(call, out entityType))
                {
                    return false;
                }

                kind = EntityFrameworkMutationKind.Clear;
                targetMember = $"{metadataName}.Clear";
                return true;
            }

            if (metadataName is not (ListMetadataName or ICollectionMetadataName))
            {
                return false;
            }

            if (ResolveReceiver(call) is not (IPropertyReferenceOperation or IFieldReferenceOperation)
                || !TryResolveCollectionEntity(call, out entityType))
            {
                return false;
            }

            kind = EntityFrameworkMutationKind.Clear;
            targetMember = $"{metadataName}.Clear";
            return true;
        }

        var containingAssembly = original.ContainingAssembly?.Identity.Name;
        if (string.IsNullOrWhiteSpace(containingAssembly)
            || !IsEfCoreVersion(original.ContainingAssembly))
        {
            return false;
        }

        switch (original.MetadataName)
        {
            case "Add":
                if (!IsExactDbSetAdd(target) || !TryResolveDbSetEntity(call, out entityType, out dbContextType))
                {
                    return false;
                }

                kind = EntityFrameworkMutationKind.Add;
                break;
            case "RemoveRange":
                if (!IsExactDbSetRemoveRange(target) || !TryResolveDbSetEntity(call, out entityType, out dbContextType))
                {
                    return false;
                }

                kind = EntityFrameworkMutationKind.RemoveRange;
                break;
            case "SaveChanges":
                if (!IsExactSaveChanges(target))
                {
                    return false;
                }

                dbContextType = ResolveReceiverType(call);
                if (string.IsNullOrWhiteSpace(dbContextType))
                {
                    return false;
                }

                kind = EntityFrameworkMutationKind.SaveChanges;
                break;
            case "SaveChangesAsync":
                if (!IsExactSaveChangesAsync(target))
                {
                    return false;
                }

                dbContextType = ResolveReceiverType(call);
                if (string.IsNullOrWhiteSpace(dbContextType))
                {
                    return false;
                }

                kind = EntityFrameworkMutationKind.SaveChangesAsync;
                break;
            case "FromSqlRaw":
            case "ExecuteSqlRawAsync":
                if (!IsExactRawSql(original, out var rawFamily))
                {
                    return false;
                }

                kind = EntityFrameworkMutationKind.Unknown;
                dbContextType = "RawSql";
                targetMember = rawFamily;
                break;
            default:
                return false;
        }

        return true;
    }

    /// <summary>Recognizes one exact EF query terminal (SingleOrDefaultAsync, FirstOrDefaultAsync, or CountAsync).</summary>
    public static bool TryMatchQueryTerminal(IMethodSymbol target, out string terminalName)
    {
        terminalName = string.Empty;
        if (target is null)
        {
            return false;
        }

        var original = target.OriginalDefinition;
        if (original.ContainingAssembly?.Identity.Name != EntityFrameworkCoreAssembly
            || !IsEfCoreVersion(original.ContainingAssembly)
            || RoslynProgramIndexExtractor.GetMetadataName(original.ContainingType) != QueryableExtensionsMetadataName
            || !original.IsExtensionMethod
            || !original.IsGenericMethod
            || original.Arity != 1)
        {
            return false;
        }

        var tSource = original.TypeParameters[0];
        terminalName = original.MetadataName;
        switch (terminalName)
        {
            case "SingleOrDefaultAsync":
            case "FirstOrDefaultAsync":
                if (!IsExactTaskReturn(original.ReturnType, tSource))
                {
                    terminalName = string.Empty;
                    return false;
                }

                break;
            case "CountAsync":
                if (!IsExactTaskReturn(original.ReturnType, SpecialType.System_Int32))
                {
                    terminalName = string.Empty;
                    return false;
                }

                break;
            default:
                terminalName = string.Empty;
                return false;
        }

        var hasPredicate = terminalName != "CountAsync" || original.Parameters.Length == 3;
        if ((terminalName == "CountAsync" && original.Parameters.Length is not (2 or 3))
            || (terminalName != "CountAsync" && original.Parameters.Length != 3)
            || original.Parameters.Any(parameter => parameter.RefKind != RefKind.None)
            || !IsExactQueryableParameter(original.Parameters[0], tSource))
        {
            terminalName = string.Empty;
            return false;
        }

        if (!hasPredicate)
        {
            if (!IsCancellationToken(original.Parameters[1]))
            {
                terminalName = string.Empty;
                return false;
            }
        }
        else
        {
            if (!IsPredicateExpression(original.Parameters[1], tSource) || !IsCancellationToken(original.Parameters[2]))
            {
                terminalName = string.Empty;
                return false;
            }
        }

        return true;
    }

    private static bool IsExactQueryableParameter(IParameterSymbol parameter, ITypeSymbol elementType)
        => parameter.Type is INamedTypeSymbol queryable
            && queryable.IsGenericType
            && queryable.TypeArguments.Length == 1
            && RoslynProgramIndexExtractor.GetMetadataName(queryable.OriginalDefinition) == "System.Linq.IQueryable`1"
            && SymbolEqualityComparer.Default.Equals(queryable.TypeArguments[0], elementType);

    private static bool IsExactTaskReturn(ITypeSymbol returnType, ITypeSymbol expectedType)
        => returnType is INamedTypeSymbol task
            && task.IsGenericType
            && task.TypeArguments.Length == 1
            && task.OriginalDefinition.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "System.Threading.Tasks.Task<TResult>"
            && SymbolEqualityComparer.Default.Equals(task.TypeArguments[0], expectedType);

    private static bool IsExactTaskReturn(ITypeSymbol returnType, SpecialType expectedType)
        => returnType is INamedTypeSymbol task
            && task.IsGenericType
            && task.TypeArguments.Length == 1
            && task.OriginalDefinition.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "System.Threading.Tasks.Task<TResult>"
            && task.TypeArguments[0].SpecialType == expectedType;

    private static bool IsExactSaveChanges(IMethodSymbol target)
    {
        var original = target.OriginalDefinition;
        if (original.ContainingAssembly?.Identity.Name != EntityFrameworkCoreAssembly
            || !IsEfCoreVersion(original.ContainingAssembly)
            || RoslynProgramIndexExtractor.GetMetadataName(original.ContainingType) != DbContextMetadataName
            || original.ReturnType.SpecialType != SpecialType.System_Int32)
        {
            return false;
        }

        return original.Parameters.Length == 0;
    }

    private static bool IsExactSaveChangesAsync(IMethodSymbol target)
    {
        var original = target.OriginalDefinition;
        if (original.ContainingAssembly?.Identity.Name != EntityFrameworkCoreAssembly
            || !IsEfCoreVersion(original.ContainingAssembly)
            || RoslynProgramIndexExtractor.GetMetadataName(original.ContainingType) != DbContextMetadataName
            || original.ReturnType is not INamedTypeSymbol taskType
            || taskType.Name != "Task"
            || taskType.TypeArguments.Length != 1
            || taskType.TypeArguments[0].SpecialType != SpecialType.System_Int32)
        {
            return false;
        }

        return original.Parameters.Length == 1 && IsCancellationToken(original.Parameters[0]);
    }

    private static bool IsExactDbSetAdd(IMethodSymbol target)
    {
        var original = target.OriginalDefinition;
        if (original.ContainingAssembly?.Identity.Name != EntityFrameworkCoreAssembly
            || !IsEfCoreVersion(original.ContainingAssembly)
            || RoslynProgramIndexExtractor.GetMetadataName(original.ContainingType) != DbSetMetadataName
            || original.ContainingType.TypeParameters.Length != 1
            || original.Parameters.Length != 1
            || original.Parameters[0].RefKind != RefKind.None)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(original.Parameters[0].Type, original.ContainingType.TypeParameters[0]);
    }

    private static bool IsExactDbSetRemoveRange(IMethodSymbol target)
    {
        var original = target.OriginalDefinition;
        if (original.ContainingAssembly?.Identity.Name != EntityFrameworkCoreAssembly
            || !IsEfCoreVersion(original.ContainingAssembly)
            || RoslynProgramIndexExtractor.GetMetadataName(original.ContainingType) != DbSetMetadataName
            || original.ContainingType.TypeParameters.Length != 1
            || original.Parameters.Length != 1
            || original.Parameters[0].RefKind != RefKind.None)
        {
            return false;
        }

        var entityType = original.ContainingType.TypeParameters[0];
        var paramType = original.Parameters[0].Type;

        if (paramType is IArrayTypeSymbol arrayType)
        {
            return SymbolEqualityComparer.Default.Equals(arrayType.ElementType, entityType);
        }

        if (paramType is INamedTypeSymbol namedType && namedType.Name == "IEnumerable" && namedType.TypeArguments.Length == 1)
        {
            return SymbolEqualityComparer.Default.Equals(namedType.TypeArguments[0], entityType);
        }

        return false;
    }

    private static bool IsCancellationToken(IParameterSymbol parameter)
        => parameter.RefKind == RefKind.None
            && parameter.Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "System.Threading.CancellationToken";

    private static bool IsEfCoreVersion(IAssemblySymbol? assembly)
        => assembly?.Identity.Version == new Version(10, 0, 10, 0);

    private static bool IsExactRawSql(IMethodSymbol original, out string family)
    {
        family = string.Empty;
        if (original.ContainingAssembly?.Identity.Name != EntityFrameworkCoreRelationalAssembly
            || !IsEfCoreVersion(original.ContainingAssembly)
            || original.Parameters.Length != 3
            || original.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
        {
            return false;
        }

        var parameters = original.Parameters;
        if (original.MetadataName == "FromSqlRaw"
            && RoslynProgramIndexExtractor.GetMetadataName(original.ContainingType) == "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions"
            && original.Arity == 1
            && parameters[0].Type is INamedTypeSymbol dbSet
            && RoslynProgramIndexExtractor.GetMetadataName(dbSet.OriginalDefinition) == DbSetMetadataName
            && parameters[1].Type.SpecialType == SpecialType.System_String
            && parameters[2].Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "System.Object[]")
        {
            family = "FromSqlRaw";
            return true;
        }

        if (original.MetadataName == "ExecuteSqlRawAsync"
            && RoslynProgramIndexExtractor.GetMetadataName(original.ContainingType) == "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions"
            && original.Arity == 0
            && parameters[0].Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade"
            && parameters[1].Type.SpecialType == SpecialType.System_String
            && parameters[2].Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "System.Object[]"
            && original.ReturnType is INamedTypeSymbol task
            && task.Name == "Task"
            && task.TypeArguments.Length == 1
            && task.TypeArguments[0].SpecialType == SpecialType.System_Int32)
        {
            family = "ExecuteSqlRawAsync";
            return true;
        }

        return false;
    }

    private static bool IsPredicateExpression(IParameterSymbol parameter, ITypeSymbol entityType)
    {
        if (parameter.RefKind != RefKind.None || parameter.Type is not INamedTypeSymbol exprType)
        {
            return false;
        }

        if (exprType.TypeArguments.Length != 1
            || RoslynProgramIndexExtractor.GetMetadataName(exprType.OriginalDefinition) != "System.Linq.Expressions.Expression`1")
        {
            return false;
        }

        if (exprType.TypeArguments[0] is not INamedTypeSymbol funcType
            || funcType.TypeArguments.Length != 2
            || RoslynProgramIndexExtractor.GetMetadataName(funcType.OriginalDefinition) != "System.Func`2")
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(funcType.TypeArguments[0], entityType)
            && funcType.TypeArguments[1].SpecialType == SpecialType.System_Boolean;
    }

    private static bool TryResolveDbSetEntity(IInvocationOperation call, out string entityType, out string dbContextType)
    {
        entityType = string.Empty;
        dbContextType = string.Empty;
        IOperation? receiver = ResolveReceiver(call);
        if (receiver is null)
        {
            return false;
        }

        if (receiver.Type is not INamedTypeSymbol named
            || !IsDbSetType(named)
            || named.TypeArguments.Length != 1)
        {
            return false;
        }

        entityType = named.TypeArguments[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
        dbContextType = receiver switch
        {
            IPropertyReferenceOperation property => property.Property.ContainingType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
            IFieldReferenceOperation field => field.Field.ContainingType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
            IInvocationOperation setCall when setCall.TargetMethod.Name == "Set" => ResolveReceiver(setCall)?.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) ?? setCall.TargetMethod.ContainingType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
            _ => string.Empty,
        };
        return !string.IsNullOrWhiteSpace(entityType) && !string.IsNullOrWhiteSpace(dbContextType);
    }

    private static bool TryResolveCollectionEntity(IInvocationOperation call, out string entityType)
    {
        entityType = string.Empty;
        IOperation? receiver = ResolveReceiver(call);
        if (receiver is null
            || receiver.Type is not INamedTypeSymbol named
            || !named.IsGenericType
            || named.TypeArguments.Length != 1)
        {
            return false;
        }

        var metadataName = RoslynProgramIndexExtractor.GetMetadataName(named);
        if (metadataName is not (LocalViewMetadataName or ListMetadataName or ICollectionMetadataName))
        {
            return false;
        }

        entityType = named.TypeArguments[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
        return !string.IsNullOrWhiteSpace(entityType);
    }

    private static bool IsDbSetType(INamedTypeSymbol type)
        => type.IsGenericType
            && string.Equals(RoslynProgramIndexExtractor.GetMetadataName(type.OriginalDefinition), DbSetMetadataName, StringComparison.Ordinal);

    private static IOperation? ResolveReceiver(IInvocationOperation call)
    {
        if (call.Instance is not null)
        {
            return UnwrapImplicitConversions(call.Instance);
        }

        if (call.TargetMethod.IsExtensionMethod && call.Arguments.Length > 0)
        {
            return UnwrapImplicitConversions(call.Arguments[0].Value);
        }

        return null;
    }

    private static string ResolveReceiverType(IInvocationOperation call)
        => ResolveReceiver(call)?.Type?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) ?? string.Empty;

    private static IOperation UnwrapImplicitConversions(IOperation operation)
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

    private static string BuildDebugProjection(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<StatusSwitchArmFact> statusSwitchArms,
        ImmutableArray<DirectTerminalOutcomeFact> directTerminals,
        ImmutableArray<StateAssignmentSemanticFact> stateAssignments,
        ImmutableArray<RelationalTimeSemanticFact> relationalTimeFacts,
        ImmutableArray<SourceObservationSemanticFact> observations,
        ImmutableArray<EntityFrameworkMutationFact> mutations,
        ImmutableArray<EfOperationSequenceFact> efSequence,
        int diagnosticCount)
    {
        var lines = new List<(string Id, string Line)>();
        foreach (var fact in statusSwitchArms)
        {
            lines.Add((fact.Id.Value, $"status-switch {fact.Id.Value} method={fact.Method.Value} status={fact.StatusMemberName} helper={fact.HelperKind.ToString()} operation={fact.OutcomeOperation.Value}"));
        }

        foreach (var fact in directTerminals)
        {
            lines.Add((fact.Id.Value, $"direct-terminal {fact.Id.Value} method={fact.Method.Value} helper={fact.HelperKind.ToString()} operation={fact.Operation.Value}"));
        }

        foreach (var fact in stateAssignments)
        {
            lines.Add((fact.Id.Value, $"state-assignment {fact.Id.Value} method={fact.Method.Value} target={fact.TargetMember} kind={fact.ValueKind.ToString()} value={fact.Value}"));
        }

        foreach (var fact in relationalTimeFacts)
        {
            lines.Add((fact.Id.Value, $"relational-time {fact.Id.Value} method={fact.Method.Value} kind={fact.Kind.ToString()} operator={fact.Operator.ToString()} left={fact.LeftOperation.Value} right={fact.RightOperation?.Value ?? fact.ThresholdValue ?? string.Empty}"));
        }

        foreach (var fact in observations)
        {
            lines.Add((fact.Id.Value, $"source-observation {fact.Id.Value} method={fact.Method.Value} kind={fact.Kind.ToString()} text={fact.Text}"));
        }

        foreach (var fact in mutations)
        {
            lines.Add((fact.Id.Value, $"ef-mutation {fact.Id.Value} method={fact.Method.Value} kind={fact.MutationKind.ToString()} ordinal={fact.SequenceOrdinal.ToString(CultureInfo.InvariantCulture)} entity={fact.EntityType}"));
        }

        foreach (var item in efSequence)
        {
            lines.Add(($"ef-sequence:{item.Method.Value}:{item.Ordinal.ToString(CultureInfo.InvariantCulture)}", $"ef-sequence method={item.Method.Value} kind={item.Kind.ToString()} ordinal={item.Ordinal.ToString(CultureInfo.InvariantCulture)} operation={item.Operation.Value}"));
        }

        var builder = new StringBuilder();
        builder.Append("non-get-semantic-facts:v1").Append('\n');
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

    private sealed record StatusSwitchDraft(
        MethodId Method,
        OperationId SwitchOperation,
        string StatusEnumType,
        string StatusMemberName,
        HttpOutcomeHelperKind HelperKind,
        OperationId OutcomeOperation,
        string? CreatedActionName,
        MethodId? CreatedTargetMethod,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record DirectTerminalDraft(
        MethodId Method,
        OperationId Operation,
        HttpOutcomeHelperKind HelperKind,
        string? CreatedActionName,
        MethodId? CreatedTargetMethod,
        int SequenceOrdinal,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record StateAssignmentDraft(
        MethodId Method,
        OperationId Operation,
        string TargetMember,
        string TargetType,
        StateAssignmentValueKind ValueKind,
        string Value,
        ImmutableArray<EvidenceRef> Evidence,
        int SequenceOrdinal);

    private sealed record RelationalTimeDraft(
        MethodId Method,
        OperationId Operation,
        RelationalTimeFactKind Kind,
        ComparisonOperatorKind Operator,
        OperationId LeftOperation,
        OperationId? RightOperation,
        string? ThresholdValue,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record ObservationDraft(
        MethodId Method,
        OperationId AnchorOperation,
        SourceObservationKind Kind,
        string Text,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record EntityFrameworkMutationDraft(
        MethodId Method,
        OperationId Operation,
        EntityFrameworkMutationKind Kind,
        int SequenceOrdinal,
        string DbContextType,
        string EntityType,
        OperationId? ArgumentOperation,
        string? TargetMember,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record EfSequenceDraft(
        MethodId Method,
        OperationId Operation,
        EfOperationSequenceKind Kind,
        int Ordinal);
}
