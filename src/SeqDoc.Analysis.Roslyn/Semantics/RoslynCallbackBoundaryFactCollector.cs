using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SeqDoc.Analysis.Roslyn.Behavior;
using SeqDoc.Analysis.Roslyn.Frameworks;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Semantics;

/// <summary>
/// Accumulates callback-boundary companion fact inputs during one Roslyn compilation/extraction
/// session and builds the Roslyn-neutral, memory-only <see cref="CallbackBoundaryFactSet"/>. The
/// extractor registers every extracted source method body together with its authoritative operation
/// map; exact callback analysis is deferred to <see cref="Build"/> so every loaded project's
/// contexts and extracted bodies are visible regardless of per-project processing order (regression).
/// The collector then enumerates compiler invocation operations whose arguments bind to delegate
/// parameters of an exact callee. Only compiler-bound anonymous functions, source local
/// functions, and source method groups become exact targets, and only when the compiler proves the
/// dispatch is direct: static methods (including extension methods), constructors/local functions,
/// non-virtual/non-abstract instance methods, sealed methods, and methods whose containing type is
/// sealed. Interface methods, abstract methods, and virtual/override dispatch that is not sealed
/// or proven exact are rejected without inferring a runtime receiver type or selecting the first
/// candidate (regression). Delegate variables, events, dynamic/unsupported conversions,
/// metadata-only callback targets, and unresolved overloads fail closed and never select a
/// candidate by position. An exact static metadata callee (for example a framework extension
/// method) whose source contract body is unavailable may still project a target/member boundary
/// from an exact source callback argument, but its contract stays Unknown in
/// provenance/cardinality/trigger with null contract anchors; a metadata callee is never
/// presented as a definite source-body contract (accepted contract). Outer invocations, contract invokes,
/// conditional triggers, and method-group members
/// reuse the accepted extracted operation identities and never recreate zero-ordinal identities
/// (regression). Cardinality is exactly once only when the direct invocation block dominates every
/// normal entry-to-exit path of the contract's authoritative Roslyn control-flow graph; an early
/// terminating path, loop, repeated invoke, conditional, or unsupported control degrades rather
/// than claiming exactly once (regression). Callback-local completion rejoins the caller only for
/// bounded synchronous bodies; async targets, awaits, try/catch/finally, using/lock, yield,
/// throws, and invalid or unsupported operations stay Unknown and never terminate the outer
/// scenario by inference (regression). Canonical member operations, evidence, and certainty are
/// projected from the exact bounded source contract so ambiguous, repeated, arbitrary, and
/// unsupported callbacks stay conservative or unknown. The collector is Roslyn-specific and
/// produces Core-neutral output; accepted Method Flow, call resolution, and behavior fingerprints
/// are never altered.
/// </summary>
internal sealed class RoslynCallbackBoundaryFactCollector
{
    private const string ProducerVersion = "0.1.0-alpha";

    private readonly Dictionary<string, RegisteredMethodContext> _registeredMethods = new(StringComparer.Ordinal);
    private readonly Dictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> _documents = new();
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = new();

    private Func<IOperation, MethodId, string, int, int, int, OperationId>? _operationIdFactory;
    private Func<IOperation, ImmutableArray<EvidenceRef>, ImmutableArray<EvidenceRef>>? _resolveEvidenceFactory;
    private Dictionary<IAssemblySymbol, StableProjectId>? _projectsByAssembly;

    /// <summary>
    /// The accumulated <c>CoreWcfHostChainScanner</c> proof across every loaded project, so an exact
    /// <c>AddServiceEndpoint</c> invocation reached only through this collector's separate
    /// callback-body companion projection (not the accepted body traversal, since a lambda/local
    /// callback target has no accepted extracted Method Flow) still carries its host-chain proof
    /// instead of silently defaulting to unproven. Syntax nodes never collide across projects, so
    /// merging per-project proofs here is safe.
    /// </summary>
    private ImmutableDictionary<SyntaxNode, ImmutableArray<EvidenceRef>> _hostChainProof =
        ImmutableDictionary<SyntaxNode, ImmutableArray<EvidenceRef>>.Empty;

    public void AddHostChainProof(ImmutableDictionary<SyntaxNode, ImmutableArray<EvidenceRef>> proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        _hostChainProof = _hostChainProof.SetItems(proof);
    }

    /// <summary>
    /// Anonymous/local callback target bodies collected while projecting boundaries. During
    /// <see cref="Build"/> these bodies' source-backed descendant invocations become companion
    /// framework operation descriptors so framework models and the scenario graph can see work that
    /// lives only inside a callback body (accepted contract).
    /// </summary>
    private readonly List<CompanionTarget> _companionTargets = [];

    /// <summary>
    /// Companion framework operation descriptors projected during <see cref="Build"/> from exact
    /// anonymous/local callback target bodies. Ordinary method-group targets reuse the accepted
    /// extracted operations and are never projected here.
    /// </summary>
    private readonly List<OperationDescriptor> _frameworkOperations = [];

    /// <summary>
    /// Gets the accumulated per-tree document map. The extractor's operation/evidence factories close
    /// over this map so callback anchors reuse the exact accepted identity and evidence algorithms
    /// while the map grows across the projects of one compilation session.
    /// </summary>
    internal IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> Documents => _documents;

    /// <summary>
    /// Supplies the stable operation-identity and evidence factories. The extractor wires its private
    /// <c>CreateOperationId</c>/<c>ResolveEvidence</c> helpers here so the collector never reaches
    /// into the extractor and every callback anchor stays deterministic and evidence-backed.
    /// </summary>
    public void SetIdentityFactories(
        Func<IOperation, MethodId, string, int, int, int, OperationId> operationIdFactory,
        Func<IOperation, ImmutableArray<EvidenceRef>, ImmutableArray<EvidenceRef>> resolveEvidenceFactory)
    {
        _operationIdFactory = operationIdFactory;
        _resolveEvidenceFactory = resolveEvidenceFactory;
    }

    /// <summary>
    /// Accumulates the documents, semantic models, and project map of one loaded project so callee
    /// and target bodies resolve deterministically for the whole session. Syntax trees never collide
    /// across projects because each tree belongs to exactly one compilation.
    /// </summary>
    public void AddProjectContext(
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents,
        IReadOnlyDictionary<SyntaxTree, SemanticModel> models,
        Dictionary<IAssemblySymbol, StableProjectId> projectsByAssembly)
    {
        foreach (var pair in documents)
        {
            _documents.TryAdd(pair.Key, pair.Value);
        }

        foreach (var pair in models)
        {
            _models.TryAdd(pair.Key, pair.Value);
        }

        _projectsByAssembly = projectsByAssembly;
    }

    /// <summary>
    /// Registers one extracted source method body with the authoritative operation map produced by
    /// the accepted behavior extraction. No callback boundary is projected here: exact analysis is
    /// deferred to <see cref="Build"/> so every loaded project's document/model contexts and every
    /// extracted method body are visible before any boundary is resolved, making cross-project
    /// contracts and targets independent of per-project processing order (regression). The authoritative
    /// <paramref name="operationById"/> map is retained so every projected boundary anchor reuses the
    /// exact accepted operation identities instead of recreating zero-ordinal identities (regression).
    /// </summary>
    public void AddMethod(
        StableProjectId project,
        MethodId methodId,
        IMethodBodyOperation bodyOperation,
        ImmutableArray<EvidenceRef> methodEvidence,
        Dictionary<IOperation, OperationId> operationById,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bodyOperation);
        ArgumentNullException.ThrowIfNull(operationById);
        cancellationToken.ThrowIfCancellationRequested();
        _registeredMethods[methodId.Value] = new RegisteredMethodContext(
            project,
            methodId,
            bodyOperation,
            methodEvidence,
            operationById,
            BuildAcceptedSpanIndex(methodId, operationById));
    }

    /// <summary>
    /// Analyzes every registered extracted method deterministically in stable method-id order and
    /// returns the exact callback boundary drafts. All project contexts and extracted method bodies
    /// are known at this point, so a caller never depends on which project was processed first
    /// (regression).
    /// </summary>
    private ImmutableArray<BoundaryDraft> AnalyzeRegisteredMethods(CancellationToken cancellationToken)
    {
        if (_operationIdFactory is null || _resolveEvidenceFactory is null || _projectsByAssembly is null)
        {
            return [];
        }

        var drafts = new List<BoundaryDraft>();
        foreach (var context in _registeredMethods.Values
                     .OrderBy(context => context.MethodId.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnalyzeMethod(context, drafts, cancellationToken);
        }

        return drafts.ToImmutableArray();
    }

    /// <summary>
    /// Inspects one registered extracted source method body for exact callback boundaries. Every
    /// invocation whose compiler target has compiler-proven direct dispatch is a candidate
    /// contract (regression); each argument bound to a delegate parameter of that contract is
    /// resolved to an exact target form. An exact source contract's own extracted body bounds
    /// cardinality/trigger; an exact static metadata contract (for example a framework extension
    /// method) has no extracted body, so its contract stays Unbounded/Unknown with null anchors
    /// (accepted contract). The exact target body always bounds callback-local completion and canonical member
    /// operations. The outer invocation and every contract invoke/condition anchor must
    /// resolve to the accepted operation identities of the caller/contract extracted bodies; a
    /// recreated identity is never used and the first candidate is never selected (regression).
    /// Metadata-only callback targets, delegate variables, events, dynamic/unsupported
    /// conversions, unresolved overloads, and unresolvable target bodies fail closed without a
    /// boundary.
    /// </summary>
    private void AnalyzeMethod(
        RegisteredMethodContext context,
        List<BoundaryDraft> drafts,
        CancellationToken cancellationToken)
    {
        var project = context.Project;
        var methodId = context.MethodId;
        var bodyOperation = context.BodyOperation;
        var methodEvidence = context.Evidence;
        var operationById = context.OperationById;
        var spanIndex = context.SpanIndex;

        foreach (var operation in EnumerateTopLevelOperations(bodyOperation, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation is not IInvocationOperation outer
                || !IsSourceBacked(outer)
                || outer.TargetMethod is not { } contractMethod
                || !IsDirectDispatchProven(contractMethod))
            {
                // An unresolved/dynamic target or a dispatchable virtual/interface/abstract
                // contract has no exact compiler-bound body; the invocation fails closed
                // (regression). A metadata-only contract with direct dispatch (for example a static
                // framework extension method) is admitted below: its source contract body is
                // unavailable so AnalyzeContract returns an unbounded Unknown contract, but an
                // exact source callback argument may still project a target/member boundary
                // (accepted contract).
                continue;
            }

            // The outer invocation must be the caller's accepted flattened operation; without it the
            // boundary cannot join the accepted Method Flow and fails closed (regression).
            if (!TryResolveAcceptedOperationId(outer, methodId, operationById, spanIndex, out var outerInvocationOperation))
            {
                continue;
            }

            foreach (var argument in outer.Arguments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (argument.Parameter is null
                    || argument.Parameter.Type is not INamedTypeSymbol { TypeKind: TypeKind.Delegate }
                    || argument.Value is null
                    || argument.IsImplicit)
                {
                    continue;
                }

                if (!TryResolveTarget(argument.Value, out var targetKind, out var targetMethod, out var targetBodyOperation))
                {
                    // A delegate variable, event, dynamic dispatch, unsupported conversion, or
                    // unresolved overload never becomes an exact boundary; the first compiler
                    // candidate is never selected.
                    continue;
                }

                // Resolve the exact target body and its authoritative identity source:
                // - an anonymous function is a companion body anchor of the caller (never an
                //   accepted Method Flow), so its member anchors stay span-based companion
                //   identities;
                // - a source local function is not an accepted Method Flow either, so its member
                //   anchors stay companion identities resolved through the accumulated models;
                // - an ordinary source method-group target must reuse the accepted operation
                //   identities of its extracted body; a target with no accepted body fails closed
                //   (regression).
                IOperation targetBody;
                RegisteredMethodContext? targetContext = null;
                switch (targetKind)
                {
                    case CallbackTargetKind.AnonymousFunction:
                        targetBody = targetBodyOperation!;
                        break;
                    case CallbackTargetKind.LocalFunction:
                        if (ResolveSourceBody(targetMethod!, cancellationToken) is not { } localFunctionBody)
                        {
                            continue;
                        }

                        targetBody = localFunctionBody;
                        break;
                    case CallbackTargetKind.MethodGroup:
                        var methodGroupTargetId = CreateMethodId(targetMethod!, methodId, project);
                        if (!_registeredMethods.TryGetValue(methodGroupTargetId.Value, out var methodGroupContext))
                        {
                            // An ordinary source method-group target whose body was never extracted
                            // (for example a bodyless method) has no authoritative operation ids;
                            // the boundary fails closed rather than claiming companion compatibility
                            // with an accepted body (regression).
                            continue;
                        }

                        targetContext = methodGroupContext;
                        targetBody = methodGroupContext.BodyOperation;
                        break;
                    default:
                        continue;
                }

                var contract = AnalyzeContract(
                    contractMethod,
                    CreateMethodId(contractMethod, methodId, project),
                    argument.Parameter,
                    cancellationToken);
                var completion = AnalyzeCompletion(
                    targetBody,
                    ResolveTargetSymbol(targetKind, targetMethod, targetBody),
                    cancellationToken);
                var targetMethodId = targetKind == CallbackTargetKind.AnonymousFunction
                    ? (MethodId?)null
                    : CreateMethodId(targetMethod!, methodId, project);
                var memberOperations = CollectMemberOperations(
                    targetBody,
                    targetMethodId ?? methodId,
                    targetKind == CallbackTargetKind.MethodGroup ? targetContext : null,
                    cancellationToken);
                if (memberOperations.IsEmpty)
                {
                    // A method-group target whose accepted body exposes no flattenable member
                    // operations carries no authoritative member set (see CollectMemberOperations);
                    // the boundary fails closed exactly like a target whose body was never extracted
                    // instead of projecting an identity over an empty canonical member set.
                    continue;
                }
                // The contract-invoke operation lives in the callee body; only its stable identity is
                // retained in the contract analysis, so the boundary evidence combines the caller-side
                // invocation, the callback argument, and the exact target body.
                var evidence = CombineEvidence(
                    ResolveEvidence(outer, methodEvidence),
                    ResolveEvidence(argument.Value, methodEvidence),
                    ResolveEvidence(targetBody, methodEvidence));
                var certainty = contract.Cardinality is CallbackCardinality.ExactlyOnce or CallbackCardinality.ZeroOrOne
                        && completion == CallbackCompletionKind.RejoinsCaller
                    ? CertaintyLevel.Exact
                    : CertaintyLevel.Conservative;

                drafts.Add(new BoundaryDraft(
                    methodId,
                    outerInvocationOperation,
                    argument.Parameter.Ordinal,
                    targetKind,
                    targetMethodId,
                    targetKind == CallbackTargetKind.AnonymousFunction
                        ? CreateOperationId(targetBodyOperation!, methodId, "AnonymousFunction")
                        : null,
                    contract.ContractMethod,
                    contract.ContractInvokeOperation,
                    contract.Cardinality,
                    contract.Trigger,
                    contract.TriggerCondition,
                    completion,
                    contract.Provenance,
                    memberOperations,
                    evidence,
                    certainty));

                // Anonymous/local callback targets have no accepted extracted Method Flow, so their
                // descendant framework calls would be invisible to framework models and the scenario
                // graph; the target body is registered here and its source-backed descendant
                // invocations become companion operation descriptors during Build with the same
                // owner method and span-based ids as the boundary member set (accepted contract). Ordinary
                // method-group targets reuse the accepted extracted body and are never duplicated.
                if (targetKind is CallbackTargetKind.AnonymousFunction or CallbackTargetKind.LocalFunction)
                {
                    _companionTargets.Add(new CompanionTarget(
                        targetBody,
                        targetMethodId ?? methodId,
                        methodEvidence,
                        project));
                }
            }
        }
    }

    public CallbackBoundaryFactSet Build(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint, nameof(programIndexFingerprint));

        // Exact callback analysis runs here, after every loaded project context and every extracted
        // method body is registered, so cross-project contracts and targets resolve deterministically
        // and no boundary is lost to per-project processing order (regression). The boundary identity
        // canonically encodes every semantic anchor, so drafts that project onto the same identity
        // carry identical payloads; grouping keeps the set deterministic regardless of collection
        // order. Anonymous/local callback target bodies are projected into companion framework
        // operation descriptors only after the boundaries exist (accepted contract).
        _companionTargets.Clear();
        _frameworkOperations.Clear();
        var boundaries = AnalyzeRegisteredMethods(cancellationToken)
            .Select(draft => ProjectBoundary(profile.Id, draft))
            .GroupBy(fact => fact.Id.Value, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(fact => fact.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var debugProjection = BuildDebugProjection(
            profile,
            programIndexFingerprint,
            boundaries,
            diagnostics.Length);
        BuildCompanionFrameworkOperations(cancellationToken);
        return new CallbackBoundaryFactSet(
            1,
            ProducerVersion,
            profile,
            programIndexFingerprint,
            boundaries,
            diagnostics,
            debugProjection);
    }

    /// <summary>
    /// Returns the canonical, deterministic companion framework operation descriptors projected
    /// during <see cref="Build"/> from the exact source-backed descendant invocations of
    /// anonymous/local callback target bodies (accepted contract). Anonymous functions and local functions have
    /// no accepted extracted Method Flow, so their descendant invocations would otherwise be
    /// invisible to framework models and the scenario graph; the companion descriptors reuse the
    /// exact span-based companion operation ids of the callback boundary member set so a framework
    /// call inside a cache-miss factory (for example the real CustomerManagement EF query) joins the
    /// boundary by operation identity. Ordinary method-group targets reuse the accepted extracted
    /// body and are never duplicated here. Descriptors are deduplicated by stable identity and
    /// emitted in identity order so encounter order never changes the framework-model request.
    /// </summary>
    public ImmutableArray<OperationDescriptor> BuildFrameworkOperations()
        => _frameworkOperations
            .DistinctBy(operation => operation.Id.Value)
            .OrderBy(operation => operation.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();

    /// <summary>
    /// Projects the companion framework operation descriptors of every anonymous/local callback
    /// target that produced a boundary. The complete companion operation map for each target body
    /// uses the same owner method and accepted <see cref="ExtractedOperationKind"/> kind strings as
    /// the boundary member set, so the projected terminal invocation shares the exact member
    /// OperationId and query-chain/predicate step ids resolve from the complete map. The outer
    /// metadata invocation itself is never projected from the callback body: it lives in the
    /// caller's accepted body and is projected by the accepted traversal. An invocation with no map
    /// entry fails closed because the companion id is never recreated after the map is built.
    /// </summary>
    private void BuildCompanionFrameworkOperations(CancellationToken cancellationToken)
    {
        if (_operationIdFactory is null || _resolveEvidenceFactory is null || _projectsByAssembly is null)
        {
            return;
        }

        foreach (var target in _companionTargets.OrderBy(item => item.OwnerMethodId.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operationMap = BuildCompanionOperationMap(target.TargetBody, target.OwnerMethodId, cancellationToken);
            foreach (var operation in EnumerateOperations(target.TargetBody, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation is not IInvocationOperation call || !IsSourceBacked(call))
                {
                    continue;
                }

                if (!operationMap.TryGetValue(call, out var invocationId))
                {
                    continue;
                }

                _frameworkOperations.Add(FrameworkAnalysisRequestProjector.ProjectOperationDescriptor(
                    call,
                    target.OwnerMethodId,
                    invocationId,
                    ResolveEvidence(call, target.MethodEvidence),
                    operationMap,
                    _documents,
                    _models,
                    project: target.Project,
                    hostChainProof: _hostChainProof,
                    dispatchCancellationToken: cancellationToken));
            }
        }
    }

    /// <summary>
    /// Builds the complete companion operation map of one source-backed callback target body: every
    /// source-backed descendant operation receives the same span-based companion id the boundary
    /// member set uses, with the same owner method and the accepted
    /// <see cref="ExtractedOperationKind"/> kind strings, and companion ordinals remain zero as
    /// accepted contract ids. The map is complete so query-chain/predicate anchors of nested framework calls
    /// resolve deterministically.
    /// </summary>
    private Dictionary<IOperation, OperationId> BuildCompanionOperationMap(
        IOperation targetBody,
        MethodId ownerMethodId,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<IOperation, OperationId>();
        foreach (var operation in EnumerateOperations(targetBody, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSourceBacked(operation))
            {
                continue;
            }

            map[operation] = CreateOperationId(
                operation,
                ownerMethodId,
                RoslynBehaviorExtractor.MapKind(operation).ToString());
        }

        return map;
    }

    /// <summary>
    /// Resolves the exact source body of a source method or local function from its declaring syntax
    /// reference and the accumulated semantic models. A metadata-only method has no declaring source
    /// reference, and a source method whose tree is not indexed yet resolves to null so the caller
    /// fails closed.
    /// </summary>
    private IOperation? ResolveSourceBody(IMethodSymbol method, CancellationToken cancellationToken)
    {
        foreach (var reference in method.DeclaringSyntaxReferences.OrderBy(item => item.Span.Start))
        {
            var syntax = reference.GetSyntax(cancellationToken);
            if (_models.TryGetValue(syntax.SyntaxTree, out var model)
                && model.GetOperation(syntax, cancellationToken) is IOperation { } operation
                && operation is IMethodBodyOperation or ILocalFunctionOperation)
            {
                return operation;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the stable source-key index over one accepted extraction's operation identities. The
    /// control-flow-graph operation instances and the body-tree operation instances can differ for
    /// one source span, so this index lets callback analysis bind body-tree operations to the exact
    /// accepted flattened identities without inventing or duplicating ids. The key is the
    /// deterministic source key (method id, document identity, source span, Roslyn operation kind);
    /// a key that maps to several accepted operations is removed entirely because exactly one match
    /// is required and the first candidate is never selected (regression).
    /// </summary>
    private static Dictionary<OperationSourceKey, OperationId> BuildAcceptedSpanIndex(
        MethodId methodId,
        Dictionary<IOperation, OperationId> operationById)
    {
        var spanToId = new Dictionary<OperationSourceKey, OperationId>();
        var ambiguous = new HashSet<OperationSourceKey>();
        foreach (var pair in operationById)
        {
            if (!IsSourceBacked(pair.Key) || pair.Key.Syntax is null)
            {
                continue;
            }

            var key = new OperationSourceKey(
                methodId,
                pair.Key.Syntax.SyntaxTree,
                pair.Key.Syntax.SpanStart,
                pair.Key.Syntax.Span.Length,
                pair.Key.Kind.ToString());
            if (!spanToId.TryAdd(key, pair.Value))
            {
                ambiguous.Add(key);
            }
        }

        foreach (var key in ambiguous)
        {
            spanToId.Remove(key);
        }

        return spanToId;
    }

    /// <summary>
    /// Resolves the accepted operation identity of a body-tree operation from the authoritative
    /// operation map of the exact extracted method. The instance map is consulted first; a distinct
    /// body-tree instance then resolves through the deterministic source key index with exactly one
    /// match. Operations the accepted traversal never flattened are not compiler-bound and return
    /// false (regression).
    /// </summary>
    private static bool TryResolveAcceptedOperationId(
        IOperation operation,
        MethodId methodId,
        IReadOnlyDictionary<IOperation, OperationId> operationById,
        IReadOnlyDictionary<OperationSourceKey, OperationId> spanIndex,
        out OperationId id)
    {
        if (operationById.TryGetValue(operation, out id))
        {
            return true;
        }

        if (operation.Syntax is { } syntax && syntax.Span.Length > 0)
        {
            return spanIndex.TryGetValue(
                new OperationSourceKey(
                    methodId,
                    syntax.SyntaxTree,
                    syntax.SpanStart,
                    syntax.Span.Length,
                    operation.Kind.ToString()),
                out id);
        }

        id = default;
        return false;
    }

    /// <summary>
    /// Bounds the exact source contract: the direct invocations of the delegate parameter inside the
    /// callee body and their controlling shape. The contract body is the registered extracted body of
    /// the exact source method, so its operation identities are the accepted ones; a contract that
    /// was never extracted has no bounded body and stays unbounded. An exact metadata callee (for
    /// example a static framework extension method) is never registered, so its contract stays
    /// unbounded Unknown with null anchors and never claims a definite source-body contract (accepted contract). One direct invocation outside
    /// nested control is ExactlyOnce/Unconditional only when the invocation block dominates every
    /// normal entry-to-exit path of the contract's authoritative Roslyn control-flow graph; an early
    /// terminating path that reaches the exit without the invocation block (for example an early
    /// return) degrades cardinality and trigger to Unknown (regression). One invocation in one direct
    /// supported <c>if</c> arm is ZeroOrOne/Conditional with the exact condition anchor; multiple
    /// invocations or any loop is RepeatedOrUnknown; switch/try/using/lock/unsupported control is
    /// Unknown. A delegate-parameter reference that escapes (stored, returned, passed on, or
    /// captured) or the absence of any direct invoke leaves the contract unbounded with unknown
    /// cardinality. The contract-invoke and trigger-condition anchors must resolve to accepted
    /// operation identities of the extracted contract body; an unresolvable anchor fails the exact
    /// definite contract and degrades to Unknown (regression).
    /// </summary>
    private ContractAnalysis AnalyzeContract(
        IMethodSymbol contractMethod,
        MethodId contractMethodId,
        IParameterSymbol delegateParameter,
        CancellationToken cancellationToken)
    {
        if (!_registeredMethods.TryGetValue(contractMethodId.Value, out var context))
        {
            return ContractAnalysis.Unbounded;
        }

        var body = context.BodyOperation;
        var invokes = new List<IOperation>();
        var escapes = false;
        foreach (var operation in EnumerateTopLevelOperations(body, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (operation)
            {
                case IInvocationOperation invocation when IsDelegateParameterInvoke(invocation, delegateParameter):
                    invokes.Add(invocation);
                    break;
                case IParameterReferenceOperation reference
                    when SymbolEqualityComparer.Default.Equals(reference.Parameter, delegateParameter)
                    && !IsDelegateInvokeInstance(reference):
                    escapes = true;
                    break;
            }
        }

        if (escapes || invokes.Count == 0)
        {
            return ContractAnalysis.Unbounded;
        }

        var sawUnsupportedControl = false;
        var sawLoop = false;
        var sawConditionalAccess = false;
        IConditionalOperation? conditionalBoundary = null;
        var hasAdditionalConditional = false;
        foreach (var invoke in invokes)
        {
            foreach (var ancestor in EnumerateAncestors(invoke))
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (ancestor)
                {
                    case ISwitchOperation or ITryOperation or IUsingOperation or ILockOperation
                        or IInvalidOperation or ICatchClauseOperation:
                        sawUnsupportedControl = true;
                        break;
                    case ILoopOperation:
                        sawLoop = true;
                        break;
                    case IConditionalAccessOperation:
                        sawConditionalAccess = true;
                        break;
                    case IConditionalOperation conditional:
                        if (conditionalBoundary is null)
                        {
                            conditionalBoundary = conditional;
                        }
                        else if (!ReferenceEquals(conditionalBoundary, conditional))
                        {
                            hasAdditionalConditional = true;
                        }

                        break;
                }
            }
        }

        var firstInvoke = invokes[0];
        // The contract-invoke operation must be an accepted operation of the extracted contract
        // method body. A body-tree instance difference is resolved through the deterministic source
        // key (method id + document identity + source span + Roslyn operation kind) with exactly one
        // match; when no accepted identity exists, the exact definite contract fails and the contract
        // degrades to Unknown without anchors (regression).
        if (!TryResolveAcceptedOperationId(firstInvoke, contractMethodId, context.OperationById, context.SpanIndex, out var firstInvokeOperation))
        {
            return ContractAnalysis.Unbounded;
        }

        if (sawUnsupportedControl)
        {
            return ContractAnalysis.Exact(
                contractMethodId,
                firstInvokeOperation,
                CallbackCardinality.Unknown,
                CallbackTriggerKind.Unknown,
                null);
        }

        if (invokes.Count > 1 || sawLoop)
        {
            return ContractAnalysis.Exact(
                contractMethodId,
                firstInvokeOperation,
                CallbackCardinality.RepeatedOrUnknown,
                CallbackTriggerKind.Unknown,
                null);
        }

        if (conditionalBoundary is null && !hasAdditionalConditional && !sawConditionalAccess)
        {
            if (!InvokeDominatesNormalExits(firstInvoke, context.BodyOperation, cancellationToken))
            {
                // An earlier normal-terminating path (for example an early return) can reach the
                // contract exit without the callback invoke, so the invocation is not proven to
                // execute exactly once on every normal entry-to-exit path; the definite contract
                // degrades to Unknown cardinality and trigger instead of claiming ExactlyOnce
                // (regression).
                return ContractAnalysis.Exact(
                    contractMethodId,
                    firstInvokeOperation,
                    CallbackCardinality.Unknown,
                    CallbackTriggerKind.Unknown,
                    null);
            }

            return ContractAnalysis.Exact(
                contractMethodId,
                firstInvokeOperation,
                CallbackCardinality.ExactlyOnce,
                CallbackTriggerKind.Unconditional,
                null);
        }

        if (conditionalBoundary is { } singleConditional
            && !hasAdditionalConditional
            && !sawConditionalAccess
            && singleConditional.Syntax is IfStatementSyntax
            && TryResolveAcceptedOperationId(
                singleConditional.Condition,
                contractMethodId,
                context.OperationById,
                context.SpanIndex,
                out var triggerConditionOperation))
        {
            return ContractAnalysis.Exact(
                contractMethodId,
                firstInvokeOperation,
                CallbackCardinality.ZeroOrOne,
                CallbackTriggerKind.Conditional,
                triggerConditionOperation);
        }

        // The conditional trigger anchor is unavailable or the shape is nested/conditional-access,
        // so the zero-or-one definite contract cannot be claimed; the contract degrades to Unknown
        // with the accepted invoke anchor rather than guessing a definite condition.
        return ContractAnalysis.Exact(
            contractMethodId,
            firstInvokeOperation,
            CallbackCardinality.Unknown,
            CallbackTriggerKind.Unknown,
            null);
    }

    /// <summary>
    /// True only when the callback invoke's basic block dominates every normal entry-to-exit path of
    /// the contract's authoritative Roslyn control-flow graph, so the direct invocation is proven to
    /// execute exactly once whenever the contract completes normally (regression). The invoke block is
    /// located by its exact source span in the flattened graph because the graph and body-tree
    /// operation instances differ and identity is never reused; an absent or ambiguous block mapping
    /// or an unavailable graph returns false so the exactly-once claim degrades to Unknown. A path
    /// from Entry to Exit that reaches the Exit block without passing through the invoke block (for
    /// example an early return before the invoke or an unreachable invoke) also returns false.
    /// Cancellation propagates through graph creation and traversal.
    /// </summary>
    private static bool InvokeDominatesNormalExits(
        IOperation invoke,
        IMethodBodyOperation contractBody,
        CancellationToken cancellationToken)
    {
        ControlFlowGraph cfg;
        try
        {
            cfg = ControlFlowGraph.Create(contractBody, cancellationToken);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            // An unavailable authoritative graph (for example an unsupported body shape) degrades
            // the exactly-once claim instead of guessing.
            return false;
        }

        if (invoke.Syntax is not { } invokeSyntax)
        {
            return false;
        }

        BasicBlock? invokeBlock = null;
        foreach (var block in cfg.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!BlockContainsSourceInvocation(block, invokeSyntax, cancellationToken))
            {
                continue;
            }

            if (invokeBlock is not null)
            {
                // Ambiguous block mapping: the exact block cannot be proven, so degrade.
                return false;
            }

            invokeBlock = block;
        }

        if (invokeBlock is null)
        {
            return false;
        }

        // The dominance proof requires the exact unique Entry and Exit blocks of the authoritative
        // graph. A graph without exactly one block of each kind cannot prove a normal path exists,
        // and an ambiguous mapping must never select the first candidate, so the proof fails closed
        // (regression).
        if (!TryResolveUniqueBlock(cfg, BasicBlockKind.Entry, cancellationToken, out var entryBlock)
            || !TryResolveUniqueBlock(cfg, BasicBlockKind.Exit, cancellationToken, out var exitBlock))
        {
            return false;
        }

        var visited = new HashSet<BasicBlock>();
        var pending = new Stack<BasicBlock>();
        pending.Push(entryBlock);
        while (pending.TryPop(out var block))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(block, invokeBlock))
            {
                continue;
            }

            if (ReferenceEquals(block, exitBlock))
            {
                // The exit block is reachable from entry without the invoke block, so an earlier
                // normal-terminating path (for example an early return) can skip the callback and
                // the invocation is not proven exactly once.
                return false;
            }

            if (!visited.Add(block))
            {
                continue;
            }

            if (block.FallThroughSuccessor?.Destination is { } fallThrough)
            {
                pending.Push(fallThrough);
            }

            if (block.ConditionalSuccessor?.Destination is { } conditional)
            {
                pending.Push(conditional);
            }
        }

        return true;
    }

    /// <summary>
    /// Locates the unique block of the requested kind in the control-flow graph. The first candidate
    /// is never selected: a graph with no matching block or with more than one matching block cannot
    /// prove a unique entry/exit, so the caller fails the dominance proof. Cancellation propagates
    /// through the scan.
    /// </summary>
    private static bool TryResolveUniqueBlock(
        ControlFlowGraph cfg,
        BasicBlockKind kind,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out BasicBlock? block)
    {
        BasicBlock? candidate = null;
        foreach (var current in cfg.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Kind != kind)
            {
                continue;
            }

            if (candidate is not null)
            {
                // More than one block claims the kind; the first candidate is never selected.
                block = null;
                return false;
            }

            candidate = current;
        }

        block = candidate;
        return block is not null;
    }

    /// <summary>
    /// True when any operation in the block's flattened operation tree is an invocation whose source
    /// span matches the contract invoke, mapping the body-tree instance to its exact graph block.
    /// </summary>
    private static bool BlockContainsSourceInvocation(
        BasicBlock block,
        SyntaxNode invokeSyntax,
        CancellationToken cancellationToken)
    {
        foreach (var operation in block.Operations)
        {
            if (OperationTreeContainsSourceInvocation(operation, invokeSyntax, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool OperationTreeContainsSourceInvocation(
        IOperation operation,
        SyntaxNode invokeSyntax,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation is IInvocationOperation
            && operation.Syntax is { } syntax
            && syntax.Span.Equals(invokeSyntax.Span))
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (OperationTreeContainsSourceInvocation(child, invokeSyntax, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the invocation directly invokes the exact delegate parameter: the compiler target is
    /// a delegate-invoke method and the receiver unwraps to the exact parameter reference. A
    /// delegate-typed variable or field invocation never matches because its receiver is not the
    /// parameter reference.
    /// </summary>
    private static bool IsDelegateParameterInvoke(IInvocationOperation invocation, IParameterSymbol parameter)
        => invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke
            && invocation.Instance is { } instance
            && UnwrapImplicitConversions(instance) is IParameterReferenceOperation reference
            && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);

    /// <summary>
    /// True when the parameter reference is the direct receiver of a delegate-invoke invocation, so
    /// the reference is the counted contract invoke rather than an escape. The receiver may be
    /// wrapped in implicit conversions (for example delegate variance).
    /// </summary>
    private static bool IsDelegateInvokeInstance(IParameterReferenceOperation reference)
    {
        IOperation? current = reference;
        while (current.Parent is IConversionOperation { IsImplicit: true })
        {
            current = current.Parent;
        }

        return current.Parent is IInvocationOperation invocation
            && invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke
            && ReferenceEquals(UnwrapImplicitConversions(invocation.Instance!), reference);
    }

    /// <summary>
    /// Resolves the callback argument after implicit conversions and delegate creation. An exact
    /// anonymous function anchors its target body operation; an exact method reference to a source
    /// local function or ordinary method anchors the exact target method. Every other shape (delegate
    /// variable, event, dynamic, unsupported conversion, metadata-only target) fails closed.
    /// </summary>
    private static bool TryResolveTarget(
        IOperation value,
        out CallbackTargetKind targetKind,
        out IMethodSymbol? targetMethod,
        out IOperation? targetBodyOperation)
    {
        targetKind = CallbackTargetKind.Unknown;
        targetMethod = null;
        targetBodyOperation = null;
        switch (UnwrapImplicitConversions(value))
        {
            case IAnonymousFunctionOperation anonymous:
                targetKind = CallbackTargetKind.AnonymousFunction;
                targetBodyOperation = anonymous;
                return true;
            case IDelegateCreationOperation creation:
                return TryResolveDelegateTarget(
                    creation.Target,
                    out targetKind,
                    out targetMethod,
                    out targetBodyOperation);
            case IMethodReferenceOperation methodReference:
                return TryResolveMethodReference(
                    methodReference,
                    out targetKind,
                    out targetMethod,
                    out targetBodyOperation);
            default:
                return false;
        }
    }

    private static bool TryResolveDelegateTarget(
        IOperation? target,
        out CallbackTargetKind targetKind,
        out IMethodSymbol? targetMethod,
        out IOperation? targetBodyOperation)
    {
        targetKind = CallbackTargetKind.Unknown;
        targetMethod = null;
        targetBodyOperation = null;
        switch (target)
        {
            case IAnonymousFunctionOperation anonymous:
                targetKind = CallbackTargetKind.AnonymousFunction;
                targetBodyOperation = anonymous;
                return true;
            case IMethodReferenceOperation methodReference:
                return TryResolveMethodReference(
                    methodReference,
                    out targetKind,
                    out targetMethod,
                    out targetBodyOperation);
            default:
                return false;
        }
    }

    /// <summary>
    /// Resolves a compiler method reference to an exact direct source target. A local function is
    /// always direct; an ordinary source method is admitted only when the compiler proves the
    /// dispatch is direct (static, non-virtual/non-abstract instance, sealed, or a sealed containing
    /// type). Interface methods, abstract methods, and virtual/override dispatch that is not sealed
    /// or proven exact are rejected without inferring a runtime receiver type or selecting the first
    /// candidate (regression). Metadata-only methods fail closed.
    /// </summary>
    private static bool TryResolveMethodReference(
        IMethodReferenceOperation methodReference,
        out CallbackTargetKind targetKind,
        out IMethodSymbol? targetMethod,
        out IOperation? targetBodyOperation)
    {
        targetKind = CallbackTargetKind.Unknown;
        targetMethod = null;
        targetBodyOperation = null;
        var method = methodReference.Method;
        if (method.MethodKind == MethodKind.LocalFunction)
        {
            targetKind = CallbackTargetKind.LocalFunction;
            targetMethod = method;
            return true;
        }

        if (method.MethodKind == MethodKind.Ordinary
            && method.Locations.Any(location => location.IsInSource)
            && IsDirectDispatchProven(method))
        {
            targetKind = CallbackTargetKind.MethodGroup;
            targetMethod = method;
            return true;
        }

        return false;
    }

    /// <summary>
    /// True only when the compiler proves the call to <paramref name="method"/> is direct, so the
    /// exact source body of the declaration is guaranteed to execute. Static methods (including
    /// extension methods), constructors, local functions, non-virtual/non-abstract instance methods,
    /// sealed methods, and methods whose containing type is sealed are direct. Interface methods,
    /// abstract methods, and virtual/override dispatch that is not sealed are dispatchable and are
    /// rejected without inferring a runtime receiver type or selecting the first candidate
    /// (regression).
    /// </summary>
    private static bool IsDirectDispatchProven(IMethodSymbol method)
    {
        if (method.MethodKind is MethodKind.LocalFunction or MethodKind.Constructor or MethodKind.StaticConstructor)
        {
            return true;
        }

        if (method.ContainingType is { TypeKind: TypeKind.Interface })
        {
            // Interface dispatch is never direct without a proven runtime receiver type.
            return false;
        }

        if (method.IsStatic)
        {
            return true;
        }

        if (method.IsSealed)
        {
            // A sealed method (for example a sealed override) is the final dispatch target.
            return true;
        }

        if (method.ContainingType is { IsSealed: true })
        {
            // A sealed containing type cannot be derived, so instance dispatch is exact.
            return true;
        }

        if (method.IsAbstract || method.IsVirtual || method.IsOverride)
        {
            return false;
        }

        // A non-virtual, non-abstract, non-override instance method cannot be re-bound at runtime.
        return true;
    }

    /// <summary>
    /// Resolves the exact compiler target symbol of a callback target body so completion analysis
    /// can reject async targets (<see cref="IMethodSymbol.IsAsync"/>) without inferring outer
    /// termination. An anonymous function exposes its synthesized method symbol; local-function and
    /// method-group targets carry their exact source method symbol.
    /// </summary>
    private static IMethodSymbol? ResolveTargetSymbol(
        CallbackTargetKind targetKind,
        IMethodSymbol? targetMethod,
        IOperation targetBody)
        => targetKind switch
        {
            CallbackTargetKind.AnonymousFunction => (targetBody as IAnonymousFunctionOperation)?.Symbol,
            CallbackTargetKind.LocalFunction or CallbackTargetKind.MethodGroup => targetMethod,
            _ => null,
        };

    /// <summary>
    /// Analyzes callback-local completion from the exact target body and symbol. An async target
    /// (<see cref="IMethodSymbol.IsAsync"/>), an await, a throw, a try/catch/finally, a catch
    /// clause, using/lock, yield, or an invalid/unsupported operation anywhere in the body stays
    /// <see cref="CallbackCompletionKind.Unknown"/>; only a bounded synchronous body without these
    /// unsupported boundaries may <see cref="CallbackCompletionKind.RejoinsCaller"/> because a
    /// callback-local return is a return from the callback itself and the outer caller continues.
    /// The outer scenario is never terminated by inference (regression).
    /// </summary>
    private static CallbackCompletionKind AnalyzeCompletion(
        IOperation targetBody,
        IMethodSymbol? targetSymbol,
        CancellationToken cancellationToken)
    {
        if (targetSymbol is { IsAsync: true })
        {
            // An async target defers completion to an unsupported state machine; the outer caller
            // must never be inferred to rejoin.
            return CallbackCompletionKind.Unknown;
        }

        foreach (var operation in EnumerateOperations(targetBody, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (operation)
            {
                case IAwaitOperation:
                case IThrowOperation:
                case ITryOperation:
                case ICatchClauseOperation:
                case IUsingOperation:
                case ILockOperation:
                case IInvalidOperation:
                    return CallbackCompletionKind.Unknown;
                case IReturnOperation returnOperation
                    when returnOperation.Kind is OperationKind.YieldReturn or OperationKind.YieldBreak:
                    return CallbackCompletionKind.Unknown;
            }
        }

        return CallbackCompletionKind.RejoinsCaller;
    }

    /// <summary>
    /// Collects the canonical, path-independent member-operation identities of the callback body:
    /// source-backed invocation, assignment/mutation, and return/throw operations. Anonymous-function
    /// and local-function operations anchor to the caller/enclosing method with span-based companion
    /// identities because those bodies are not accepted Method Flows; an ordinary source method-group
    /// target instead reuses the accepted operation identities of its extracted target body
    /// (<paramref name="acceptedTargetContext"/>), so a member the accepted traversal never flattened
    /// is never claimed (regression). Literals and captured values never serialize. An anonymous/local
    /// body still carries its body anchor so the boundary's member set is never empty; a method-group
    /// body with no accepted members carries an empty member set rather than a companion anchor.
    /// </summary>
    private ImmutableArray<string> CollectMemberOperations(
        IOperation targetBody,
        MethodId ownerMethodId,
        RegisteredMethodContext? acceptedTargetContext,
        CancellationToken cancellationToken)
    {
        var members = new List<string>();
        foreach (var operation in EnumerateOperations(targetBody, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSourceBacked(operation))
            {
                continue;
            }

            var kind = operation switch
            {
                IInvocationOperation => "Invocation",
                ISimpleAssignmentOperation => "Assignment",
                ICompoundAssignmentOperation => "CompoundAssignment",
                IIncrementOrDecrementOperation => "IncrementOrDecrement",
                IEventAssignmentOperation => "EventAssignment",
                IReturnOperation => "Return",
                IThrowOperation => "Throw",
                _ => null,
            };
            if (kind is null)
            {
                continue;
            }

            if (acceptedTargetContext is not null)
            {
                // Method-group targets must reuse the exact accepted operation identities of the
                // target body; an operation the accepted traversal never flattened is never claimed
                // as a member (regression).
                if (TryResolveAcceptedOperationId(
                        operation,
                        acceptedTargetContext.MethodId,
                        acceptedTargetContext.OperationById,
                        acceptedTargetContext.SpanIndex,
                        out var acceptedMemberId))
                {
                    members.Add(acceptedMemberId.Value);
                }
            }
            else
            {
                members.Add(CreateOperationId(operation, ownerMethodId, kind).Value);
            }
        }

        var canonical = members
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        if (!canonical.IsEmpty)
        {
            return canonical;
        }

        if (acceptedTargetContext is not null)
        {
            // An ordinary source method-group target whose accepted body exposes no flattenable
            // member operations carries no authoritative member set; a companion anchor would falsely
            // claim compatibility with an accepted body.
            return [];
        }

        var anchorKind = targetBody is IAnonymousFunctionOperation ? "AnonymousFunction" : "Body";
        return ImmutableArray.Create(CreateOperationId(targetBody, ownerMethodId, anchorKind).Value);
    }

    /// <summary>
    /// Creates the stable method identity of an exact callback target or contract method. Ordinary
    /// methods use the standard Program Index descriptor so method-group targets join the accepted
    /// index; local functions are not Program Index methods, so the exact enclosing method identity
    /// is embedded in the containing metadata name to prevent same-named local functions of one
    /// containing type from collapsing onto one anchor.
    /// </summary>
    private MethodId CreateMethodId(IMethodSymbol method, MethodId enclosingMethodId, StableProjectId project)
    {
        var projectId = RoslynProgramIndexExtractor.ResolveProject(method, project, _projectsByAssembly!);
        var descriptor = RoslynProgramIndexExtractor.CreateMethodDescriptor(method, projectId);
        if (method.MethodKind != MethodKind.LocalFunction)
        {
            return StableIdentity.CreateMethodId(descriptor);
        }

        return StableIdentity.CreateMethodId(new SymbolIdentityDescriptor(
            descriptor.Project,
            descriptor.AssemblyIdentity,
            $"{descriptor.ContainingMetadataName}<{enclosingMethodId.Value}>",
            descriptor.Kind,
            descriptor.MetadataName,
            descriptor.GenericArity,
            descriptor.ExplicitInterfaceIdentity,
            descriptor.Parameters,
            descriptor.ReturnType,
            descriptor.IncludeReturnTypeInIdentity));
    }

    private OperationId CreateOperationId(IOperation operation, MethodId methodId, string kind)
        => _operationIdFactory!(operation, methodId, kind, 0, 0, 0);

    private ImmutableArray<EvidenceRef> ResolveEvidence(IOperation operation, ImmutableArray<EvidenceRef> methodEvidence)
        => _resolveEvidenceFactory!(operation, methodEvidence);

    /// <summary>
    /// Combines source invocation, argument, and target-body evidence into one canonical,
    /// deterministic union ordered by evidence identity. Duplicate evidence identities collapse so
    /// certainty never exceeds the weakest contributor.
    /// </summary>
    private static ImmutableArray<EvidenceRef> CombineEvidence(params ImmutableArray<EvidenceRef>[] sources)
        => sources
            .SelectMany(source => source)
            .Where(item => item is not null)
            .DistinctBy(item => item.Id.Value, StringComparer.Ordinal)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();

    private static CallbackBoundaryFact ProjectBoundary(CompilationProfileId profileId, BoundaryDraft draft)
    {
        var canonicalMembers = string.Join("|", draft.MemberOperations);
        var id = StableIdentity.CreateCallbackBoundaryId(new CallbackBoundaryIdentityDescriptor(
            profileId,
            draft.CallerMethod,
            draft.OuterInvocationOperation,
            draft.ParameterOrdinal,
            draft.TargetKind,
            draft.TargetMethod,
            draft.TargetBodyOperation,
            draft.ContractMethod,
            draft.ContractInvokeOperation,
            draft.Cardinality,
            draft.Trigger,
            draft.TriggerCondition,
            draft.Completion,
            draft.ContractProvenance,
            canonicalMembers));
        return new CallbackBoundaryFact(
            id,
            draft.CallerMethod,
            draft.OuterInvocationOperation,
            draft.ParameterOrdinal,
            draft.TargetKind,
            draft.TargetMethod,
            draft.TargetBodyOperation,
            draft.ContractMethod,
            draft.ContractInvokeOperation,
            draft.Cardinality,
            draft.Trigger,
            draft.TriggerCondition,
            draft.Completion,
            draft.ContractProvenance,
            draft.MemberOperations,
            draft.Evidence,
            draft.Certainty);
    }

    /// <summary>
    /// Builds the deterministic, path-free debug projection: schema/producer/profile/fingerprint
    /// headers followed by one canonical line per boundary carrying only ids, enums, and ordinals.
    /// Physical paths, traversal order, timestamps, and raw captured values never appear.
    /// </summary>
    private static string BuildDebugProjection(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<CallbackBoundaryFact> boundaries,
        int diagnosticCount)
    {
        var lines = new List<(string Id, string Line)>();
        foreach (var boundary in boundaries)
        {
            lines.Add((
                boundary.Id.Value,
                $"boundary {boundary.Id.Value} caller={boundary.CallerMethod.Value} outer={boundary.OuterInvocationOperation.Value} ordinal={boundary.ParameterOrdinal.ToString(CultureInfo.InvariantCulture)} target={boundary.TargetKind.ToString()} targetMethod={boundary.TargetMethod?.Value ?? "-"} targetBody={boundary.TargetBodyOperation?.Value ?? "-"} contract={boundary.ContractMethod?.Value ?? "-"} invoke={boundary.ContractInvokeOperation?.Value ?? "-"} cardinality={boundary.Cardinality.ToString()} trigger={boundary.Trigger.ToString()} condition={boundary.TriggerCondition?.Value ?? "-"} completion={boundary.Completion.ToString()} provenance={boundary.ContractProvenance.ToString()} members={boundary.MemberOperations.Length.ToString(CultureInfo.InvariantCulture)} certainty={boundary.Certainty.ToString()}"));
        }

        var builder = new StringBuilder();
        builder.Append("callback-boundary:v1").Append('\n');
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

    private static IEnumerable<IOperation> EnumerateTopLevelOperations(IOperation root, CancellationToken cancellationToken)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.TryPop(out var operation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return operation;
            if (operation is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                continue;
            }

            foreach (var child in operation.ChildOperations)
            {
                pending.Push(child);
            }
        }
    }

    private static IEnumerable<IOperation> EnumerateOperations(IOperation root, CancellationToken cancellationToken)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.TryPop(out var operation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return operation;
            foreach (var child in operation.ChildOperations)
            {
                pending.Push(child);
            }
        }
    }

    private static IEnumerable<IOperation> EnumerateAncestors(IOperation operation)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            yield return current;
        }
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

    private static bool IsSourceBacked(IOperation operation)
        => operation.Syntax is not null
            && operation.Syntax.Span.Length > 0
            && operation.Syntax.SpanStart >= 0;

    private sealed record ContractAnalysis(
        CallbackContractProvenance Provenance,
        MethodId? ContractMethod,
        OperationId? ContractInvokeOperation,
        CallbackCardinality Cardinality,
        CallbackTriggerKind Trigger,
        OperationId? TriggerCondition)
    {
        public static ContractAnalysis Unbounded { get; } = new(
            CallbackContractProvenance.Unknown,
            null,
            null,
            CallbackCardinality.Unknown,
            CallbackTriggerKind.Unknown,
            null);

        public static ContractAnalysis Exact(
            MethodId contractMethod,
            OperationId invokeOperation,
            CallbackCardinality cardinality,
            CallbackTriggerKind trigger,
            OperationId? triggerCondition) =>
            new(
                CallbackContractProvenance.SourceBody,
                contractMethod,
                invokeOperation,
                cardinality,
                trigger,
                triggerCondition);
    }

    /// <summary>
    /// One extracted source method registered by <see cref="AddMethod"/> with its authoritative
    /// operation map and stable source-key index from the accepted behavior extraction. The body
    /// operation, evidence, and operation identities are exactly the ones the accepted extractor
    /// produced, so deferred callback analysis reuses them byte-for-byte (regression).
    /// </summary>
    private sealed record RegisteredMethodContext(
        StableProjectId Project,
        MethodId MethodId,
        IMethodBodyOperation BodyOperation,
        ImmutableArray<EvidenceRef> Evidence,
        IReadOnlyDictionary<IOperation, OperationId> OperationById,
        IReadOnlyDictionary<OperationSourceKey, OperationId> SpanIndex);

    /// <summary>
    /// Deterministic, path-free source key that binds a body-tree operation instance to its exact
    /// accepted flattened operation identity: the method id, the document identity (SyntaxTree), the
    /// source span, and the Roslyn operation kind. The key never contains a physical path,
    /// timestamp, or traversal ordinal.
    /// </summary>
    private sealed record OperationSourceKey(
        MethodId Method,
        SyntaxTree Tree,
        int Start,
        int Length,
        string Kind);

    private sealed record BoundaryDraft(
        MethodId CallerMethod,
        OperationId OuterInvocationOperation,
        int ParameterOrdinal,
        CallbackTargetKind TargetKind,
        MethodId? TargetMethod,
        OperationId? TargetBodyOperation,
        MethodId? ContractMethod,
        OperationId? ContractInvokeOperation,
        CallbackCardinality Cardinality,
        CallbackTriggerKind Trigger,
        OperationId? TriggerCondition,
        CallbackCompletionKind Completion,
        CallbackContractProvenance ContractProvenance,
        ImmutableArray<string> MemberOperations,
        ImmutableArray<EvidenceRef> Evidence,
        CertaintyLevel Certainty);

    /// <summary>
    /// One anonymous/local callback target body collected while projecting boundaries. The owner
    /// method is the caller for an anonymous function and the exact local-function method for a
    /// local function, matching the member-set owner; the target body's source-backed descendant
    /// invocations become companion framework descriptors during <see cref="Build"/> (accepted contract).
    /// </summary>
    private sealed record CompanionTarget(
        IOperation TargetBody,
        MethodId OwnerMethodId,
        ImmutableArray<EvidenceRef> MethodEvidence,
        StableProjectId Project);
}
