using System.Collections.Immutable;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Identity;

namespace SeqDoc.Analysis.Behavior;

/// <summary>Verifies deterministic ordering and structural integrity of an extracted behavior input.</summary>
public static class ExtractionValidator
{
    /// <summary>Returns deterministic diagnostics for an extracted behavior input, or empty when valid.</summary>
    public static ImmutableArray<AnalysisDiagnostic> Validate(ExtractedBehaviorInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        var methods = new HashSet<MethodId>();
        for (var methodOrdinal = 0; methodOrdinal < input.Methods.Length; methodOrdinal++)
        {
            var body = input.Methods[methodOrdinal];
            if (!methods.Add(body.Method))
            {
                diagnostics.Add(CreateDiagnostic(
                    "BD1001",
                    "A method body appears more than once in the extraction.",
                    body.Method.Value,
                    methodOrdinal));
            }

            ValidateBody(body, methodOrdinal, diagnostics);
        }

        for (var index = 1; index < input.Methods.Length; index++)
        {
            if (string.Compare(
                    input.Methods[index - 1].Method.Value,
                    input.Methods[index].Method.Value,
                    StringComparison.Ordinal) >= 0)
            {
                diagnostics.Add(CreateDiagnostic(
                    "BD1002",
                    "Extracted methods are not in canonical MethodId order.",
                    input.Profile.Id.Value,
                    index));
                break;
            }
        }

        return diagnostics.ToImmutable();
    }

    private static void ValidateBody(
        ExtractedMethodBody body,
        int methodOrdinal,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics)
    {
        if (string.IsNullOrWhiteSpace(body.BodyFingerprint))
        {
            diagnostics.Add(CreateDiagnostic(
                "BD1003",
                "An extracted method body has no fingerprint.",
                body.Method.Value,
                methodOrdinal));
        }

        var operationsById = new Dictionary<OperationId, ExtractedOperation>();
        for (var ordinal = 0; ordinal < body.Operations.Length; ordinal++)
        {
            var operation = body.Operations[ordinal];
            if (operation.EvaluationOrdinal != ordinal)
            {
                diagnostics.Add(CreateDiagnostic(
                    "BD1004",
                    "Operation evaluation ordinals are not contiguous from zero.",
                    body.Method.Value,
                    ordinal));
            }

            if (!operationsById.TryAdd(operation.Id, operation))
            {
                diagnostics.Add(CreateDiagnostic(
                    "BD1005",
                    "An operation ID appears more than once in one method body.",
                    body.Method.Value,
                    ordinal));
            }
        }

        var blocksById = new Dictionary<int, ExtractedBasicBlock>();
        for (var ordinal = 0; ordinal < body.Blocks.Length; ordinal++)
        {
            var block = body.Blocks[ordinal];
            if (block.Ordinal != ordinal)
            {
                diagnostics.Add(CreateDiagnostic(
                    "BD1006",
                    "Block ordinals are not contiguous from zero.",
                    body.Method.Value,
                    ordinal));
            }

            blocksById.TryAdd(block.Ordinal, block);
            if (block.BranchCondition is not null && !operationsById.ContainsKey(block.BranchCondition.Value))
            {
                diagnostics.Add(CreateDiagnostic(
                    "BD1007",
                    "A block branch condition references an unknown operation.",
                    body.Method.Value,
                    ordinal));
            }

            foreach (var operationId in block.Operations)
            {
                if (!operationsById.ContainsKey(operationId))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "BD1008",
                        "A block references an operation outside its own body.",
                        body.Method.Value,
                        ordinal));
                }
            }
        }

        for (var ordinal = 0; ordinal < body.Blocks.Length; ordinal++)
        {
            var block = body.Blocks[ordinal];
            if (block.FallThroughSuccessor is { } fallThrough && !blocksById.ContainsKey(fallThrough))
            {
                diagnostics.Add(CreateDiagnostic(
                    "BD1009",
                    "A block fall-through successor references an unknown block.",
                    body.Method.Value,
                    ordinal));
            }

            foreach (var successor in block.ConditionalSuccessors)
            {
                if (!blocksById.ContainsKey(successor))
                {
                    diagnostics.Add(CreateDiagnostic(
                        "BD1010",
                        "A conditional successor references an unknown block.",
                        body.Method.Value,
                        ordinal));
                }
            }
        }

        var regionIds = new HashSet<FlowRegionId>();
        for (var ordinal = 0; ordinal < body.Regions.Length; ordinal++)
        {
            var region = body.Regions[ordinal];
            if (region.Ordinal != ordinal)
            {
                diagnostics.Add(CreateDiagnostic(
                    "BD1011",
                    "Region ordinals are not contiguous from zero.",
                    body.Method.Value,
                    ordinal));
            }

            if (!regionIds.Add(region.Id))
            {
                diagnostics.Add(CreateDiagnostic(
                    "BD1012",
                    "A region ID appears more than once in one method body.",
                    body.Method.Value,
                    ordinal));
            }

            if (region.Parent is { } parent && !regionIds.Contains(parent))
            {
                diagnostics.Add(CreateDiagnostic(
                    "BD1013",
                    "A region parent must be declared before the region itself.",
                    body.Method.Value,
                    ordinal));
            }

            if (region.StartBlockOrdinal < 0
                || region.EndBlockOrdinal < region.StartBlockOrdinal
                || (region.Kind != ExtractedRegionKind.Root
                    && (region.StartBlockOrdinal >= body.Blocks.Length
                        || region.EndBlockOrdinal >= body.Blocks.Length)))
            {
                diagnostics.Add(CreateDiagnostic(
                    "BD1014",
                    "A region references an invalid block range.",
                    body.Method.Value,
                    ordinal));
            }
        }
    }

    private static AnalysisDiagnostic CreateDiagnostic(
        string code,
        string summary,
        string subjectId,
        int ordinal)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.BaselineIndex,
            null,
            subjectId,
            Math.Max(0, ordinal)));
        return new AnalysisDiagnostic(
            id,
            code,
            DiagnosticSeverity.Warning,
            AnalysisStage.BaselineIndex,
            summary,
            new DiagnosticLocation("behavior extraction", symbol: new SymbolId(subjectId)),
            $"The extracted behavior input violates invariant '{code}'.",
            "The extracted behavior facts are not trustworthy for this method.",
            "Reanalyze the target; if the problem persists, report the affected method identity.",
            Core.Evidence.CertaintyLevel.Exact,
            internalDetail: $"{code} at ordinal {ordinal}");
    }
}
