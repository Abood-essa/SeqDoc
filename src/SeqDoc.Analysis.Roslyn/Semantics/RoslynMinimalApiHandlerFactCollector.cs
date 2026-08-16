using System.Collections.Immutable;
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

namespace SeqDoc.Analysis.Roslyn.Semantics;

/// <summary>Projects only compiler-bound anonymous Minimal API bodies into the neutral handler companion.</summary>
internal static class RoslynMinimalApiHandlerFactCollector
{
    public static MinimalApiHandlerFactSet Collect(
        CompilationProfile profile,
        string fingerprint,
        IEnumerable<(SemanticModel Model, IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> Documents)> contexts,
        ImmutableArray<OperationDescriptor> frameworkOperations,
        CancellationToken cancellationToken)
    {
        var facts = new List<MinimalApiHandlerFact>();
        var diagnostics = new List<AnalysisDiagnostic>();
        foreach (var (model, documents) in contexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var lambda in model.SyntaxTree.GetRoot(cancellationToken).DescendantNodes().OfType<LambdaExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var anonymous = model.GetOperation(lambda, cancellationToken) as IAnonymousFunctionOperation
                    ?? GetOperation(model, lambda.Body, cancellationToken)?.Parent as IAnonymousFunctionOperation;
                if (anonymous is null)
                {
                    continue;
                }

                var enclosingInvocationSyntax = lambda.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
                var enclosingInvocation = enclosingInvocationSyntax is null
                    ? null
                    : GetOperation(model, enclosingInvocationSyntax, cancellationToken);
                var enclosingMapInvocation = enclosingInvocation as IInvocationOperation;
                var route = frameworkOperations.FirstOrDefault(operation => IsExactMapRegistration(operation)
                    && operation.CallbackTarget is
                    {
                        Kind: CallbackTargetKind.AnonymousFunction,
                        CallbackBoundaryId: not null,
                    } callback
                    && enclosingMapInvocation is not null
                    && HasSameSourceAnchor(operation, enclosingInvocationSyntax, documents));
                if (route?.CallbackTarget is not
                    {
                        Kind: CallbackTargetKind.AnonymousFunction,
                        CallbackBoundaryId: { } boundaryId,
                    } callbackTarget)
                {
                    continue;
                }
                var bodyAnchor = callbackTarget.TargetBodyOperation
                    ?? RoslynBehaviorExtractor.CreateOperationId(
                        anonymous, route.Method, "AnonymousFunction", 0, 0, 0, documents);
                var method = new MethodId($"method:v1:minimal:{bodyAnchor.Value}");
                var evidence = RoslynBehaviorExtractor.ResolveEvidence(anonymous, documents, route.Evidence);
                if (evidence.IsDefaultOrEmpty)
                {
                    evidence = RoslynBehaviorExtractor.ResolveEvidence(anonymous.Body, documents, route.Evidence);
                }
                if (evidence.IsDefaultOrEmpty
                    && documents.TryGetValue(lambda.SyntaxTree, out var documentContext))
                {
                    evidence = documentContext.Document.Evidence;
                }
                evidence = CombineEvidence(route.Evidence, evidence);
                if (evidence.IsDefaultOrEmpty)
                {
                    continue;
                }
                var decisions = EnumerateConditionals(anonymous, model, cancellationToken)
                    .OrderBy(item => item.Statement.SpanStart)
                    .ToArray();
                var parameters = ProjectParameters(anonymous.Symbol.Parameters, route, evidence);
                var operations = new List<MinimalApiHandlerOperation>();
                var predicates = new List<MinimalApiHandlerPredicate>();
                foreach (var (conditional, ordinal) in decisions.Select((item, ordinal) => (item, ordinal)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryPredicate(conditional.Condition, model, method, documents, evidence,
                        ordinal, ordinal == 0 ? null : decisions[ordinal - 1].Condition,
                        predicates, TrueArmTerminates(conditional.Statement), out var predicate))
                    {
                        predicates.Add(predicate);
                    }
                }

                var admittedOrdinals = predicates
                    .Select(predicate => predicate.TrueArm.DecisionOrdinal!.Value)
                    .ToHashSet();
                foreach (var (conditional, ordinal) in decisions.Select((item, ordinal) => (item, ordinal)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!admittedOrdinals.Contains(ordinal))
                    {
                        diagnostics.Add(UnsupportedGuardDiagnostic(
                            profile,
                            RoslynBehaviorExtractor.CreateOperationId(conditional.Condition, method, "Predicate", 0, 0, 0, documents),
                            ordinal));
                    }
                }
                var operationOrdinal = 0;
                foreach (var operation in EnumerateBodyOperations(anonymous, model, decisions, cancellationToken)
                    .OrderBy(item => item.Syntax?.SpanStart ?? int.MaxValue))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (operation is not IInvocationOperation invocation)
                    {
                        continue;
                    }

                    var target = invocation.TargetMethod;
                    if (IsExactResults(target))
                    {
                        var status = target.MetadataName == "Ok" ? 200 : ConstantStatus(invocation);
                        if (status is not null)
                        {
                            var arm = Arm(operation, decisions, operationOrdinal);
                            if (arm.DecisionOrdinal is int decisionOrdinal && !admittedOrdinals.Contains(decisionOrdinal))
                            {
                                continue;
                            }
                            operationOrdinal++;
                            var identity = target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                            operations.Add(new MinimalApiHandlerOperation(
                                Id(operation, method, documents), MinimalApiHandlerOperationKind.Outcome,
                                 identity, null, status, identity, arm, evidence, CertaintyLevel.Exact));
                        }
                        else if (target.MetadataName == "Problem")
                        {
                            diagnostics.Add(NonConstantProblemDiagnostic(profile, Id(operation, method, documents)));
                        }
                    }
                    else if (IsExactTaskDelay(target)
                        && invocation.Arguments.FirstOrDefault()?.Value.ConstantValue is { HasValue: true, Value: int milliseconds })
                    {
                        var arm = Arm(operation, decisions, operationOrdinal);
                        if (arm.DecisionOrdinal is int decisionOrdinal && !admittedOrdinals.Contains(decisionOrdinal))
                        {
                            continue;
                        }
                        operationOrdinal++;
                        operations.Add(new MinimalApiHandlerOperation(
                            Id(operation, method, documents), MinimalApiHandlerOperationKind.Delay,
                             target.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), milliseconds,
                             null, null, arm, evidence, CertaintyLevel.Exact));
                    }
                }

                var outcomes = operations
                    .Where(item => item.Kind == MinimalApiHandlerOperationKind.Outcome)
                    .OrderBy(item => item.Arm.SourceOrdinal)
                    .Select(item => new MinimalApiHandlerOutcome(
                        item.Id, item.FactoryIdentity!, item.StatusCode,
                        item.Arm, item.Evidence, item.Certainty))
                    .ToImmutableArray();
                facts.Add(new MinimalApiHandlerFact(
                    boundaryId, method, bodyAnchor, parameters, operations.ToImmutableArray(),
                    predicates.OrderBy(item => item.TrueArm.DecisionOrdinal).ToImmutableArray(), outcomes,
                    evidence, CertaintyLevel.Exact));
            }
        }

        return new MinimalApiHandlerFactSet(profile, fingerprint, facts, diagnostics, "minimal-api-handler");
    }

    private static bool IsExactMapRegistration(OperationDescriptor operation)
    {
        if (operation.TargetIdentity is not
            {
                AssemblyIdentity: "Microsoft.AspNetCore.Routing",
                AssemblyVersion: "10.0.0.0",
                ContainingMetadataType: "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions",
                MethodMetadataName: "MapGet" or "MapPost" or "MapPut" or "MapDelete",
                GenericArity: 0,
                ReturnType: "Microsoft.AspNetCore.Builder.RouteHandlerBuilder",
                Parameters: var parameters,
            })
        {
            return false;
        }

        return parameters.Length == 3
            && parameters[0] == new ParameterIdentityDescriptor(ParameterRefKind.None, "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder")
            && parameters[1] == new ParameterIdentityDescriptor(ParameterRefKind.None, "System.String")
            && parameters[2] == new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Delegate")
            && operation.ConstantArguments.Any(argument => argument.FullyQualifiedType == "System.String");
    }

    private static bool HasSameSourceAnchor(
        OperationDescriptor operation,
        SyntaxNode? syntax,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents)
    {
        return syntax is not null
            && documents.TryGetValue(syntax.SyntaxTree, out var document)
            && operation.Document == document.Document.Id
            && operation.SourceStart == syntax.SpanStart
            && operation.SourceLength == syntax.Span.Length;
    }

    private static ImmutableArray<EvidenceRef> CombineEvidence(
        ImmutableArray<EvidenceRef> first,
        ImmutableArray<EvidenceRef> second)
    {
        if (first.IsDefaultOrEmpty)
        {
            return second;
        }

        if (second.IsDefaultOrEmpty)
        {
            return first;
        }

        return first.Concat(second).Distinct().ToImmutableArray();
    }

    private static ImmutableArray<MinimalApiHandlerParameter> ProjectParameters(
        ImmutableArray<IParameterSymbol> sourceParameters, OperationDescriptor route, ImmutableArray<EvidenceRef> evidence)
    {
        var allowBody = sourceParameters.Count(parameter => IsBodyEligible(parameter.Type) && !HasCustomBinder(parameter.Type)) == 1;
        return sourceParameters.Select(parameter => ProjectParameter(parameter, route, evidence, allowBody)).ToImmutableArray();
    }

    private static MinimalApiHandlerParameter ProjectParameter(
        IParameterSymbol parameter, OperationDescriptor route, ImmutableArray<EvidenceRef> evidence, bool allowBody)
    {
        var type = parameter.Type;
        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "", StringComparison.Ordinal);
        var routeTemplate = EffectiveRoute(route);
        var placeholder = routeTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim('{', '}').Split(':', 2)[0])
            .FirstOrDefault(name => name == parameter.Name);
        var kind = placeholder is not null
            ? HttpBindingKind.Route
             : IsCancellationToken(type)
                 ? HttpBindingKind.CancellationToken
                : HasCustomBinder(type)
                    ? HttpBindingKind.Unknown
                    : IsSimple(type) ? HttpBindingKind.Query
                    : allowBody && IsBodyEligible(type) && !HasCustomBinder(type) ? HttpBindingKind.Body : HttpBindingKind.Unknown;
        return new MinimalApiHandlerParameter(parameter.Name, typeName, kind,
            placeholder is not null ? $"route placeholder '{placeholder}'" : "compiler-bound parameter", evidence, CertaintyLevel.Exact);
    }

    private static string EffectiveRoute(OperationDescriptor operation)
    {
        var route = operation.ConstantArguments.First(argument => argument.FullyQualifiedType == "System.String").Value;
        var prefixes = operation.RouteGroup?.Prefixes ?? [];
        return "/" + string.Join("/", prefixes.Concat([route])
            .SelectMany(value => value.Split('/', StringSplitOptions.RemoveEmptyEntries)));
    }

    private static bool IsSimple(ITypeSymbol type)
        => type.SpecialType is not SpecialType.None and not SpecialType.System_Object
            || type.ToDisplayString() == "string";

    private static bool HasCustomBinder(ITypeSymbol type)
        => type is INamedTypeSymbol named && named.GetMembers().OfType<IMethodSymbol>().Any(method =>
            method.IsStatic && method.MethodKind == MethodKind.Ordinary
                && method.MetadataName is "BindAsync" or "TryParse");

    private static bool IsBodyEligible(ITypeSymbol type)
        => type is INamedTypeSymbol { IsRecord: true };

    private static IEnumerable<IOperation> EnumerateBodyOperations(
        IAnonymousFunctionOperation anonymous, SemanticModel model, ConditionalInfo[] decisions,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<(int Start, int Length, string Symbol)>();
        foreach (var operation in anonymous.Body.DescendantsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seen.Add(OperationKey(operation)))
            {
                yield return operation;
            }
        }

        // Some Roslyn versions omit invocation descendants from an anonymous body tree.
        foreach (var invocationSyntax in anonymous.Syntax!.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Concat(anonymous.Body.Syntax!.DescendantNodes().OfType<InvocationExpressionSyntax>()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (model.GetOperation(invocationSyntax, cancellationToken) is IOperation operation
                && seen.Add(OperationKey(operation)))
            {
                yield return operation;
            }
        }
    }

    private static List<ConditionalInfo> EnumerateConditionals(
        IAnonymousFunctionOperation anonymous, SemanticModel model, CancellationToken cancellationToken)
    {
        var conditionals = new List<ConditionalInfo>();
        foreach (var statement in anonymous.Syntax!.DescendantNodes().OfType<IfStatementSyntax>()
            .Concat(anonymous.Body.Syntax!.DescendantNodes().OfType<IfStatementSyntax>())
            .DistinctBy(statement => statement.Span))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var condition = model.GetOperation(statement.Condition, cancellationToken)
                ?? (model.GetOperation(statement, cancellationToken) as IConditionalOperation)?.Condition
                ?? anonymous.Body.DescendantsAndSelf()
                    .FirstOrDefault(operation => operation.Syntax?.Span == statement.Condition.Span);
            if (condition is not null)
            {
                conditionals.Add(new ConditionalInfo(condition, statement));
            }
        }
        return conditionals;
    }

    private static bool TryPredicate(IOperation condition, SemanticModel model, MethodId method,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        ImmutableArray<EvidenceRef> evidence, int decisionOrdinal,
        IOperation? priorCondition,
        IReadOnlyList<MinimalApiHandlerPredicate> priorPredicates,
        bool trueArmTerminates,
        out MinimalApiHandlerPredicate result)
    {
        result = null!;
        if (Unwrap(condition) is IIsPatternOperation isPattern
            && TryPatternUpperBound(isPattern, decisionOrdinal, priorCondition, priorPredicates, out var patternValue,
                out var patternConstant, out var patternExpression))
        {
            result = new MinimalApiHandlerPredicate(
                RoslynBehaviorExtractor.CreateOperationId(isPattern, method, "IsPattern", 0, 0, 0, documents),
                patternExpression, patternConstant,
                new MinimalApiHandlerArm(decisionOrdinal, true, decisionOrdinal),
                new MinimalApiHandlerArm(decisionOrdinal, false, decisionOrdinal), evidence, CertaintyLevel.Exact,
                trueArmTerminates);
            return true;
        }

        var value = Unwrap(condition) as IBinaryOperation
            ?? condition.DescendantsAndSelf().OfType<IBinaryOperation>().FirstOrDefault();
        if (value is null || value.ConstantValue is { HasValue: true }
            || value.RightOperand.ConstantValue is not { HasValue: true, Value: int constant })
        {
            return false;
        }
        var leftName = value.LeftOperand is ILocalReferenceOperation local ? local.Local.Name
            : value.LeftOperand is IParameterReferenceOperation parameter ? parameter.Parameter.Name
            : value.LeftOperand.Syntax?.ToString();
        var left = new PredicateExpression(PredicateExpressionKind.SymbolValue, [], value.LeftOperand.Type?.ToDisplayString() ?? "System.Object", displayName: leftName);
        var right = new PredicateExpression(PredicateExpressionKind.NumericConstant, [], value.RightOperand.Type?.ToDisplayString() ?? "System.Int32", constantValue: constant.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var op = value.OperatorKind switch
        {
            BinaryOperatorKind.LessThanOrEqual => PredicateComparisonOperatorKind.LessThanOrEqual,
            BinaryOperatorKind.LessThan => PredicateComparisonOperatorKind.LessThan,
            BinaryOperatorKind.GreaterThanOrEqual => PredicateComparisonOperatorKind.GreaterThanOrEqual,
            BinaryOperatorKind.GreaterThan => PredicateComparisonOperatorKind.GreaterThan,
            _ => PredicateComparisonOperatorKind.Equal,
        };
        result = new MinimalApiHandlerPredicate(
            RoslynBehaviorExtractor.CreateOperationId(value, method, "Binary", 0, 0, 0, documents),
            new PredicateExpression(PredicateExpressionKind.Comparison, [left, right], "System.Boolean", op), constant,
            new MinimalApiHandlerArm(decisionOrdinal, true, decisionOrdinal),
            new MinimalApiHandlerArm(decisionOrdinal, false, decisionOrdinal), evidence, CertaintyLevel.Exact,
            trueArmTerminates);
        return true;
    }

    private static bool TryPatternUpperBound(
        IIsPatternOperation isPattern, int decisionOrdinal,
        IOperation? priorCondition,
        IReadOnlyList<MinimalApiHandlerPredicate> priorPredicates,
        out IOperation value, out int constant, out PredicateExpression expression)
    {
        value = null!;
        constant = default;
        expression = null!;
        if (decisionOrdinal == 0
            || isPattern.Value is not IOperation patternInput
            || !TryCompilerBoundValue(patternInput, out var inputSymbol, out var inputName)
            || priorCondition is null)
        {
            return false;
        }

        var previous = priorPredicates.LastOrDefault(predicate =>
            predicate.TrueArm.DecisionOrdinal == decisionOrdinal - 1);
        if (previous is null
            || !previous.TrueArmTerminates
            || previous.Expression.Kind != PredicateExpressionKind.Comparison
            || previous.Expression.ComparisonOperator != PredicateComparisonOperatorKind.LessThanOrEqual
            || previous.Expression.Children[0].DisplayName != inputName)
        {
            return false;
        }
        var lowerBound = previous.Constant;

        var priorValue = Unwrap(priorCondition) as IBinaryOperation;
        if (priorValue is null
            || !TryCompilerBoundValue(priorValue.LeftOperand, out var priorSymbol, out _)
            || !SymbolEqualityComparer.Default.Equals(inputSymbol, priorSymbol))
        {
            return false;
        }

        if (isPattern.Pattern is not IBinaryPatternOperation
            {
                OperatorKind: BinaryOperatorKind.And,
                LeftPattern: IRelationalPatternOperation left,
                RightPattern: IRelationalPatternOperation right,
            })
        {
            return false;
        }

        var relationalPatterns = new[] { left, right };
        var lower = relationalPatterns.SingleOrDefault(pattern =>
            pattern.OperatorKind == BinaryOperatorKind.GreaterThan
            && pattern.Value.ConstantValue is { HasValue: true, Value: int value }
            && value == lowerBound);
        var upper = relationalPatterns.SingleOrDefault(pattern =>
            pattern.OperatorKind == BinaryOperatorKind.LessThanOrEqual
            && pattern.Value.ConstantValue is { HasValue: true, Value: int value });
        if (lower is null || upper is null
            || upper.Value.ConstantValue is not { HasValue: true, Value: int upperBound })
        {
            return false;
        }

        value = patternInput;
        constant = upperBound;
        var leftExpression = new PredicateExpression(PredicateExpressionKind.SymbolValue, [],
            patternInput.Type?.ToDisplayString() ?? "System.Object", displayName: inputName);
        var rightExpression = new PredicateExpression(PredicateExpressionKind.NumericConstant, [],
            upper.Value.Type?.ToDisplayString() ?? "System.Int32",
            constantValue: upperBound.ToString(System.Globalization.CultureInfo.InvariantCulture));
        expression = new PredicateExpression(PredicateExpressionKind.Comparison,
            [leftExpression, rightExpression], "System.Boolean",
            PredicateComparisonOperatorKind.LessThanOrEqual);
        return true;
    }

    private static bool TryCompilerBoundValue(IOperation operation, out ISymbol symbol, out string name)
    {
        operation = Unwrap(operation);
        switch (operation)
        {
            case ILocalReferenceOperation local:
                symbol = local.Local;
                name = local.Local.Name;
                return true;
            case IParameterReferenceOperation parameter:
                symbol = parameter.Parameter;
                name = parameter.Parameter.Name;
                return true;
            default:
                symbol = null!;
                name = string.Empty;
                return false;
        }
    }

    private static bool IsExactResults(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        if (!definition.IsStatic || definition.MethodKind != MethodKind.Ordinary || definition.Arity is not (0 or 1)
            || definition.Parameters.Any(parameter => parameter.RefKind != RefKind.None)
            || !IsExactType(definition.ReturnType, "IResult", "Microsoft.AspNetCore.Http")
            || definition.ContainingAssembly.Identity.Name != "Microsoft.AspNetCore.Http.Results"
            || definition.ContainingAssembly.Identity.Version != new Version(10, 0, 0, 0)
            || RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) != "Microsoft.AspNetCore.Http.Results")
        {
            return false;
        }

        return definition.MetadataName switch
        {
            "Ok" => definition.Arity == 0 && definition.Parameters.Length == 1
                && IsExactType(definition.Parameters[0].Type, "Object", "System")
                || definition.Arity == 1 && definition.Parameters.Length == 1
                && definition.Parameters[0].Type is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method, Ordinal: 0 },
            "Problem" => definition.Arity == 0 && definition.Parameters.Length == 6
                && IsExactType(definition.Parameters[0].Type, "String", "System")
                && IsExactType(definition.Parameters[1].Type, "String", "System")
                && IsNullableInt(definition.Parameters[2].Type)
                && IsExactType(definition.Parameters[3].Type, "String", "System")
                && IsExactType(definition.Parameters[4].Type, "String", "System")
                && IsEnumerableOfKeyValuePair(definition.Parameters[5].Type),
            _ => false,
        };
    }

    private static bool IsExactTaskDelay(IMethodSymbol target)
    {
        var method = target.OriginalDefinition ?? target;
        return IsCoreContractAssembly(method.ContainingAssembly)
            && method.MetadataName == "Delay" && method.Arity == 0
            && method.Parameters.Length is 1 or 2
            && method.Parameters[0].Type.SpecialType == SpecialType.System_Int32
            && (method.Parameters.Length == 1 || IsCancellationToken(method.Parameters[1].Type))
            && IsTask(method.ReturnType);
    }

    private static int? ConstantStatus(IInvocationOperation invocation)
    {
        var argument = invocation.Arguments.SingleOrDefault(argument =>
            argument.Parameter is { Ordinal: 2, Name: "statusCode" });
        if (argument is null || argument.Value is not IConversionOperation conversion
            || !conversion.IsImplicit || conversion.Type is not { } conversionType
            || !IsNullableInt(conversionType))
        {
            return null;
        }

        var operand = Unwrap(conversion.Operand).ConstantValue;
        return operand is { HasValue: true, Value: int status } ? status : null;
    }

    private static bool IsCancellationToken(ITypeSymbol type)
    {
        var named = type as INamedTypeSymbol ?? type.OriginalDefinition as INamedTypeSymbol;
        return named is not null
            && named.TypeKind == TypeKind.Struct
            && named.MetadataName == "CancellationToken"
            && named.ContainingNamespace.ToDisplayString() == "System.Threading"
            && IsCoreContractAssembly(named.ContainingAssembly);
    }

    private static bool IsExactType(ITypeSymbol type, string metadataName, string namespaceName)
    {
        var named = type as INamedTypeSymbol ?? type.OriginalDefinition as INamedTypeSymbol;
        return named is not null
            && named.MetadataName == metadataName
            && named.ContainingNamespace.ToDisplayString() == namespaceName;
    }

    private static bool IsNullableInt(ITypeSymbol type)
        => type is INamedTypeSymbol named
            && named.MetadataName == "Nullable`1"
            && named.ContainingNamespace.ToDisplayString() == "System"
            && named.TypeArguments.Length == 1
            && IsExactType(named.TypeArguments[0], "Int32", "System")
            && IsCoreContractAssembly(named.ContainingAssembly);

    private static bool IsEnumerableOfKeyValuePair(ITypeSymbol type)
        => type is INamedTypeSymbol enumerable
            && enumerable.MetadataName == "IEnumerable`1"
            && enumerable.ContainingNamespace.ToDisplayString() == "System.Collections.Generic"
            && enumerable.TypeArguments.Length == 1
            && enumerable.TypeArguments[0] is INamedTypeSymbol pair
            && pair.MetadataName == "KeyValuePair`2"
            && pair.ContainingNamespace.ToDisplayString() == "System.Collections.Generic"
            && pair.TypeArguments.Length == 2
            && IsExactType(pair.TypeArguments[0], "String", "System")
            && IsExactType(pair.TypeArguments[1], "Object", "System");

    private static bool IsTask(ITypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        return definition is INamedTypeSymbol named
            && named.MetadataName == "Task"
            && named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks"
            && named.TypeArguments.Length == 0
            && IsCoreContractAssembly(named.ContainingAssembly);
    }

    private static bool IsCoreContractAssembly(IAssemblySymbol assembly)
        => assembly.Identity.Version == new Version(10, 0, 0, 0)
            && assembly.Identity.Name is "System.Private.CoreLib" or "System.Runtime" or "System.Runtime.Extensions";

    private static (int Start, int Length, string Symbol) OperationKey(IOperation operation)
        => (operation.Syntax?.SpanStart ?? -1, operation.Syntax?.Span.Length ?? 0,
            operation is IInvocationOperation invocation
                ? invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : operation.Kind.ToString());

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation conversion && conversion.IsImplicit)
        {
            operation = conversion.Operand;
        }
        return operation;
    }

    private static MinimalApiHandlerArm Arm(IOperation operation, ConditionalInfo[] decisions, int sourceOrdinal)
    {
        var syntax = operation.Syntax;
        for (var index = decisions.Length - 1; index >= 0; index--)
        {
            var statement = decisions[index].Statement;
            if (statement.Statement.Span.Contains(syntax.Span))
            {
                return new MinimalApiHandlerArm(sourceOrdinal, true, index);
            }
            if (statement.Else?.Statement.Span.Contains(syntax.Span) == true)
            {
                return new MinimalApiHandlerArm(sourceOrdinal, false, index);
            }
        }
        return decisions.Length == 0
            ? new MinimalApiHandlerArm(sourceOrdinal, true)
            : new MinimalApiHandlerArm(sourceOrdinal, false, decisions.Length - 1);
    }

    private static bool TrueArmTerminates(IfStatementSyntax statement)
    {
        var trueStatement = statement.Statement;
        if (trueStatement is ReturnStatementSyntax or ThrowStatementSyntax)
        {
            return true;
        }

        if (trueStatement is not BlockSyntax block
            || block.Statements.LastOrDefault() is not (ReturnStatementSyntax or ThrowStatementSyntax))
        {
            return false;
        }

        return !block.DescendantNodes().Any(node => node is
            LabeledStatementSyntax or GotoStatementSyntax or YieldStatementSyntax or TryStatementSyntax
            or ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax
            or UsingStatementSyntax or LocalFunctionStatementSyntax or LockStatementSyntax);
    }

    private static IOperation? GetOperation(SemanticModel model, SyntaxNode syntax, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return model.GetOperation(syntax, cancellationToken);
    }

    private static OperationId Id(IOperation operation, MethodId method,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents)
        => RoslynBehaviorExtractor.CreateOperationId(operation, method, operation.Kind.ToString(), 0, 0, 0, documents);

    private static AnalysisDiagnostic NonConstantProblemDiagnostic(CompilationProfile profile, OperationId operation)
    {
        const string code = "MA002";
        var detail = operation.Value + "|nonconstant-problem-status";
        return new AnalysisDiagnostic(
            StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(code, AnalysisStage.FrameworkModel, profile.Id, detail, 0)),
            code, SeqDoc.Core.Diagnostics.DiagnosticSeverity.Warning, AnalysisStage.FrameworkModel,
            "A recognized ASP.NET Core Problem result has a nonconstant status.",
            new DiagnosticLocation("minimal API Problem invocation", profile.Id),
            "The Problem statusCode argument is not compiler-constant.",
            "No exact HTTP outcome was emitted.",
            "Use a compiler-proven constant status code.", CertaintyLevel.Exact, internalDetail: detail);
    }

    private static AnalysisDiagnostic UnsupportedGuardDiagnostic(
        CompilationProfile profile, OperationId condition, int decisionOrdinal)
    {
        const string code = "MA003";
        var detail = $"{condition.Value}|decision:{decisionOrdinal}";
        return new AnalysisDiagnostic(
            StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(code, AnalysisStage.FrameworkModel, profile.Id, detail, 0)),
            code, SeqDoc.Core.Diagnostics.DiagnosticSeverity.Warning, AnalysisStage.FrameworkModel,
            "A Minimal API handler guard could not be compiler-admitted.",
            new DiagnosticLocation("minimal API handler condition", profile.Id),
            $"Decision ordinal {decisionOrdinal} does not satisfy the supported predicate contract.",
            "Operations controlled by that guard were withheld.",
            "Use a directly supported compiler comparison or pattern.", CertaintyLevel.Exact, internalDetail: detail);
    }

    private sealed record ConditionalInfo(IOperation Condition, IfStatementSyntax Statement);
}
