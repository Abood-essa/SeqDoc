using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;

namespace SeqDoc.FrameworkModels.Workers;

/// <summary>Recognizes the exact four-argument System.Threading.Timer callback registration.</summary>
public sealed class SchedulerModel : IFrameworkBehaviorModel
{
    public const string ModelIdValue = "seqdoc.system.threading.timer";
    public const string ModelVersionValue = "1.0.0";

    public FrameworkModelDescriptor Descriptor { get; } = new(
        ModelIdValue,
        ModelVersionValue,
        "System.Threading.Timer scheduler",
        121);

    public bool IsApplicable(FrameworkDetectionContext context) => true;

    public ValueTask<ModelResult> AnalyzeSymbolAsync(
        SymbolDescriptor symbol,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ModelResult.Unrecognized);
    }

    public ValueTask<ModelResult> AnalyzeOperationAsync(
        OperationDescriptor operation,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsTimerConstructor(operation.TargetIdentity)
            || operation.CallbackTarget is not { Kind: CallbackTargetKind.MethodGroup, TargetMethod: { } job })
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        var callback = context.ProgramIndex.Methods.FirstOrDefault(method => method.Id == job);
        if (callback is null || operation.Evidence.IsDefaultOrEmpty)
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        var underlying = operation.Evidence.Concat(callback.Evidence)
            .DistinctBy(evidence => evidence.Id.Value)
            .ToImmutableArray();
        var certainty = underlying.Min(evidence => evidence.Certainty);
        var evidenceId = StableIdentity.CreateEvidenceIdV2(new EvidenceIdentityDescriptor(
            EvidenceKind.FrameworkModel,
            $"{Descriptor.ModelId}:{Descriptor.Version}",
            null,
            null,
            null,
            null,
            certainty,
            Descriptor.ModelId,
            Descriptor.Version,
            operation.Id.Value));
        var evidence = ImmutableArray.Create(new EvidenceRef(
            evidenceId,
            EvidenceKind.FrameworkModel,
            $"{Descriptor.ModelId}:{Descriptor.Version}",
            null,
            null,
            operation.Id.Value,
            certainty,
            underlying,
            Descriptor.ModelId,
            Descriptor.Version));
        var fact = new SchedulerJobFact
        {
            Id = StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
                context.Profile.Id,
                Descriptor.ModelId,
                Descriptor.Version,
                "timer-job",
                new OperationBehaviorFactAnchor(operation.Method, operation.Id),
                0)),
            Scheduler = SchedulerKind.Timer,
            RegistrationMethod = operation.Method,
            RegistrationOperation = operation.Id,
            JobMethod = job,
            SourceStart = operation.SourceStart,
            CallbackTypeName = operation.TargetIdentity!.Parameters[0].FullyQualifiedType,
            Evidence = evidence,
            Certainty = certainty,
        };
        return ValueTask.FromResult(new ModelResult(true, facts: [fact]));
    }

    private static bool IsTimerConstructor(FrameworkMethodIdentity? identity)
        => identity is not null
            && identity.AssemblyIdentity == "System.Threading"
            && identity.ContainingMetadataType == "System.Threading.Timer"
            && identity.MethodMetadataName == ".ctor"
            && identity.GenericArity == 0
            && identity.Parameters.Length == 4
            && identity.Parameters[0] == new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Threading.TimerCallback")
            && identity.Parameters[1] == new ParameterIdentityDescriptor(ParameterRefKind.None, "System.Object")
            && identity.Parameters[2] == new ParameterIdentityDescriptor(ParameterRefKind.None, "System.TimeSpan")
            && identity.Parameters[3] == new ParameterIdentityDescriptor(ParameterRefKind.None, "System.TimeSpan")
            && (identity.ReturnType is null or "System.Void");
}
