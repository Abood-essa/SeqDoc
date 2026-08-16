using System.Collections.Immutable;
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
