using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Semantics;

/// <summary>
/// Accumulates structural result companion fact drafts during one Roslyn compilation/extraction
/// session and builds the Roslyn-neutral, memory-only <see cref="StructuralResultFactSet"/>. Factory
/// admission requires the compiler-proven self-returning static factory shape on a result-shaped type
/// (an instance boolean IsSuccess member plus at least one self-returning factory); decision
/// admission requires a branch on the exact IsSuccess member of a result-shaped type whose success
/// and failure paths lead to exact ControllerBase outcome helpers. Lookalike shapes and unproven
/// control relationships fail closed by producing no fact.
/// </summary>
internal sealed class RoslynStructuralResultFactCollector
{
    private const string ProducerVersion = "0.1.0-alpha";
    internal const string ControllerBaseMetadataName = "Microsoft.AspNetCore.Mvc.ControllerBase";

    private static readonly ImmutableDictionary<string, HttpOutcomeHelperKind> OutcomeHelperNames =
        new Dictionary<string, HttpOutcomeHelperKind>(StringComparer.Ordinal)
        {
            ["Ok"] = HttpOutcomeHelperKind.Ok,
            ["CreatedAtAction"] = HttpOutcomeHelperKind.CreatedAtAction,
            ["BadRequest"] = HttpOutcomeHelperKind.BadRequest,
            ["NotFound"] = HttpOutcomeHelperKind.NotFound,
            ["Conflict"] = HttpOutcomeHelperKind.Conflict,
            ["StatusCode"] = HttpOutcomeHelperKind.StatusCode,
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private readonly List<FactoryDraft> _factories = [];
    private readonly List<DecisionDraft> _decisions = [];
    private readonly Dictionary<StableProjectId, INamedTypeSymbol?> _controllerBaseByProject = [];

    public void SetAuthoritativeSymbols(StableProjectId project, INamedTypeSymbol? controllerBase)
    {
        _controllerBaseByProject[project] = controllerBase;
    }

    /// <summary>
    /// Records one admitted result-factory invocation. Admission requires the compiler-proven
    /// self-returning factory shape on a result-shaped type AND a proven polarity/kind: the factory
    /// method must return an object construction whose IsSuccess value and status member are proven
    /// from the initializer or the constructor argument flow. Factory names never infer meaning;
    /// an unproven construction or a polarity that contradicts the proven status emits no fact.
    /// </summary>
    public void AddFactoryCall(
        MethodId methodId,
        IInvocationOperation call,
        OperationId operationId,
        Dictionary<IOperation, OperationId> operationById,
        ImmutableArray<EvidenceRef> evidence,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
    {
        if (!TryAdmitFactory(call, operationById, models, out var kind, out var isSuccess, out var argumentOperation, out var resultType))
        {
            return;
        }

        _factories.Add(new FactoryDraft(
            methodId,
            operationId,
            resultType,
            kind,
            isSuccess,
            argumentOperation,
            evidence));
    }

    /// <summary>
    /// Records one admitted result decision when the branch condition is the exact IsSuccess member of
    /// a result-shaped type. The success and failure paths are resolved by walking the control-flow
    /// successors from each conditional branch to exact ControllerBase outcome helpers.
    /// </summary>
    public void AddDecision(
        StableProjectId project,
        MethodId methodId,
        ControlFlowGraph cfg,
        BasicBlock block,
        Dictionary<IOperation, OperationId> operationById,
        ImmutableArray<EvidenceRef> evidence,
        CancellationToken cancellationToken)
    {
        if (block.ConditionalSuccessor is null
            || block.BranchValue is null
            || block.BranchValue.Type is null
            || block.BranchValue.Type.SpecialType != SpecialType.System_Boolean)
        {
            return;
        }

        var (condition, isSuccessNegated) = ResolveIsSuccessCondition(block.BranchValue);
        if (condition is null
            || !string.Equals(condition.Property.Name, "IsSuccess", StringComparison.Ordinal)
            || !operationById.TryGetValue(block.BranchValue, out var decisionOperation)
            || !operationById.TryGetValue(condition, out var propertyOperation)
            || !IsResultShapedType(condition.Property.ContainingType))
        {
            return;
        }

        if (!operationById.TryGetValue(condition.Instance ?? condition, out var resultOperation))
        {
            // The operand that produces the result object must have a stable operation anchor; without
            // it the decision cannot be joined downstream and fails closed.
            return;
        }

        var resultLocalName = (condition.Instance ?? condition) switch
        {
            ILocalReferenceOperation localReference => localReference.Local.Name,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter.Name,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(resultLocalName))
        {
            // The decision must test a named local or parameter result; anonymous operands cannot be
            // joined through the local value graph and fail closed.
            return;
        }

        var blocksByOrdinal = cfg.Blocks.ToDictionary(b => b.Ordinal);
        var truePath = block.ConditionalSuccessor.Destination is { } trueDestination
            ? ResolvePathOutcomes(project, trueDestination, blocksByOrdinal, operationById, cancellationToken)
            : [];
        var falsePath = block.FallThroughSuccessor?.Destination is { } falseDestination
            ? ResolvePathOutcomes(project, falseDestination, blocksByOrdinal, operationById, cancellationToken)
            : [];

        var (successPath, failurePath) = isSuccessNegated
            ? (falsePath, truePath)
            : (truePath, falsePath);
        _decisions.Add(new DecisionDraft(
            methodId,
            decisionOperation,
            propertyOperation,
            resultOperation,
            resultLocalName,
            isSuccessNegated,
            successPath,
            failurePath,
            evidence));
    }

    public StructuralResultFactSet Build(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint, nameof(programIndexFingerprint));

        var factories = _factories
            .DistinctBy(factory => factory.Operation.Value)
            .OrderBy(factory => factory.Method.Value, StringComparer.Ordinal)
            .ThenBy(factory => factory.Operation.Value, StringComparer.Ordinal)
            .Select(draft => ProjectFactory(profile.Id, draft))
            .ToImmutableArray();
        var decisions = _decisions
            .DistinctBy(decision => decision.DecisionOperation.Value)
            .OrderBy(decision => decision.Method.Value, StringComparer.Ordinal)
            .ThenBy(decision => decision.DecisionOperation.Value, StringComparer.Ordinal)
            .Select(draft => ProjectDecision(profile.Id, draft))
            .ToImmutableArray();
        var debugProjection = BuildDebugProjection(
            profile,
            programIndexFingerprint,
            factories,
            decisions,
            diagnostics.Length);
        return new StructuralResultFactSet(
            1,
            ProducerVersion,
            profile,
            programIndexFingerprint,
            factories,
            decisions,
            diagnostics,
            debugProjection);
    }

    private static bool TryAdmitFactory(
        IInvocationOperation call,
        Dictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        out StructuralResultFactoryKind kind,
        out bool isSuccess,
        out OperationId? argumentOperation,
        out string resultType)
    {
        kind = StructuralResultFactoryKind.Unknown;
        isSuccess = false;
        argumentOperation = null;
        resultType = string.Empty;

        var target = call.TargetMethod;
        if (target is null
            || !target.IsStatic
            || target.ContainingType is null
            || !SymbolEqualityComparer.Default.Equals(target.ReturnType, target.ContainingType)
            || !IsResultShapedType(target.ContainingType)
            || !TryResolveFactoryProof(target, models, out var provenIsSuccess, out var provenKind))
        {
            return false;
        }

        // A proven status that contradicts the proven IsSuccess polarity is a lookalike whose name
        // must never drive meaning; it fails closed.
        if ((provenKind == StructuralResultFactoryKind.Success) != provenIsSuccess)
        {
            return false;
        }

        argumentOperation = ResolveArgumentOperation(call, operationById);
        kind = provenKind;
        isSuccess = provenIsSuccess;
        resultType = target.ContainingType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
        return true;
    }

    /// <summary>
    /// Proves a factory's polarity and status kind from its returned object construction. The factory
    /// method must return an object creation whose IsSuccess value is a constant bool (from the
    /// initializer or the constructor argument that flows into the IsSuccess property) and whose
    /// status argument is a constant enum member. Any unproven piece fails closed.
    /// </summary>
    private static bool TryResolveFactoryProof(
        IMethodSymbol factory,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        out bool isSuccess,
        out StructuralResultFactoryKind kind)
    {
        isSuccess = false;
        kind = StructuralResultFactoryKind.Unknown;
        // The factory may be referenced through a constructed generic type; the original definition
        // owns the declaring syntax and binds to the same compiler semantic model.
        var factoryDefinition = factory.OriginalDefinition ?? factory;
        var factorySyntax = factoryDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var factoryTree = factorySyntax?.SyntaxTree;
        if (factorySyntax is null
            || factoryTree is null
            || !models.TryGetValue(factoryTree, out var factoryModel)
            || factoryModel.GetOperation(factorySyntax) is not IMethodBodyOperation factoryBody)
        {
            return false;
        }

        IOperation? factoryBodyRoot = factoryBody.BlockBody ?? factoryBody.ExpressionBody;
        if (FindReturnedObjectCreation(factoryBodyRoot ?? factoryBody) is not { } creation)
        {
            return false;
        }

        var initializerPolarity = ResolveInitializerPolarity(creation);
        var (isSuccessOrdinal, statusOrdinal) = ResolveConstructorPropertyOrdinals(creation.Constructor, models);
        var provenPolarity = initializerPolarity
            ?? (isSuccessOrdinal is { } successOrdinal ? ResolveConstantBoolArgument(creation, successOrdinal) : null);
        var provenStatusName = statusOrdinal is { } statusOrdinalValue
            ? ResolveConstantEnumMemberName(creation, statusOrdinalValue)
            : null;
        if (provenPolarity is null || provenStatusName is null)
        {
            return false;
        }

        var provenKind = provenStatusName switch
        {
            "Success" => StructuralResultFactoryKind.Success,
            "NotFound" => StructuralResultFactoryKind.NotFound,
            "Conflict" => StructuralResultFactoryKind.Conflict,
            "ValidationError" => StructuralResultFactoryKind.ValidationError,
            _ => (StructuralResultFactoryKind?)null,
        };
        if (provenKind is null)
        {
            return false;
        }

        isSuccess = provenPolarity.Value;
        kind = provenKind.Value;
        return true;
    }

    private static IObjectCreationOperation? FindReturnedObjectCreation(IOperation? body)
    {
        if (body is null)
        {
            return null;
        }

        IObjectCreationOperation? expressionBodyCreation = null;
        foreach (var operation in EnumerateOperations(body))
        {
            if (operation is IReturnOperation { ReturnedValue: { } returned })
            {
                var value = UnwrapImplicitConversions(returned);
                if (value is IObjectCreationOperation creation)
                {
                    return creation;
                }

                // An explicit return that is not an object construction proves the factory does not
                // construct its result; fail closed.
                return null;
            }

            if (operation is IExpressionStatementOperation { Operation: { } statementValue }
                && UnwrapImplicitConversions(statementValue) is IObjectCreationOperation expressionCreation)
            {
                expressionBodyCreation = expressionCreation;
            }
        }

        return expressionBodyCreation;
    }

    private static bool? ResolveInitializerPolarity(IObjectCreationOperation creation)
    {
        if (creation.Initializer is null)
        {
            return null;
        }

        foreach (var operation in EnumerateOperations(creation.Initializer))
        {
            if (operation is IAssignmentOperation { Target: IPropertyReferenceOperation property, Value: { } value }
                && string.Equals(property.Property.Name, "IsSuccess", StringComparison.Ordinal)
                && value.ConstantValue is { HasValue: true, Value: bool polarity })
            {
                return polarity;
            }
        }

        return null;
    }

    private static (int? IsSuccessOrdinal, int? StatusOrdinal) ResolveConstructorPropertyOrdinals(
        IMethodSymbol? constructor,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
    {
        if (constructor is null)
        {
            return (null, null);
        }

        // The constructor may be referenced through a constructed generic type; the original
        // definition owns the declaring syntax and binds to the same compiler semantic model.
        var constructorDefinition = constructor.OriginalDefinition ?? constructor;
        var syntax = constructorDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        if (syntax is null
            || syntax.SyntaxTree is null
            || !models.TryGetValue(syntax.SyntaxTree, out var model)
            || model.GetOperation(syntax) is not IOperation bodyOperation)
        {
            return (null, null);
        }

        // The semantic model reports constructors as IConstructorBodyOperation rather than
        // IMethodBodyOperation; both expose the same block/expression body shape.
        var bodyBlock = bodyOperation switch
        {
            IMethodBodyOperation methodBody => methodBody.BlockBody ?? methodBody.ExpressionBody,
            IConstructorBodyOperation constructorBody => constructorBody.BlockBody ?? constructorBody.ExpressionBody,
            _ => null,
        };
        if (bodyBlock is null)
        {
            return (null, null);
        }

        int? isSuccessOrdinal = null;
        int? statusOrdinal = null;
        foreach (var operation in EnumerateOperations(bodyBlock))
        {
            if (operation is IAssignmentOperation { Target: IPropertyReferenceOperation property, Value: IParameterReferenceOperation parameter })
            {
                if (string.Equals(property.Property.Name, "IsSuccess", StringComparison.Ordinal))
                {
                    isSuccessOrdinal = parameter.Parameter.Ordinal;
                }

                if (string.Equals(property.Property.Name, "Status", StringComparison.Ordinal))
                {
                    statusOrdinal = parameter.Parameter.Ordinal;
                }
            }
        }

        return (isSuccessOrdinal, statusOrdinal);
    }

    private static bool? ResolveConstantBoolArgument(IObjectCreationOperation creation, int parameterOrdinal)
    {
        foreach (var argument in creation.Arguments)
        {
            if (argument.Parameter is { Ordinal: var ordinal } && ordinal == parameterOrdinal
                && UnwrapImplicitConversions(argument.Value) is { ConstantValue: { HasValue: true, Value: bool value } })
            {
                return value;
            }
        }

        return null;
    }

    private static string? ResolveConstantEnumMemberName(IObjectCreationOperation creation, int parameterOrdinal)
    {
        foreach (var argument in creation.Arguments)
        {
            if (argument.Parameter is { Ordinal: var ordinal } && ordinal == parameterOrdinal
                && UnwrapImplicitConversions(argument.Value) is IFieldReferenceOperation field)
            {
                return field.Field.Name;
            }
        }

        return null;
    }

    private static IEnumerable<IOperation> EnumerateOperations(IOperation root)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.TryPop(out var operation))
        {
            yield return operation;
            foreach (var child in operation.ChildOperations)
            {
                pending.Push(child);
            }
        }
    }

    private static OperationId? ResolveArgumentOperation(
        IInvocationOperation call,
        Dictionary<IOperation, OperationId> operationById)
    {
        foreach (var argument in call.Arguments)
        {
            if (argument.Parameter is null || argument.Parameter.Ordinal != 0)
            {
                continue;
            }

            var value = UnwrapImplicitConversions(argument.Value);
            if (value is null || value.IsImplicit || value.Syntax is null)
            {
                continue;
            }

            return operationById.TryGetValue(value, out var id) ? id : null;
        }

        return null;
    }

    /// <summary>
    /// Requires the result-shaped type contract: an instance boolean IsSuccess property and at least
    /// one static self-returning factory with a closed factory name. This closes the lookalike door so
    /// unrelated classes that happen to expose IsSuccess never project structural meaning.
    /// </summary>
    private static bool IsResultShapedType(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        var hasIsSuccess = type.GetMembers("IsSuccess").Any(member =>
            member is IPropertySymbol property
            && !property.IsStatic
            && property.Type.SpecialType == SpecialType.System_Boolean);
        if (!hasIsSuccess)
        {
            return false;
        }

        return type.GetMembers().Any(member =>
            member is IMethodSymbol method
            && method.IsStatic
            && method.Name is "Success" or "NotFound" or "Conflict" or "ValidationError"
            && SymbolEqualityComparer.Default.Equals(method.ReturnType, method.ContainingType));
    }

    private static (IPropertyReferenceOperation? Condition, bool IsSuccessNegated) ResolveIsSuccessCondition(IOperation branchValue)
    {
        var current = branchValue;
        while (current is IConversionOperation { IsImplicit: true } conversion)
        {
            current = conversion.Operand;
        }

        if (current is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, Operand: { } operand })
        {
            var inner = UnwrapImplicitConversions(operand);
            return inner is IPropertyReferenceOperation property
                ? (property, true)
                : (null, false);
        }

        if (current is IPropertyReferenceOperation direct)
        {
            return (direct, false);
        }

        return (null, false);
    }

    private ImmutableArray<StructuralOutcomePath> ResolvePathOutcomes(
        StableProjectId project,
        BasicBlock start,
        Dictionary<int, BasicBlock> blocksByOrdinal,
        Dictionary<IOperation, OperationId> operationById,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<StructuralOutcomePath>();
        var visited = new HashSet<int>();
        var pending = new Queue<BasicBlock>();
        pending.Enqueue(start);
        var boundedSteps = 0;
        while (pending.Count > 0 && boundedSteps++ < 64)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = pending.Dequeue();
            if (!visited.Add(block.Ordinal))
            {
                continue;
            }

            foreach (var operation in EnumeratePathOperations(block))
            {
                if (operation is not IInvocationOperation call
                    || call.TargetMethod is null
                    || call.TargetMethod.ContainingType is null
                    || !_controllerBaseByProject.TryGetValue(project, out var controllerBase)
                    || controllerBase is null
                    || !SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType.OriginalDefinition, controllerBase)
                    || !OutcomeHelperNames.TryGetValue(call.TargetMethod.Name, out var helperKind)
                    || !operationById.TryGetValue(call, out var outcomeOperation))
                {
                    continue;
                }

                outcomes.Add(new StructuralOutcomePath(helperKind, outcomeOperation));
            }

            if (block.FallThroughSuccessor?.Destination is { } fallThrough)
            {
                pending.Enqueue(fallThrough);
            }

            if (block.ConditionalSuccessor?.Destination is { } conditional)
            {
                pending.Enqueue(conditional);
            }
        }

        return outcomes
            .OrderBy(outcome => outcome.HelperKind)
            .ThenBy(outcome => outcome.OutcomeOperation.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static IEnumerable<IOperation> EnumeratePathOperations(BasicBlock block)
    {
        var pending = new Stack<IOperation>();
        foreach (var operation in block.Operations)
        {
            pending.Push(operation);
        }

        if (block.BranchValue is not null)
        {
            // The branch value (for example a return expression) can be an implicit conversion
            // wrapping the outcome invocation; its children must be traversed like any operation.
            pending.Push(block.BranchValue);
        }

        while (pending.TryPop(out var current))
        {
            yield return current;
            foreach (var child in current.ChildOperations)
            {
                pending.Push(child);
            }
        }
    }

    private static IOperation UnwrapImplicitConversions(IOperation? operation)
    {
        IOperation current = operation!;
        while (current is IConversionOperation { IsImplicit: true } conversion)
        {
            current = conversion.Operand;
        }

        return current;
    }

    private static StructuralResultFactoryFact ProjectFactory(CompilationProfileId profileId, FactoryDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "structural-result-factory",
            draft.Method,
            draft.Operation,
            $"{draft.Kind.ToString()}|{draft.ResultType}"));
        return new StructuralResultFactoryFact(
            id,
            draft.Method,
            draft.Operation,
            draft.ResultType,
            draft.Kind,
            draft.IsSuccess,
            draft.ArgumentOperation,
            draft.Evidence,
            CertaintyLevel.Exact);
    }

    private static StructuralResultDecisionFact ProjectDecision(CompilationProfileId profileId, DecisionDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "structural-result-decision",
            draft.Method,
            draft.DecisionOperation,
            draft.IsSuccessNegated ? "negated" : "direct"));
        return new StructuralResultDecisionFact(
            id,
            draft.Method,
            draft.DecisionOperation,
            draft.PropertyOperation,
            draft.ResultOperation,
            draft.ResultLocalName,
            draft.IsSuccessNegated,
            draft.SuccessPath,
            draft.FailurePath,
            draft.Evidence,
            CertaintyLevel.Exact);
    }

    private static string BuildDebugProjection(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<StructuralResultFactoryFact> factories,
        ImmutableArray<StructuralResultDecisionFact> decisions,
        int diagnosticCount)
    {
        var lines = new List<(string Id, string Line)>();
        foreach (var fact in factories)
        {
            lines.Add((
                fact.Id.Value,
                $"factory {fact.Id.Value} method={fact.Method.Value} kind={fact.FactoryKind.ToString()} isSuccess={fact.IsSuccess.ToString().ToLowerInvariant()} resultType={fact.ResultType} operation={fact.Operation.Value}"));
        }

        foreach (var fact in decisions)
        {
            var success = string.Join(",", fact.SuccessPath.Select(path => $"{path.HelperKind.ToString()}:{path.OutcomeOperation.Value}"));
            var failure = string.Join(",", fact.FailurePath.Select(path => $"{path.HelperKind.ToString()}:{path.OutcomeOperation.Value}"));
            lines.Add((
                fact.Id.Value,
                $"decision {fact.Id.Value} method={fact.Method.Value} decision={fact.DecisionOperation.Value} property={fact.PropertyOperation.Value} result={fact.ResultOperation.Value} negated={fact.IsSuccessNegated.ToString().ToLowerInvariant()} success={success} failure={failure}"));
        }

        var builder = new StringBuilder();
        builder.Append("structural-result:v1").Append('\n');
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

    private sealed record FactoryDraft(
        MethodId Method,
        OperationId Operation,
        string ResultType,
        StructuralResultFactoryKind Kind,
        bool IsSuccess,
        OperationId? ArgumentOperation,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record DecisionDraft(
        MethodId Method,
        OperationId DecisionOperation,
        OperationId PropertyOperation,
        OperationId ResultOperation,
        string? ResultLocalName,
        bool IsSuccessNegated,
        ImmutableArray<StructuralOutcomePath> SuccessPath,
        ImmutableArray<StructuralOutcomePath> FailurePath,
        ImmutableArray<EvidenceRef> Evidence);
}
