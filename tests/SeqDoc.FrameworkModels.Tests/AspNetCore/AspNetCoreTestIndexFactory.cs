using System.Collections.Immutable;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.FrameworkModels.Tests.AspNetCore;

/// <summary>
/// Builds deterministic Program Index snapshots and model descriptors for ASP.NET Core controller
/// model tests. Every symbol and attribute uses the exact fully qualified identities the Roslyn index
/// produces, so tests exercise the same semantic inventory without raw name matching.
/// </summary>
internal static class AspNetCoreTestIndexFactory
{
    public const string ProjectRelativePath = "tests/fixtures/PassC/AspNetCoreControllers/AspNetCoreControllers.csproj";
    public const string ControllerMetadataName = "AspNetCoreControllers.OrdersController";
    public const string ControllerBaseAssembly = "Microsoft.AspNetCore.Mvc.Core";
    public const string ControllerBaseType = "Microsoft.AspNetCore.Mvc.ControllerBase";
    public const string ControllerBaseAssemblyVersion = "10.0.0.0";

    public static CompilationProfile Profile { get; } =
        CompilationProfile.Create(ProjectRelativePath, "Release", "net10.0");

    public static ProjectId ProjectId { get; } = new("project:v1:aspnetcore-controllers");

    public static DocumentId DocumentId { get; } = new("document:v1:orders-controller");

    public static SymbolId ControllerSymbol { get; } = new("symbol:v1:AspNetCoreControllers.OrdersController");

    public static SymbolId MethodSymbol(string name) => new($"symbol:v1:AspNetCoreControllers.OrdersController.{name}");

    public static MethodId MethodId(string name) => new($"method:v1:AspNetCoreControllers.OrdersController.{name}");

    public static EvidenceRef SourceEvidence(string symbol)
        => new(
            new EvidenceId($"evidence:v1:{symbol}"),
            EvidenceKind.Source,
            "Controllers/OrdersController.cs",
            new SourceRange(DocumentId, new SourcePosition(10, 0), new SourcePosition(10, 30)),
            symbol,
            detail: null,
            CertaintyLevel.Exact);

    public static ProgramAttributeApplication Attribute(
        SymbolId target,
        string attributeType,
        params string[] arguments)
        => new(
            $"attribute:v1:{attributeType}|{target.Value}|{string.Join(",", arguments)}",
            target,
            attributeType,
            $"{attributeType}.ctor",
            arguments.ToImmutableArray(),
            [SourceEvidence(attributeType)]);

    public static ProgramProject Project(ProjectKind kind = ProjectKind.Library)
        => new(
            ProjectId,
            "AspNetCoreControllers",
            ProjectRelativePath,
            Profile.Id,
            "net10.0",
            kind,
            "project-build:v1:test",
            [],
            [SourceEvidence("project")]);

    public static ProgramReference MvcCoreReference()
        => new(
            "reference:v1:assembly|Microsoft.AspNetCore.Mvc.Core",
            ProjectId,
            ProgramReferenceKind.Assembly,
            ControllerBaseAssembly,
            "10.0.0",
            [SourceEvidence("reference")]);

    public static ProgramType ControllerType()
        => new(
            ControllerSymbol,
            ProjectId,
            new SymbolId("symbol:v1:AspNetCoreControllers"),
            ControllerMetadataName,
            ProgramTypeKind.Class,
            BaseType: new SymbolId("symbol:v1:Microsoft.AspNetCore.Mvc.ControllerBase"),
            Interfaces: [],
            SignatureFingerprint: "type-signature:v1:test",
            Evidence: [SourceEvidence(ControllerMetadataName)]);

    public static ProgramMethod Method(
        string name,
        (string Name, string Type)[] parameters,
        string returnType = "Microsoft.AspNetCore.Mvc.ActionResult")
        => new(
            MethodId(name),
            MethodSymbol(name),
            ControllerSymbol,
            name,
            $"{ControllerMetadataName}.{name}({string.Join(", ", parameters.Select(p => p.Type))})",
            parameters
                .Select(parameter => new ParameterDescriptor(parameter.Name, parameter.Type, ParameterRefKind.None))
                .ToImmutableArray(),
            returnType,
            $"method-signature:v1:{name}",
            $"method-body:v1:{name}",
            [SourceEvidence($"{ControllerMetadataName}.{name}")]);

    public static ProgramIndexSnapshot ToIndex(
        ImmutableArray<ProgramType> types,
        ImmutableArray<ProgramMethod> methods,
        ImmutableArray<ProgramAttributeApplication> attributes,
        ProjectKind projectKind = ProjectKind.Library,
        bool includeMvcReference = true)
        => new(
            SchemaVersion: 1,
            ProducerVersion: "test",
            Profile,
            [Project(projectKind)],
            [
                new ProgramDocument(
                    DocumentId,
                    ProjectId,
                    "Controllers/OrdersController.cs",
                    DocumentOrigin.Source,
                    "content:v1",
                    null,
                    [SourceEvidence("document")]),
            ],
            [],
            types,
            [],
            methods,
            attributes,
            includeMvcReference ? [MvcCoreReference()] : [],
            [],
            [],
            [],
            "input-hash",
            "index-fingerprint");

    public static SymbolDescriptor MethodSymbolDescriptor(string name, FrameworkMethodShape? shape = null)
        => new(
            MethodSymbol(name),
            "Method",
            name,
            DocumentId,
            100,
            24,
            [SourceEvidence($"{ControllerMetadataName}.{name}")],
            CertaintyLevel.Exact,
            shape ?? EligibleMethodShape(name));

    /// <summary>
    /// Constructs the same method symbol descriptor as <see cref="MethodSymbolDescriptor(string, FrameworkMethodShape?)"/>
    /// but with no compiler shape attached. Unlike the default-eligible helper, the optional-shape
    /// fallback is deliberately not applied so tests can exercise the fails-closed missing-shape path.
    /// </summary>
    public static SymbolDescriptor MethodSymbolDescriptorWithoutShape(string name)
        => new(
            MethodSymbol(name),
            "Method",
            name,
            DocumentId,
            100,
            24,
            [SourceEvidence($"{ControllerMetadataName}.{name}")],
            CertaintyLevel.Exact,
            MethodShape: null);

    /// <summary>
    /// The eligible controller type shape used by default in model tests: a public nonabstract
    /// nongeneric class whose exact base chain contains ControllerBase from Microsoft.AspNetCore.Mvc.
    /// Core version 10.0.0.0.
    /// </summary>
    public static FrameworkTypeIdentity EligibleControllerTypeIdentity()
        => new("AspNetCoreControllers", "1.0.0", ControllerMetadataName);

    public static FrameworkTypeShape EligibleControllerTypeShape()
        => new(
            Identity: EligibleControllerTypeIdentity(),
            IsClass: true,
            IsPublicOrNestedPublic: true,
            IsAbstract: false,
            IsStatic: false,
            GenericArity: 0,
            BaseTypeChain:
            [
                new FrameworkTypeIdentity(ControllerBaseAssembly, ControllerBaseAssemblyVersion, ControllerBaseType),
                new FrameworkTypeIdentity("System.Private.CoreLib", "10.0.0.0", "System.Object"),
            ]);

    public static FrameworkMethodShape EligibleMethodShape(string methodName)
        => new(
            MethodSymbol(methodName),
            ControllerSymbol,
            IsOrdinary: true,
            IsPublic: true,
            IsStatic: false,
            IsAbstract: false,
            GenericArity: 0,
            DeclaringType: EligibleControllerTypeShape());

    public static FrameworkMethodIdentity ControllerBaseIdentity(string methodName, params string[] parameterTypes)
        => new(
            ControllerBaseAssembly,
            ControllerBaseType,
            methodName,
            GenericArity: 0,
            parameterTypes
                .Select(parameterType => new ParameterIdentityDescriptor(ParameterRefKind.None, parameterType))
                .ToImmutableArray(),
            ReturnType: ResolveControllerBaseReturnType(methodName, parameterTypes),
            AssemblyVersion: ControllerBaseAssemblyVersion);

    public static FrameworkMethodIdentity ControllerBaseIdentityWithVersion(
        string? assemblyVersion,
        string methodName,
        params string[] parameterTypes)
        => new(
            ControllerBaseAssembly,
            ControllerBaseType,
            methodName,
            GenericArity: 0,
            parameterTypes
                .Select(parameterType => new ParameterIdentityDescriptor(ParameterRefKind.None, parameterType))
                .ToImmutableArray(),
            ReturnType: ResolveControllerBaseReturnType(methodName, parameterTypes),
            AssemblyVersion: assemblyVersion);

    /// <summary>
    /// Resolves the exact ASP.NET Core ControllerBase result type for the supported version table,
    /// mirroring the real framework overloads so model tests exercise exact return-type matching.
    /// </summary>
    private static string ResolveControllerBaseReturnType(string methodName, string[] parameterTypes)
        => methodName switch
        {
            "Ok" => parameterTypes.Length == 0
                ? "Microsoft.AspNetCore.Mvc.OkResult"
                : "Microsoft.AspNetCore.Mvc.OkObjectResult",
            "CreatedAtAction" => "Microsoft.AspNetCore.Mvc.CreatedAtActionResult",
            "BadRequest" => parameterTypes.Length == 0
                ? "Microsoft.AspNetCore.Mvc.BadRequestResult"
                : "Microsoft.AspNetCore.Mvc.BadRequestObjectResult",
            "NotFound" => parameterTypes.Length == 0
                ? "Microsoft.AspNetCore.Mvc.NotFoundResult"
                : "Microsoft.AspNetCore.Mvc.NotFoundObjectResult",
            "Conflict" => parameterTypes.Length == 0
                ? "Microsoft.AspNetCore.Mvc.ConflictResult"
                : "Microsoft.AspNetCore.Mvc.ConflictObjectResult",
            "StatusCode" => parameterTypes.Length == 1
                ? "Microsoft.AspNetCore.Mvc.StatusCodeResult"
                : "Microsoft.AspNetCore.Mvc.ObjectResult",
            _ => "Microsoft.AspNetCore.Mvc.ActionResult",
        };

    public static OperationDescriptor Invocation(
        string rootMethod,
        FrameworkMethodIdentity identity,
        params string[] constantArgumentValues)
        => new(
            new OperationId($"operation:v1:{rootMethod}:{identity.MethodMetadataName}"),
            MethodId(rootMethod),
            "Invocation",
            DocumentId,
            200,
            16,
            [SourceEvidence($"{ControllerMetadataName}.{rootMethod}:{identity.MethodMetadataName}")],
            CertaintyLevel.Exact,
            identity,
            constantArgumentValues
                .Select((value, ordinal) => new CompilerProvenArgument(ordinal, "System.Int32", value))
                .ToImmutableArray());

    public static OperationDescriptor InvocationWithConstantArguments(
        string rootMethod,
        FrameworkMethodIdentity identity,
        params CompilerProvenArgument[] constantArguments)
        => new(
            new OperationId($"operation:v1:{rootMethod}:{identity.MethodMetadataName}"),
            MethodId(rootMethod),
            "Invocation",
            DocumentId,
            200,
            16,
            [SourceEvidence($"{ControllerMetadataName}.{rootMethod}:{identity.MethodMetadataName}")],
            CertaintyLevel.Exact,
            identity,
            constantArguments.ToImmutableArray());

    public static CompilerProvenArgument ConstantArgument(int ordinal, string fullyQualifiedType, string value)
        => new(ordinal, fullyQualifiedType, value);
}
