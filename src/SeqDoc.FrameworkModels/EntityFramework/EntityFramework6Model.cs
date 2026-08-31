using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.Semantics;

namespace SeqDoc.FrameworkModels.EntityFramework;

public sealed class EntityFramework6Model : IFrameworkBehaviorModel
{
    public const string ModelIdValue = "seqdoc.entityframework6";
    public const string ModelVersionValue = "1.0.0";
    private const string EfAssembly = "EntityFramework";
    private const string EfVersion = "6.0.0.0";
    private const string DbContext = "System.Data.Entity.DbContext";
    private const string DbSet = "System.Data.Entity.DbSet`1";
    private const string Queryable = "System.Linq.Queryable";
    private const string EfToken = "b77a5c561934e089";
    private const string QueryableToken = "b03f5f7f11d50a3a";

    public FrameworkModelDescriptor Descriptor { get; } = new(ModelIdValue, ModelVersionValue, "Entity Framework 6 and EDMX", 201);

    public bool IsApplicable(FrameworkDetectionContext context)
        => context.ProgramIndex.References.Any(reference => reference.Kind == ProgramReferenceKind.Package
            && string.Equals(reference.Identity, EfAssembly, StringComparison.Ordinal)
            && string.Equals(reference.Version, "6.4.4", StringComparison.Ordinal))
        && context.ProgramIndex.References.Any(reference => reference.Kind == ProgramReferenceKind.Assembly
            && string.Equals(reference.Identity, EfAssembly, StringComparison.Ordinal)
            && string.Equals(reference.Version, EfVersion, StringComparison.Ordinal));
    public ValueTask<ModelResult> AnalyzeSymbolAsync(SymbolDescriptor symbol, FrameworkAnalysisContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(ModelResult.Unrecognized);

    public ValueTask<ModelResult> AnalyzeOperationAsync(OperationDescriptor operation, FrameworkAnalysisContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Kind == "EdmxMetadata" && ValidEdmxMetadataArguments(operation.ConstantArguments))
        {
            var a = operation.ConstantArguments;
            var fact = new EntityFrameworkEdmxMetadataFact
            {
                Id = StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(context.Profile.Id, Descriptor.ModelId, Descriptor.Version, "edmx", new OperationBehaviorFactAnchor(operation.Method, operation.Id), 0)),
                Project = new ProjectId(a[0].Value!),
                RepositoryRelativePath = a[1].Value!,
                ContentFingerprint = a[2].Value!,
                HasFunctionImport = a[3].Value == "true",
                HasStoreFunction = a[4].Value == "true",
                Evidence = Evidence(operation, "edmx"),
                Certainty = operation.Certainty
            };
            return ValueTask.FromResult(new ModelResult(true, [fact]));
        }
        if (operation.TargetIdentity is not { } identity)
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }
        if (!string.Equals(operation.Kind, "Invocation", StringComparison.Ordinal))
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }
        var chain = operation.QueryChain;
        if (chain is not null && identity.AssemblyIdentity == Queryable && identity.AssemblyVersion == "9.0.0.0" && identity.AssemblyPublicKeyToken == QueryableToken && identity.ContainingMetadataType == Queryable && identity.GenericArity == 1)
        {
            var kind = identity.MethodMetadataName switch { "FirstOrDefault" => EntityFrameworkQueryOperatorKind.FirstOrDefault, "Count" => EntityFrameworkQueryOperatorKind.Count, _ => EntityFrameworkQueryOperatorKind.Unknown };
            if (kind != EntityFrameworkQueryOperatorKind.Unknown && ExactQueryable(identity, kind, chain)
                && (kind != EntityFrameworkQueryOperatorKind.FirstOrDefault
                    || operation.PredicateShape is { Kind: PredicateShapeKind.EqualityComparison, ComparisonOperation: not null }))
            {
                return ValueTask.FromResult(new ModelResult(true, [Query(context.Profile.Id, operation, chain, kind)]));
            }
        }
        var target = identity;
        if (target.AssemblyIdentity != EfAssembly || target.AssemblyVersion != EfVersion || target.AssemblyPublicKeyToken != EfToken)
        {
            return ValueTask.FromResult(ModelResult.Unrecognized);
        }
        if (chain is not null && target.ContainingMetadataType == DbSet && target.MethodMetadataName == "Add" && target.GenericArity == 0 && target.Parameters.Length == 1 && target.Parameters[0].RefKind == ParameterRefKind.None && target.Parameters[0].FullyQualifiedType == chain.EntityType)
        {
            return ValueTask.FromResult(new ModelResult(true, [Mutation(context.Profile.Id, operation, EntityFrameworkMutationKind.Add, chain.ContainingType, chain.EntityType)]));
        }

        if (chain is not null && target.ContainingMetadataType == DbContext && target.MethodMetadataName == "SaveChanges" && target.GenericArity == 0 && target.Parameters.IsEmpty && target.ReturnType == "System.Int32")
        {
            return ValueTask.FromResult(new ModelResult(true, [Mutation(context.Profile.Id, operation, EntityFrameworkMutationKind.SaveChanges, chain!.ContainingType, "")]));
        }

        return ValueTask.FromResult(ModelResult.Unrecognized);
    }

    private static bool ExactQueryable(FrameworkMethodIdentity i, EntityFrameworkQueryOperatorKind k, FrameworkQueryChainDescriptor c)
    {
        if (c.ReceiverType != $"System.Data.Entity.DbSet<{c.EntityType}>" || c.ContainingType.Length == 0)
        {
            return false;
        }

        if (c.Steps.Any(step => !ExactWhereStep(step.TargetIdentity, c.EntityType)))
        {
            return false;
        }

        if (i.Parameters.Length != (k == EntityFrameworkQueryOperatorKind.Count ? 1 : 2) || i.Parameters.Any(p => p.RefKind != ParameterRefKind.None))
        {
            return false;
        }

        if (i.Parameters[0].FullyQualifiedType != $"System.Linq.IQueryable<{c.EntityType}>")
        {
            return false;
        }

        if (k == EntityFrameworkQueryOperatorKind.FirstOrDefault && i.Parameters[1].FullyQualifiedType != $"System.Linq.Expressions.Expression<System.Func<{c.EntityType}, System.Boolean>>")
        {
            return false;
        }

        return i.ReturnType == (k == EntityFrameworkQueryOperatorKind.Count ? "System.Int32" : c.EntityType);
    }

    private static bool ValidEdmxMetadataArguments(ImmutableArray<CompilerProvenArgument> arguments)
        => arguments.Length == 5
            && arguments.Select((argument, ordinal) => argument.Ordinal == ordinal).All(valid => valid)
            && arguments[0].FullyQualifiedType == "System.String"
            && arguments[1].FullyQualifiedType == "System.String"
            && arguments[2].FullyQualifiedType == "System.String"
            && arguments[3].FullyQualifiedType == "System.Boolean"
            && arguments[4].FullyQualifiedType == "System.Boolean"
            && !string.IsNullOrWhiteSpace(arguments[0].Value)
            && !string.IsNullOrWhiteSpace(arguments[1].Value)
            && !string.IsNullOrWhiteSpace(arguments[2].Value)
            && arguments[3].Value is "true" or "false"
            && arguments[4].Value is "true" or "false";

    private static bool ExactWhereStep(FrameworkMethodIdentity identity, string entity)
        => identity.AssemblyIdentity == Queryable
            && identity.AssemblyVersion == "9.0.0.0"
            && identity.AssemblyPublicKeyToken == QueryableToken
            && identity.ContainingMetadataType == Queryable
            && identity.MethodMetadataName == "Where"
            && identity.GenericArity == 1
            && identity.Parameters.Length == 2
            && identity.Parameters.All(parameter => parameter.RefKind == ParameterRefKind.None)
            && identity.Parameters[0].FullyQualifiedType == $"System.Linq.IQueryable<{entity}>"
            && identity.Parameters[1].FullyQualifiedType == $"System.Linq.Expressions.Expression<System.Func<{entity}, System.Boolean>>"
            && identity.ReturnType == $"System.Linq.IQueryable<{entity}>";
    private static EntityFrameworkQueryFact Query(CompilationProfileId profile, OperationDescriptor o, FrameworkQueryChainDescriptor c, EntityFrameworkQueryOperatorKind k)
    {
        var chain = c.Steps.Select(step => new EntityFrameworkQueryChainItem(step.TargetIdentity.MethodMetadataName == "Where"
                ? EntityFrameworkQueryOperatorKind.Where : EntityFrameworkQueryOperatorKind.Unknown,
            step.Operation, step.NavigationMemberIdentity)).ToList();
        chain.Add(new EntityFrameworkQueryChainItem(k, o.Id, null));
        return new EntityFrameworkQueryFact
        {
            Id = Id(profile, o, "query"),
            Method = o.Method,
            Operation = o.Id,
            DbContextType = c.ContainingType,
            DbSetMemberType = c.ReceiverType,
            EntityType = c.EntityType,
            Chain = chain.ToImmutableArray(),
            PredicateOperation = o.PredicateShape?.ComparisonOperation,
            PredicateOperator = ComparisonOperatorKind.Equal,
            Evidence = Evidence(o, "query"),
            Certainty = o.Certainty,
        };
    }
    private static EntityFrameworkMutationFact Mutation(CompilationProfileId profile, OperationDescriptor o, EntityFrameworkMutationKind k, string context, string entity) => new()
    { Id = Id(profile, o, "mutation"), Method = o.Method, Operation = o.Id, MutationKind = k, SequenceOrdinal = o.SourceStart, DbContextType = context, EntityType = entity, Evidence = Evidence(o, "mutation"), Certainty = o.Certainty };
    private static BehaviorFactId Id(CompilationProfileId profile, OperationDescriptor o, string kind) => StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(profile, ModelIdValue, ModelVersionValue, kind, new OperationBehaviorFactAnchor(o.Method, o.Id), 0));
    private static ImmutableArray<EvidenceRef> Evidence(OperationDescriptor o, string detail) => [new EvidenceRef(StableIdentity.CreateEvidenceIdV2(new EvidenceIdentityDescriptor(EvidenceKind.FrameworkModel, $"{ModelIdValue}:{ModelVersionValue}", null, null, null, null, o.Certainty, ModelIdValue, ModelVersionValue, $"{detail}:{o.Id.Value}")), EvidenceKind.FrameworkModel, $"{ModelIdValue}:{ModelVersionValue}", null, null, $"{detail}:{o.Id.Value}", o.Certainty, o.Evidence, ModelIdValue, ModelVersionValue)];
}
