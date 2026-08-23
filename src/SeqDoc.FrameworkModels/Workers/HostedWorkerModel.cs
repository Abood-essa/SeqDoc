using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.FrameworkModels.Workers;

/// <summary>
/// Recognizes the exact Microsoft.Extensions.Hosting hosted-service contract and BackgroundService
/// lifecycle surface. It emits only source-backed lifecycle facts; inherited framework bodies remain
/// absent and are never reconstructed from method names.
/// </summary>
public sealed class HostedWorkerModel : IFrameworkBehaviorModel
{
    public const string ModelIdValue = "seqdoc.microsoft.extensions.hosted-worker";
    public const string ModelVersionValue = "1.0.0";

    private const string HostingAssembly = "Microsoft.Extensions.Hosting.Abstractions";
    private const string HostingRegistrationType = "Microsoft.Extensions.DependencyInjection.ServiceCollectionHostedServiceExtensions";
    private const string ServiceCollectionType = "Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string HostedServiceType = "Microsoft.Extensions.Hosting.IHostedService";
    private const string BackgroundServiceType = "Microsoft.Extensions.Hosting.BackgroundService";
    private const string CancellationTokenType = "System.Threading.CancellationToken";
    private const string TaskType = "System.Threading.Tasks.Task";

    public FrameworkModelDescriptor Descriptor { get; } = new(
        ModelIdValue,
        ModelVersionValue,
        "Microsoft.Extensions.Hosting hosted workers",
        120);

    public bool IsApplicable(FrameworkDetectionContext context)
        => context.ProgramIndex.References.Any(reference =>
            reference.Identity == HostingAssembly);

    public ValueTask<ModelResult> AnalyzeOperationAsync(
        OperationDescriptor operation,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        if (!IsHostedServiceRegistration(operation.TargetIdentity)
            || operation.ConstructedType is not { } hostedType
            || operation.ConstructedTypeSymbol is not { } hostedTypeSymbol)
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        var certainty = operation.Evidence.Max(evidence => evidence.Certainty);
        var evidence = CreateModelEvidence(
            $"hosted-registration:{operation.Id.Value}",
            operation.Evidence,
            certainty);
        var fact = new HostedWorkerRegistrationFact
        {
            Id = StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
                context.Profile.Id,
                Descriptor.ModelId,
                Descriptor.Version,
                "hosted-worker-registration",
                new OperationBehaviorFactAnchor(operation.Method, operation.Id),
                0)),
            HostedType = hostedTypeSymbol,
            RegistrationMethod = operation.Method,
            RegistrationOperation = operation.Id,
            Evidence = evidence,
            Certainty = certainty,
        };
        return ValueTask.FromResult(new ModelResult(true, facts: [fact]));
    }

    public ValueTask<ModelResult> AnalyzeSymbolAsync(
        SymbolDescriptor symbol,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(context);

        if (symbol.MethodShape is not { } shape
            || !IsHostedType(shape.DeclaringType)
            || context.ProgramIndex.Methods.FirstOrDefault(method => method.Symbol == symbol.Id) is not { } current)
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        var methods = context.ProgramIndex.Methods
            .Where(method => method.ContainingType == shape.DeclaringTypeSymbol)
            .ToImmutableArray();
        var isBackgroundService = HasBaseType(shape.DeclaringType, BackgroundServiceType);
        var start = isBackgroundService ? null : methods.FirstOrDefault(IsStartMethod);
        var execute = isBackgroundService ? methods.FirstOrDefault(IsExecuteMethod) : null;
        var stop = isBackgroundService ? null : methods.FirstOrDefault(IsStopMethod);
        var root = start ?? execute ?? stop;
        if (root is null || root.Id != current.Id)
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        var underlying = methods
            .Where(method => method.Id == start?.Id || method.Id == execute?.Id || method.Id == stop?.Id)
            .SelectMany(method => method.Evidence)
            .Concat(shape.DeclaringType is null ? [] : context.ProgramIndex.Types
                .Where(type => type.Id == shape.DeclaringTypeSymbol)
                .SelectMany(type => type.Evidence))
            .DistinctBy(evidence => evidence.Id.Value)
            .ToImmutableArray();
        if (underlying.IsDefaultOrEmpty)
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }

        var certainty = underlying.Max(evidence => evidence.Certainty);
        var hostedType = context.ProgramIndex.Types.FirstOrDefault(type => type.Id == shape.DeclaringTypeSymbol);
        if (hostedType is null || string.IsNullOrWhiteSpace(hostedType.MetadataName))
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }
        var evidence = CreateModelEvidence(
            $"hosted-worker:{shape.DeclaringTypeSymbol.Value}",
            underlying,
            certainty);
        var fact = new HostedWorkerLifecycleFact
        {
            Id = StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
                context.Profile.Id,
                Descriptor.ModelId,
                Descriptor.Version,
                "hosted-worker-lifecycle",
                new SymbolBehaviorFactAnchor(hostedType.Project, shape.DeclaringTypeSymbol),
                0)),
            EntryPointId = StableIdentity.CreateHostedWorkerEntryPointId(
                new HostedWorkerEntryPointIdentityDescriptor(context.Profile.Id, shape.DeclaringTypeSymbol, root.Id)),
            RootMethod = root.Id,
            HostedType = shape.DeclaringTypeSymbol,
            HostedTypeName = hostedType.MetadataName,
            StartMethod = start?.Id,
            ExecuteMethod = execute?.Id,
            StopMethod = stop?.Id,
            IsBackgroundService = isBackgroundService,
            CancellationParameterName = (execute ?? start ?? stop)?.Parameters
                .FirstOrDefault(parameter => parameter.FullyQualifiedType == CancellationTokenType)?.Name,
            Evidence = evidence,
            Certainty = certainty,
        };
        return ValueTask.FromResult(new ModelResult(true, facts: [fact]));
    }

    private static bool IsHostedType(FrameworkTypeShape type)
        => type.Interfaces.Any(IsHostedService) || HasBaseType(type, BackgroundServiceType);

    private static bool IsHostedService(FrameworkTypeIdentity type)
        => type.AssemblyIdentity == HostingAssembly
            && type.MetadataName == HostedServiceType;

    private static bool HasBaseType(FrameworkTypeShape? type, string metadataName)
        => type is not null && type.BaseTypeChain.Any(baseType =>
            baseType.AssemblyIdentity == HostingAssembly
            && baseType.MetadataName == metadataName);

    private static bool IsStartMethod(ProgramMethod method) => IsLifecycleMethod(method, "StartAsync");
    private static bool IsExecuteMethod(ProgramMethod method) => IsLifecycleMethod(method, "ExecuteAsync");
    private static bool IsStopMethod(ProgramMethod method) => IsLifecycleMethod(method, "StopAsync");

    private static bool IsHostedServiceRegistration(FrameworkMethodIdentity? identity)
        => identity is not null
            && identity.AssemblyIdentity == HostingAssembly
            && identity.ContainingMetadataType == HostingRegistrationType
            && identity.MethodMetadataName == "AddHostedService"
            && identity.GenericArity == 1
            && identity.Parameters.Length == 1
            && identity.Parameters[0] == new ParameterIdentityDescriptor(ParameterRefKind.None, ServiceCollectionType)
            && identity.ReturnType == ServiceCollectionType;

    private static bool IsLifecycleMethod(ProgramMethod method, string name)
        => method.Name == name
            && method.ReturnType == TaskType
            && method.Parameters.Length == 1
            && method.Parameters[0].RefKind == ParameterRefKind.None
            && method.Parameters[0].FullyQualifiedType == CancellationTokenType;

    private ImmutableArray<EvidenceRef> CreateModelEvidence(
        string artifact,
        ImmutableArray<EvidenceRef> underlying,
        CertaintyLevel certainty)
    {
        var id = StableIdentity.CreateEvidenceIdV2(new EvidenceIdentityDescriptor(
            EvidenceKind.FrameworkModel,
            $"{Descriptor.ModelId}:{Descriptor.Version}",
            null,
            null,
            null,
            null,
            certainty,
            Descriptor.ModelId,
            Descriptor.Version,
            artifact));
        return [new EvidenceRef(
            id,
            EvidenceKind.FrameworkModel,
            $"{Descriptor.ModelId}:{Descriptor.Version}",
            null,
            null,
            null,
            certainty,
            underlying,
            Descriptor.ModelId,
            Descriptor.Version)];
    }
}
