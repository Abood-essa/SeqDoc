using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Core.Frameworks;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Frameworks;

/// <summary>
/// Projects the compiler-proven shape of one invocation whose target method may be an admitted
/// service-client operation call. Reuses the same eligibility projection a service-implementation
/// method gets (<see cref="FrameworkSymbolEligibilityProjector.ProjectMethodShape"/>), so a
/// client-invocation-aware framework model can prove admitted interface-member identity, the declaring
/// type's constructed <c>ClientBase&lt;TContract&gt;</c> base, and generated-marker attributes without a
/// second symbol walk. Additionally projects the invocation receiver's own exact static-type identity
/// and whether it is a concrete class (never an interface — an interface-typed receiver is an ambiguous
/// receiver a model must reject), and the compiler-proven syntactic disposition of the call site's own
/// result (discarded/assigned/returned/unclaimed, optionally awaited) purely from the invocation
/// operation's own position in its control-flow graph. Nothing here decides admission, registration, or
/// a response/fault claim — that decision belongs to the framework model that consumes this shape.
/// </summary>
internal static class CoreWcfClientInvocationScanner
{
    /// <summary>
    /// Returns null for a static, generic, non-ordinary, or receiver-less invocation, or when no project
    /// context is available to resolve symbol identities.
    /// </summary>
    public static FrameworkClientInvocationShapeDescriptor? Project(
        IInvocationOperation call,
        StableProjectId? project,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents)
    {
        if (project is not { } projectId)
        {
            return null;
        }

        var target = call.TargetMethod.OriginalDefinition;
        if (target.MethodKind != MethodKind.Ordinary
            || target.IsStatic
            || target.IsAbstract
            || target.IsGenericMethod
            || target.ContainingType is null
            || target.ContainingType.AllInterfaces.IsDefaultOrEmpty
            || call.Instance is null)
        {
            return null;
        }

        if (FrameworkAnalysisRequestProjector.UnwrapAllConversionsAndParentheses(call.Instance).Type is not INamedTypeSymbol receiverType)
        {
            return null;
        }

        var shape = FrameworkSymbolEligibilityProjector.ProjectMethodShape(target, projectId, documents);
        if (shape is null)
        {
            return null;
        }

        var (resultClaim, isAwaited, bindingName) = ProjectResultClaim(call);
        return new FrameworkClientInvocationShapeDescriptor(
            shape,
            RoslynProgramIndexExtractor.CreateSymbolId(receiverType, projectId),
            receiverType.TypeKind == TypeKind.Class,
            resultClaim,
            isAwaited,
            bindingName,
            target.ReturnType.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat));
    }

    /// <summary>
    /// Projects the compiler-proven syntactic disposition of one invocation's own result: discarded
    /// (a bare expression statement), assigned to a local/parameter, directly returned, or unclaimed
    /// (chained member access, an argument, a field store, or a discard assignment) — optionally
    /// awaited when the call itself (before any further use) is the operand of an <c>await</c>. This is
    /// a syntactic classification of the call site only; it never inspects or claims anything about a
    /// runtime response.
    /// </summary>
    private static (ClientInvocationResultClaimKind Claim, bool IsAwaited, string? BindingName) ProjectResultClaim(
        IInvocationOperation call)
    {
        IOperation current = call;
        while (current.Parent is IConversionOperation { IsImplicit: true } conversion)
        {
            current = conversion;
        }

        var isAwaited = false;
        if (current.Parent is IAwaitOperation awaitOperation)
        {
            isAwaited = true;
            current = awaitOperation;
            while (current.Parent is IConversionOperation { IsImplicit: true } awaitedConversion)
            {
                current = awaitedConversion;
            }
        }

        // The compiler's control-flow graph represents both an explicit `return expr;` and an
        // expression-bodied `=> expr;` as the owning basic block's own BranchValue, never as a nested
        // IReturnOperation, so the returned expression's own Parent is always null at this point — this
        // is not a detached/unreachable operation. The only way to recover the "returned" claim is to
        // ask whether the exact enclosing member is void (equivalent to a discarded statement, since
        // nothing is returned) or value-returning (a genuine response claim).
        if (current.Parent is null)
        {
            var enclosingMethod = call.SemanticModel?.GetEnclosingSymbol(current.Syntax.SpanStart) as IMethodSymbol;
            return enclosingMethod is { ReturnsVoid: true }
                ? (ClientInvocationResultClaimKind.Discarded, isAwaited, null)
                : (ClientInvocationResultClaimKind.ResultReturned, isAwaited, null);
        }

        return current.Parent switch
        {
            IExpressionStatementOperation => (ClientInvocationResultClaimKind.Discarded, isAwaited, null),
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } =>
                (ClientInvocationResultClaimKind.ResultAssigned, isAwaited, declarator.Symbol.Name),
            ISimpleAssignmentOperation { Target: ILocalReferenceOperation local } =>
                (ClientInvocationResultClaimKind.ResultAssigned, isAwaited, local.Local.Name),
            ISimpleAssignmentOperation { Target: IParameterReferenceOperation parameter } =>
                (ClientInvocationResultClaimKind.ResultAssigned, isAwaited, parameter.Parameter.Name),
            _ => (ClientInvocationResultClaimKind.Unclaimed, isAwaited, null),
        };
    }
}
