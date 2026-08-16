using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;

namespace SeqDoc.FrameworkModels.MediatR;

/// <summary>Exact MediatR 13 request/response adapter for compiler-proven ISender.Send calls.</summary>
public sealed class MediatRDispatchModel : IFrameworkBehaviorModel
{
    public const string ModelIdValue = "seqdoc.mediatr.dispatch";
    public const string ModelVersionValue = "1.0.0";
    public FrameworkModelDescriptor Descriptor { get; } = new(ModelIdValue, ModelVersionValue, "MediatR 13 dispatch", 220);

    public bool IsApplicable(FrameworkDetectionContext context)
        => context.ProgramIndex.References.Any(reference =>
            reference.Identity == "MediatR" && reference.Version == "13.0.0");

    public ValueTask<ModelResult> AnalyzeSymbolAsync(SymbolDescriptor symbol, FrameworkAnalysisContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(ModelResult.Unrecognized);

    public ValueTask<ModelResult> AnalyzeOperationAsync(OperationDescriptor operation, FrameworkAnalysisContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.DispatchShape is null)
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        var identity = operation.TargetIdentity;
        if (!IsExactIdentity(identity) || identity is null)
        {
            return ValueTask.FromResult(new ModelResult(false, diagnostics:
                [MediatRDispatchModelDiagnostics.UnsupportedShape(context.Profile.Id, operation.Id, "does not match the exact MediatR 13 Send signature")]));
        }

        var shape = operation.DispatchShape;
        var returnType = identity.ReturnType;
        var requestParameter = identity.Parameters[0].FullyQualifiedType;
        if (!shape.IsClosedConstructed
            || string.IsNullOrWhiteSpace(shape.RequestType) || string.IsNullOrWhiteSpace(shape.ResponseType)
            || string.IsNullOrWhiteSpace(shape.RequestContractType)
            || string.Equals(shape.RequestType, shape.RequestContractType, StringComparison.Ordinal)
            || identity!.Parameters.Length != 2
            || string.IsNullOrWhiteSpace(returnType)
            || !TryParseSingleGenericArgument(requestParameter, "MediatR.IRequest<", out var targetRequestResponse)
            || !TryParseSingleGenericArgument(returnType, "System.Threading.Tasks.Task<", out var targetTaskResponse)
            || !TryParseSingleGenericArgument(shape.RequestContractType, "MediatR.IRequest<", out var contractResponse)
            || targetRequestResponse != targetTaskResponse
            || targetRequestResponse != shape.ResponseType
            || contractResponse != shape.ResponseType
            || requestParameter != shape.RequestContractType
            || shape.Candidates.Any(candidate => candidate.Evidence.IsDefaultOrEmpty))
        {
            return ValueTask.FromResult(new ModelResult(false, diagnostics:
                [MediatRDispatchModelDiagnostics.UnsupportedShape(context.Profile.Id, operation.Id, "has incomplete request, response, or candidate evidence")]));
        }

        var resolution = shape.Candidates.Length switch
        {
            0 => DispatchResolution.Unresolved,
            1 => DispatchResolution.ExactSingle,
            _ => DispatchResolution.Ambiguous,
        };
        var candidates = shape.Candidates.Select(candidate => new DispatchCandidate(
            candidate.Method,
            candidate.DisplayName,
            candidate.BodyAvailable,
            candidate.Evidence,
            candidate.Certainty)).ToImmutableArray();
        var factEvidence = CreateEvidence(operation, context);
        var fact = new DispatchFact(
            StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
                context.Profile.Id, Descriptor.ModelId, Descriptor.Version, "mediatr-send",
                new OperationBehaviorFactAnchor(operation.Method, operation.Id), 0)),
            context.Profile.Id,
            context.ProgramIndex.IndexFingerprint,
            operation.Method,
            operation.Id,
            DispatchBoundaryKind.RequestResponse,
            DispatchCardinality.ExactlyOne,
            resolution,
            shape.RequestType,
            shape.ResponseType,
            candidates,
            DispatchPipelineMetadata.Unknown,
            factEvidence,
            operation.Certainty);
        return ValueTask.FromResult(new ModelResult(true, [fact]));
    }

    private static bool IsExactIdentity(FrameworkMethodIdentity? identity)
        => identity is not null
            && identity.AssemblyIdentity == "MediatR"
            && identity.AssemblyVersion == "13.0.0.0"
            && identity.ContainingMetadataType == "MediatR.ISender"
            && identity.MethodMetadataName == "Send"
            && identity.GenericArity == 1
            && identity.Parameters.Length == 2
            && identity.Parameters[0].FullyQualifiedType.StartsWith("MediatR.IRequest<", StringComparison.Ordinal)
            && identity.Parameters[1] == new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Threading.CancellationToken")
            && identity.ReturnType?.StartsWith("System.Threading.Tasks.Task<", StringComparison.Ordinal) == true;

    private static bool TryParseSingleGenericArgument(
        string value,
        string prefix,
        out string argument)
    {
        argument = string.Empty;
        if (!value.StartsWith(prefix, StringComparison.Ordinal)
            || !value.EndsWith('>')
            || value.Length <= prefix.Length + 1)
        {
            return false;
        }

        var inner = value[prefix.Length..^1];
        // The identity format is canonical. Reject nested generic arguments, commas, and whitespace
        // rather than attempting to recover a possibly ambiguous response type from display text.
        if (inner.Length == 0
            || inner.Any(character => character is '<' or '>' or ',')
            || inner.Any(char.IsWhiteSpace))
        {
            return false;
        }

        argument = inner;
        return true;
    }

    private ImmutableArray<EvidenceRef> CreateEvidence(OperationDescriptor operation, FrameworkAnalysisContext context)
    {
        var artifact = $"{Descriptor.ModelId}:{Descriptor.Version}";
        var id = StableIdentity.CreateEvidenceIdV2(new EvidenceIdentityDescriptor(
            EvidenceKind.FrameworkModel, artifact, null, null, null, null, operation.Certainty,
            Descriptor.ModelId, Descriptor.Version, operation.Id.Value));
        return [new EvidenceRef(id, EvidenceKind.FrameworkModel, artifact, null, null, operation.Id.Value,
            operation.Certainty, operation.Evidence, Descriptor.ModelId, Descriptor.Version)];
    }
}
