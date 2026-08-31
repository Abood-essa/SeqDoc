using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SeqDoc.Analysis.Roslyn.Frameworks;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;

namespace SeqDoc.Analysis.Roslyn.Semantics;

internal static class RoslynEntityFramework6FactCollector
{
    private const string DbSet = "System.Data.Entity.DbSet`1";

    public static OperationDescriptor Enrich(IInvocationOperation call, OperationDescriptor descriptor, Dictionary<IOperation, OperationId> operationIds, IReadOnlyDictionary<SyntaxTree, SemanticModel> models, IReadOnlyDictionary<ILocalSymbol, IOperation>? localInitializers = null, IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext>? documents = null, ControlFlowGraph? cfg = null)
    {
        var receiver = Unwrap(call.Instance ?? (call.TargetMethod.IsExtensionMethod && call.Arguments.Length > 0 ? call.Arguments[0].Value : null));
        var localSymbol = FindLocalSymbol(receiver, models);
        var property = FindDbSetProperty(receiver) ?? FindDbSetProperty(call);
        var sourceProperty = property is null ? FindSourceDbSetProperty(call, models) : null;
        if (sourceProperty is not null
            && IsExactEf6DbSet(receiver?.Type, out _)
            && call.TargetMethod.OriginalDefinition.MetadataName == "Add")
        {
            var enriched = descriptor with
            {
                QueryChain = new FrameworkQueryChainDescriptor(
                    sourceProperty.Value.SetType,
                    sourceProperty.Value.ContextType,
                    sourceProperty.Value.MemberName,
                    sourceProperty.Value.EntityType,
                    BuildWhereSteps(receiver, operationIds, descriptor.Id) ?? [])
            };
            return Normalize(enriched, call);
        }
        if (localSymbol is { } local
            && call.TargetMethod.OriginalDefinition.MetadataName == "Count"
            && call.TargetMethod.OriginalDefinition.ContainingAssembly?.Identity.Name == "System.Linq.Queryable"
            && call.TargetMethod.OriginalDefinition.ContainingAssembly.Identity.Version?.ToString() == "9.0.0.0"
            && IsExactCount(call.TargetMethod.OriginalDefinition)
            && localInitializers is not null
             && localInitializers.TryGetValue(localSymbol, out var initializer)
             && SameMethod(receiver!, initializer, call, models)
             && IsControlFlowSafe(initializer, call, cfg)
             && FindDbSetProperty(initializer) is { } localProperty
             && IsExactEf6DbSet(localProperty.Property.Type, out var localEntity)
            && IsDerivedContext(localProperty.Property.ContainingType)
            && initializer is IInvocationOperation)
        {
            var steps = BuildWhereSteps(initializer, operationIds, descriptor.Id);
            if (steps is not null && steps.Value.Length > 0)
            {
                if (initializer.Type is not INamedTypeSymbol queryable
                    || !queryable.IsGenericType || queryable.TypeArguments.Length != 1
                     || queryable.TypeArguments[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) != localEntity)
                {
                    return descriptor;
                }
                var enriched = descriptor with
                {
                    QueryChain = new FrameworkQueryChainDescriptor(
                         $"System.Data.Entity.DbSet<{localEntity}>",
                        localProperty.Property.ContainingType?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) ?? string.Empty,
                        localProperty.Property.Name,
                         localEntity,
                        steps.Value),
                };
                var withInitializerEvidence = AddInitializerEvidence(enriched, initializer, documents, models);
                return Normalize(withInitializerEvidence, call);
            }
        }
        if (property is not null && property.Property.Type is INamedTypeSymbol set
            && set.OriginalDefinition.ContainingAssembly?.Identity.Name == "EntityFramework"
            && HasToken(set.OriginalDefinition.ContainingAssembly, "b77a5c561934e089")
            && set.OriginalDefinition.ContainingAssembly.Identity.Version?.ToString() == "6.0.0.0"
            && RoslynProgramIndexExtractor.GetMetadataName(set.OriginalDefinition) == DbSet && set.TypeArguments.Length == 1)
        {
            var entity = set.TypeArguments[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
            var context = property.Property.ContainingType?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) ?? "";
            var discoveredSteps = descriptor.QueryChain?.Steps ?? BuildWhereSteps(receiver, operationIds, descriptor.Id);
            if (!IsDerivedContext(property.Property.ContainingType) || (receiver is IInvocationOperation && discoveredSteps is null))
            {
                return descriptor;
            }
            var steps = discoveredSteps ?? [];
            var enriched = descriptor with
            {
                QueryChain = new FrameworkQueryChainDescriptor(
                    set.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), context, property.Property.Name, entity, steps)
            };
            return Normalize(enriched, call);
        }
        if (call.TargetMethod.OriginalDefinition.ContainingAssembly?.Identity.Name == "EntityFramework"
            && HasToken(call.TargetMethod.OriginalDefinition.ContainingAssembly, "b77a5c561934e089")
            && call.TargetMethod.OriginalDefinition.ContainingAssembly.Identity.Version?.ToString() == "6.0.0.0"
            && RoslynProgramIndexExtractor.GetMetadataName(call.TargetMethod.OriginalDefinition.ContainingType) == DbSet
            && call.TargetMethod.OriginalDefinition.MetadataName == "Add"
            && call.TargetMethod.OriginalDefinition.Arity == 0
            && receiver?.Type is INamedTypeSymbol receiverSet
            && property is not null
            && IsDerivedContext(property.Property.ContainingType)
            && receiverSet.OriginalDefinition.ContainingAssembly?.Identity.Name == "EntityFramework"
            && HasToken(receiverSet.OriginalDefinition.ContainingAssembly, "b77a5c561934e089")
            && receiverSet.OriginalDefinition.ContainingAssembly.Identity.Version?.ToString() == "6.0.0.0"
            && RoslynProgramIndexExtractor.GetMetadataName(receiverSet.OriginalDefinition) == DbSet)
        {
            var entity = receiverSet.TypeArguments[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
            var enriched = descriptor with
            {
                QueryChain = new FrameworkQueryChainDescriptor(
                    receiverSet.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
                    receiver?.Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) ?? "EF6 context",
                    "",
                    entity,
                    [])
            };
            return Normalize(enriched, call);
        }
        if (call.TargetMethod.OriginalDefinition.ContainingAssembly?.Identity.Name == "EntityFramework"
            && HasToken(call.TargetMethod.OriginalDefinition.ContainingAssembly, "b77a5c561934e089")
            && call.TargetMethod.OriginalDefinition.ContainingAssembly.Identity.Version?.ToString() == "6.0.0.0"
            && RoslynProgramIndexExtractor.GetMetadataName(call.TargetMethod.OriginalDefinition.ContainingType) == "System.Data.Entity.DbContext"
            && IsDerivedContext(receiver?.Type))
        {
            var receiverType = receiver!.Type!.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
            return Normalize(descriptor with { QueryChain = new FrameworkQueryChainDescriptor(receiverType, receiverType, "", "", []) }, call);
        }
        return descriptor;
    }

    private static ImmutableArray<FrameworkChainStepDescriptor>? BuildWhereSteps(IOperation? receiver, Dictionary<IOperation, OperationId> operationIds, OperationId terminal)
    {
        var steps = new List<FrameworkChainStepDescriptor>();
        var current = receiver;
        while (current is IInvocationOperation invocation)
        {
            var target = invocation.TargetMethod.OriginalDefinition;
            if (!IsExactWhere(target))
            {
                return null;
            }

            var operation = operationIds.GetValueOrDefault(current, new OperationId($"{terminal.Value}:where:{current.Syntax.SpanStart}"));
            // The model requires the closed compiler-bound Where<T> identity.  The original
            // definition is used only for admission; the constructed invocation preserves the
            // proven entity type in parameter and return identities.
            steps.Add(new FrameworkChainStepDescriptor(operation, Identity(invocation.TargetMethod), null));
            current = Unwrap(invocation.Instance ?? (target.IsExtensionMethod && invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null));
        }

        steps.Reverse();
        return steps.ToImmutableArray();
    }

    private static OperationDescriptor Normalize(OperationDescriptor descriptor, IInvocationOperation call)
    {
        var method = call.TargetMethod;
        var original = method.OriginalDefinition;
        var token = original.MetadataName switch
        {
            "FirstOrDefault" => "ef6-first-or-default",
            "Count" => "ef6-count",
            "Add" => "ef6-add",
            "SaveChanges" => "ef6-save-changes",
            _ => null,
        };
        if (token is null)
        {
            return descriptor;
        }

        var exactTarget = token is "ef6-first-or-default" or "ef6-count"
            ? original.ContainingAssembly?.Identity.Name == "System.Linq.Queryable"
                && original.ContainingAssembly.Identity.Version?.ToString() == "9.0.0.0"
                && RoslynProgramIndexExtractor.GetMetadataName(original.ContainingType) == "System.Linq.Queryable"
                && original.Arity == 1
            : original.ContainingAssembly?.Identity.Name == "EntityFramework"
                && original.ContainingAssembly.Identity.Version?.ToString() == "6.0.0.0"
                && (token == "ef6-add"
                    ? RoslynProgramIndexExtractor.GetMetadataName(original.ContainingType) == DbSet && original.Arity == 0
                    : RoslynProgramIndexExtractor.GetMetadataName(original.ContainingType) == "System.Data.Entity.DbContext" && original.Arity == 0);
        if (!exactTarget)
        {
            return descriptor;
        }

        if (original.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
        {
            return descriptor;
        }
        // OperationDescriptor.Id is the exact Method Flow anchor assigned by the accepted
        // Roslyn traversal. Framework-specific re-identification would sever topology joins.
        return descriptor;
    }

    private static bool IsExactWhere(IMethodSymbol method)
        => method.ContainingAssembly?.Identity.Name == "System.Linq.Queryable"
            && HasToken(method.ContainingAssembly, "b03f5f7f11d50a3a")
            && method.ContainingAssembly.Identity.Version?.ToString() == "9.0.0.0"
            && RoslynProgramIndexExtractor.GetMetadataName(method.ContainingType) == "System.Linq.Queryable"
            && method.MetadataName == "Where" && method.Arity == 1 && method.Parameters.Length == 2
            && method.Parameters.All(parameter => parameter.RefKind == RefKind.None)
            && method.ReturnType is INamedTypeSymbol result && IsQueryableOf(result, method.TypeParameters[0])
            && method.Parameters[0].Type is INamedTypeSymbol source && IsQueryableOf(source, method.TypeParameters[0])
            && method.Parameters[1].Type is INamedTypeSymbol expression
            && RoslynProgramIndexExtractor.GetMetadataName(expression.OriginalDefinition) == "System.Linq.Expressions.Expression`1"
            && expression.TypeArguments[0] is INamedTypeSymbol function
            && RoslynProgramIndexExtractor.GetMetadataName(function.OriginalDefinition) == "System.Func`2"
            && SymbolEqualityComparer.Default.Equals(function.TypeArguments[0], method.TypeParameters[0])
            && function.TypeArguments[1].SpecialType == SpecialType.System_Boolean;

    private static bool IsExactCount(IMethodSymbol method)
        => method.ContainingAssembly?.Identity.Name == "System.Linq.Queryable"
            && HasToken(method.ContainingAssembly, "b03f5f7f11d50a3a")
            && method.MetadataName == "Count" && method.Arity == 1 && method.Parameters.Length == 1
            && method.Parameters[0].RefKind == RefKind.None
            && method.Parameters[0].Type is INamedTypeSymbol source
            && IsQueryableOf(source, method.TypeParameters[0])
            && method.ReturnType.SpecialType == SpecialType.System_Int32;

    private static bool IsQueryableOf(INamedTypeSymbol type, ITypeParameterSymbol argument)
        => RoslynProgramIndexExtractor.GetMetadataName(type.OriginalDefinition) == "System.Linq.IQueryable`1"
            && type.TypeArguments.Length == 1
            && SymbolEqualityComparer.Default.Equals(type.TypeArguments[0], argument);

    private static bool IsExactEf6DbSet(ITypeSymbol? type, out string entity)
    {
        entity = string.Empty;
        if (type is not INamedTypeSymbol set || set.TypeArguments.Length != 1
            || RoslynProgramIndexExtractor.GetMetadataName(set.OriginalDefinition) != DbSet
            || set.OriginalDefinition.ContainingAssembly?.Identity.Name != "EntityFramework"
            || !HasToken(set.OriginalDefinition.ContainingAssembly, "b77a5c561934e089")
            || set.OriginalDefinition.ContainingAssembly.Identity.Version?.ToString() != "6.0.0.0")
        {
            return false;
        }
        entity = set.TypeArguments[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat);
        return true;
    }

    private static bool SameMethod(IOperation local, IOperation initializer, IInvocationOperation call, IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
        => models.TryGetValue(local.Syntax.SyntaxTree, out var model)
            && SymbolEqualityComparer.Default.Equals(
                model.GetEnclosingSymbol(local.Syntax.SpanStart),
                model.GetEnclosingSymbol(call.Syntax.SpanStart))
             && initializer.Syntax.SyntaxTree == call.Syntax.SyntaxTree;

    private static ILocalSymbol? FindLocalSymbol(
        IOperation? receiver,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
    {
        if (receiver is ILocalReferenceOperation local)
        {
            return local.Local;
        }

        if (receiver?.Syntax is { } syntax
            && models.TryGetValue(syntax.SyntaxTree, out var model))
        {
            return model.GetSymbolInfo(syntax).Symbol as ILocalSymbol;
        }

        return null;
    }

    private static bool IsControlFlowSafe(IOperation initializer, IInvocationOperation terminal, ControlFlowGraph? cfg)
    {
        if (cfg is null || initializer.Syntax is null || terminal.Syntax is null)
        {
            return false;
        }

        // A definition inside a conditional, loop, exception region, lambda, or local function is
        // not a single unconditional definition for the terminal, even when the local map contains it.
        for (var syntax = initializer.Syntax.Parent; syntax is not null && syntax != cfg.OriginalOperation.Syntax; syntax = syntax.Parent)
        {
            if (syntax is IfStatementSyntax or SwitchStatementSyntax or SwitchSectionSyntax
                or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or ForEachStatementSyntax
                or TryStatementSyntax or CatchClauseSyntax or FinallyClauseSyntax
                or LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return false;
            }
        }

        var blocks = cfg.Blocks.ToArray();
        var source = FindBlock(blocks, initializer);
        var target = FindBlock(blocks, terminal);
        if (source is null || target is null)
        {
            return false;
        }

        if (source.Ordinal == target.Ordinal)
        {
            return initializer.Syntax.SpanStart <= terminal.Syntax.SpanStart;
        }

        var predecessors = blocks.ToDictionary(
            block => block.Ordinal,
            block => block.Predecessors.Select(branch => branch.Source?.Ordinal).Where(value => value is not null).Select(value => value!.Value).ToImmutableHashSet());
        var all = blocks.Select(block => block.Ordinal).ToImmutableHashSet();
        var dominators = blocks.ToDictionary(block => block.Ordinal, block => all);
        dominators[blocks.OrderBy(block => block.Ordinal).First().Ordinal] = [blocks.OrderBy(block => block.Ordinal).First().Ordinal];
        bool changed;
        do
        {
            changed = false;
            foreach (var block in blocks.OrderBy(block => block.Ordinal).Skip(1))
            {
                var incoming = predecessors[block.Ordinal];
                var next = incoming.Count == 0
                    ? ImmutableHashSet<int>.Empty
                    : incoming.Select(predecessor => dominators[predecessor]).Aggregate((left, right) => left.Intersect(right)).Add(block.Ordinal);
                if (!next.SetEquals(dominators[block.Ordinal]))
                {
                    dominators[block.Ordinal] = next;
                    changed = true;
                }
            }
        } while (changed);

        if (!dominators[target.Ordinal].Contains(source.Ordinal))
        {
            return false;
        }

        var reachable = new HashSet<int> { source.Ordinal };
        var pending = new Stack<int>(reachable);
        while (pending.TryPop(out var current))
        {
            var block = blocks.Single(candidate => candidate.Ordinal == current);
            foreach (var successor in new[] { block.FallThroughSuccessor?.Destination, block.ConditionalSuccessor?.Destination })
            {
                if (successor is not null && reachable.Add(successor.Ordinal))
                {
                    pending.Push(successor.Ordinal);
                }
            }
        }

        return reachable.Contains(target.Ordinal);
    }

    private static BasicBlock? FindBlock(BasicBlock[] blocks, IOperation operation)
        => blocks.FirstOrDefault(block =>
            block.Operations.Any(root => Enumerate(root).Any(candidate => ReferenceEquals(candidate, operation)))
            || (block.BranchValue is not null && Enumerate(block.BranchValue).Any(candidate => ReferenceEquals(candidate, operation))))
            ?? blocks.Where(block => block.Operations.Concat(block.BranchValue is null ? [] : [block.BranchValue])
                    .Where(root => root is not null)
                    .SelectMany(root => Enumerate(root!))
                    .Any(candidate => candidate.Syntax is { } syntax
                        && operation.Syntax is { } target
                        && syntax.SyntaxTree == target.SyntaxTree
                        && syntax.Span.Contains(target.Span)))
                .OrderBy(block => block.Operations.Concat(block.BranchValue is null ? [] : [block.BranchValue])
                    .Where(root => root is not null)
                    .SelectMany(root => Enumerate(root!))
                    .Where(candidate => candidate.Syntax is { } syntax && operation.Syntax is { } target
                        && syntax.SyntaxTree == target.SyntaxTree && syntax.Span.Contains(target.Span))
                    .Select(candidate => candidate.Syntax!.Span.Length)
                    .DefaultIfEmpty(int.MaxValue)
                    .Min())
                .FirstOrDefault();

    private static IEnumerable<IOperation> Enumerate(IOperation root)
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

    private static OperationDescriptor AddInitializerEvidence(OperationDescriptor descriptor, IOperation initializer, IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext>? documents, IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
    {
        if (documents is null || !documents.TryGetValue(initializer.Syntax.SyntaxTree, out var document))
        {
            return descriptor;
        }
        var evidence = RoslynProgramIndexExtractor.CreateSourceEvidence(
            document.Document.Id, document.Document.LogicalPath, document.Text,
             initializer.Syntax.Span,
             models.TryGetValue(initializer.Syntax.SyntaxTree, out var model)
                 ? model.GetSymbolInfo(initializer.Syntax).Symbol?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat)
                 : null,
             document.Document.Origin == SeqDoc.Core.ProgramIndex.DocumentOrigin.GeneratedSource);
        return descriptor with { Evidence = descriptor.Evidence.Add(evidence) };
    }

    private static FrameworkMethodIdentity Identity(IMethodSymbol method)
        => new(method.ContainingAssembly!.Identity.Name,
            RoslynProgramIndexExtractor.GetMetadataName(method.ContainingType), method.MetadataName, method.Arity,
            method.Parameters.Select(parameter => new ParameterIdentityDescriptor(
                RoslynProgramIndexExtractor.ToParameterRefKind(parameter.RefKind),
                parameter.Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat))).ToImmutableArray(),
             method.ReturnType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
             method.ContainingAssembly.Identity.Version?.ToString(),
             method.ContainingAssembly.Identity.PublicKeyToken is { Length: > 0 } token
                 ? Convert.ToHexString(token.ToArray()).ToLowerInvariant()
                 : null);

    private static (string SetType, string ContextType, string MemberName, string EntityType)? FindSourceDbSetProperty(IInvocationOperation call, IReadOnlyDictionary<SyntaxTree, SemanticModel> models)
    {
        if (!models.TryGetValue(call.Syntax.SyntaxTree, out var model))
        {
            return null;
        }

        var syntaxRoots = new[] { call.Syntax, call.Instance?.Syntax }
            .Where(syntax => syntax is not null)
            .Select(syntax => syntax!);
        foreach (var member in syntaxRoots.SelectMany(syntax => syntax.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()))
        {
            if (model.GetSymbolInfo(member).Symbol is IPropertySymbol property
                && property.Type is INamedTypeSymbol set
                && RoslynProgramIndexExtractor.GetMetadataName(set.OriginalDefinition) == DbSet
                && set.OriginalDefinition.ContainingAssembly?.Identity.Name == "EntityFramework"
                && HasToken(set.OriginalDefinition.ContainingAssembly, "b77a5c561934e089")
                && set.OriginalDefinition.ContainingAssembly.Identity.Version?.ToString() == "6.0.0.0"
                && IsDerivedContext(property.ContainingType))
            {
                return (
                    set.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat),
                    property.ContainingType?.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) ?? string.Empty,
                    property.Name,
                    set.TypeArguments[0].ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat));
            }
        }

        return null;
    }

    private static IPropertyReferenceOperation? FindDbSetProperty(IOperation? receiver)
    {
        if (receiver is null)
        {
            return null;
        }

        var pending = new Stack<IOperation>();
        pending.Push(receiver);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current is IPropertyReferenceOperation property
                && property.Property.Type is INamedTypeSymbol set
                && RoslynProgramIndexExtractor.GetMetadataName(set.OriginalDefinition) == DbSet
                && set.OriginalDefinition.ContainingAssembly?.Identity.Name == "EntityFramework"
                && HasToken(set.OriginalDefinition.ContainingAssembly, "b77a5c561934e089")
                && set.OriginalDefinition.ContainingAssembly.Identity.Version?.ToString() == "6.0.0.0")
            {
                return property;
            }

            foreach (var child in current.ChildOperations)
            {
                pending.Push(child);
            }
        }

        return null;
    }

    private static bool IsDerivedContext(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (RoslynProgramIndexExtractor.GetMetadataName(current.OriginalDefinition) == "System.Data.Entity.DbContext"
                && current.OriginalDefinition.ContainingAssembly?.Identity.Name == "EntityFramework"
                && HasToken(current.OriginalDefinition.ContainingAssembly, "b77a5c561934e089")
                && current.OriginalDefinition.ContainingAssembly.Identity.Version?.ToString() == "6.0.0.0")
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasToken(IAssemblySymbol? assembly, string expected)
        => assembly?.Identity.PublicKeyToken is { Length: > 0 } token
            && Convert.ToHexString(token.ToArray()).Equals(expected, StringComparison.OrdinalIgnoreCase);


    private static IOperation? Unwrap(IOperation? operation)
    {
        for (var depth = 0; depth < 8 && operation is IConversionOperation conversion; depth++)
        {
            operation = conversion.Operand;
        }
        return operation;
    }
}
