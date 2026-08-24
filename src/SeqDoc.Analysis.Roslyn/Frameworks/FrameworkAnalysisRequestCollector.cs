using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;

namespace SeqDoc.Analysis.Roslyn.Frameworks;

/// <summary>
/// Accumulates Roslyn-neutral <see cref="OperationDescriptor"/> and <see cref="SymbolDescriptor"/>
/// inputs during one Roslyn compilation/extraction session. Builders deduplicate by stable identity
/// and emit canonical identity order so registration and encounter order never change the
/// framework-model request.
/// </summary>
internal sealed class FrameworkAnalysisRequestCollector
{
    private readonly List<OperationDescriptor> _operations = [];
    private readonly List<SymbolDescriptor> _symbols = [];

    /// <summary>
    /// The current project's <c>CoreWcfHostChainScanner</c> result, set once per project (mirroring the
    /// existing <c>SetAuthoritativeSymbols</c> per-project-state pattern other collectors already use in
    /// the same extraction pass) before that project's method bodies are extracted, and consulted at every
    /// <c>ProjectOperationDescriptor</c> call site so an <c>AddServiceEndpoint</c> invocation is projected
    /// with its exact host-chain proof (or the absence of one) without threading a new parameter through
    /// every intermediate extraction method. Defaults to an empty proof, so an unset collector never
    /// claims a chain is proven.
    /// </summary>
    public ImmutableDictionary<SyntaxNode, ImmutableArray<EvidenceRef>> HostChainProof { get; private set; }
        = ImmutableDictionary<SyntaxNode, ImmutableArray<EvidenceRef>>.Empty;

    public void SetHostChainProof(ImmutableDictionary<SyntaxNode, ImmutableArray<EvidenceRef>> proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        HostChainProof = proof;
    }

    public void AddOperation(OperationDescriptor operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _operations.Add(operation);
    }

    public void AddSymbol(SymbolDescriptor symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        _symbols.Add(symbol);
    }

    public ImmutableArray<OperationDescriptor> BuildOperations() => _operations
        .DistinctBy(operation => operation.Id.Value)
        .OrderBy(operation => operation.Id.Value, StringComparer.Ordinal)
        .ToImmutableArray();

    public ImmutableArray<SymbolDescriptor> BuildSymbols() => _symbols
        .DistinctBy(symbol => symbol.Id.Value)
        .OrderBy(symbol => symbol.Id.Value, StringComparer.Ordinal)
        .ToImmutableArray();
}
