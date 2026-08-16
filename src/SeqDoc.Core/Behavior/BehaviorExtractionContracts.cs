using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Behavior;

/// <summary>Classifies an extracted operation using a closed SeqDoc vocabulary.</summary>
public enum ExtractedOperationKind
{
    Unknown,
    Invalid,
    Block,
    ExpressionStatement,
    VariableDeclaration,
    LocalReference,
    ParameterReference,
    FieldReference,
    PropertyReference,
    MemberReference,
    ArrayElementReference,
    Literal,
    Invocation,
    ObjectCreation,
    DynamicInvocation,
    EventAssignment,
    DelegateCreation,
    AnonymousFunction,
    Assignment,
    CompoundAssignment,
    IncrementOrDecrement,
    Conversion,
    Binary,
    Unary,
    Conditional,
    Coalesce,
    Return,
    Throw,
    Rethrow,
    Await,
    ForLoop,
    ForEachLoop,
    WhileLoop,
    DoWhileLoop,
    ConditionalBranch,
    Branch,
    Lock,
    Using,
    End,
}

/// <summary>Classifies how one extracted basic block terminates.</summary>
public enum ExtractedBlockTerminalKind
{
    Unknown,
    None,
    Conditional,
    Return,
    Throw,
    Rethrow,
    Exit,
}

/// <summary>Classifies one extracted exception or control-flow region.</summary>
public enum ExtractedRegionKind
{
    Unknown,
    Root,
    LocalLifetime,
    StaticLocalInitializer,
    Try,
    Filter,
    Catch,
    FilterAndHandler,
    TryAndCatch,
    Finally,
    TryAndFinally,
    ErroneousBody,
}

/// <summary>Describes one flattened operation in a method body.</summary>
public sealed record ExtractedOperation(
    OperationId Id,
    MethodId Method,
    ExtractedOperationKind Kind,
    OperationId? Parent,
    ImmutableArray<OperationId> Operands,
    int EvaluationOrdinal,
    string TypeDescriptor,
    string? ConstantValue,
    bool IsImplicit,
    bool IsSourceBacked,
    ImmutableArray<MethodId> ReferencedMethods,
    ImmutableArray<SymbolId> ReferencedTypes,
    ImmutableArray<SymbolId> ReferencedMembers,
    ExtractedInvocationPayload? Invocation,
    ExtractedAssignmentPayload? Assignment,
    ExtractedConversionPayload? Conversion,
    ExtractedAwaitPayload? Await,
    ExtractedReturnPayload? Return,
    ExtractedThrowPayload? Throw,
    string? LocalName,
    int? ParameterOrdinal,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

public sealed record ExtractedInvocationPayload
{
    public ExtractedInvocationPayload(
        MethodId? Target,
        bool IsDispatchable,
        bool IsDelegateOrEventInvoke,
        bool IsStatic,
        bool IsConstructor,
        bool IsDynamic,
        ImmutableArray<OperationId> Arguments,
        string? TargetContainingTypeName = null,
        string? TargetMethodName = null,
        bool IsInsideNestedFunction = false,
        bool IsLoadedProjectTarget = false,
        string? TargetAssemblyName = null,
        bool IsPlatformTarget = false)
    {
        var hasTypedPresentation = TargetContainingTypeName is not null
            || TargetMethodName is not null
            || TargetAssemblyName is not null;
        if (hasTypedPresentation
            && (TargetContainingTypeName is null
                || TargetMethodName is null
                || TargetAssemblyName is null))
        {
            throw new ArgumentException("Typed invocation target assembly, containing type, and method names must be supplied together.");
        }

        if (TargetContainingTypeName is not null
            && (string.IsNullOrWhiteSpace(TargetContainingTypeName)
                || TargetContainingTypeName.Any(char.IsWhiteSpace)
                || string.IsNullOrWhiteSpace(TargetMethodName)
                || TargetMethodName!.Any(char.IsWhiteSpace)
                || string.IsNullOrWhiteSpace(TargetAssemblyName)
                || TargetAssemblyName!.Any(char.IsWhiteSpace)))
        {
            throw new ArgumentException("Typed invocation target names must be non-empty and contain no whitespace.");
        }

        this.Target = Target;
        this.IsDispatchable = IsDispatchable;
        this.IsDelegateOrEventInvoke = IsDelegateOrEventInvoke;
        this.IsStatic = IsStatic;
        this.IsConstructor = IsConstructor;
        this.IsDynamic = IsDynamic;
        this.Arguments = Arguments;
        this.TargetContainingTypeName = TargetContainingTypeName;
        this.TargetMethodName = TargetMethodName;
        this.IsInsideNestedFunction = IsInsideNestedFunction;
        this.IsLoadedProjectTarget = IsLoadedProjectTarget;
        this.TargetAssemblyName = TargetAssemblyName;
        this.IsPlatformTarget = IsPlatformTarget;
    }

    public MethodId? Target { get; init; }
    public bool IsDispatchable { get; init; }
    public bool IsDelegateOrEventInvoke { get; init; }
    public bool IsStatic { get; init; }
    public bool IsConstructor { get; init; }
    public bool IsDynamic { get; init; }
    public ImmutableArray<OperationId> Arguments { get; init; }
    public string? TargetContainingTypeName { get; init; }
    public string? TargetMethodName { get; init; }
    public bool IsInsideNestedFunction { get; init; }
    public bool IsLoadedProjectTarget { get; init; }
    public string? TargetAssemblyName { get; init; }
    public bool IsPlatformTarget { get; init; }
}

public sealed record ExtractedAssignmentPayload(OperationId Target, OperationId Value, bool IsCompound);

public sealed record ExtractedConversionPayload(string FromType, string ToType);

public sealed record ExtractedAwaitPayload(OperationId Operand);

public sealed record ExtractedReturnPayload(OperationId? Value);

public sealed record ExtractedThrowPayload(OperationId? Exception, bool IsRethrow);

/// <summary>Describes one raw control-flow block in compiler evaluation order.</summary>
public sealed record ExtractedBasicBlock(
    int Ordinal,
    ImmutableArray<OperationId> Operations,
    OperationId? BranchCondition,
    int? FallThroughSuccessor,
    ImmutableArray<int> ConditionalSuccessors,
    ImmutableArray<int> Predecessors,
    ExtractedBlockTerminalKind Terminal,
    bool EscapingThrow,
    ImmutableArray<FlowRegionId> EnteringRegions,
    ImmutableArray<FlowRegionId> LeavingRegions,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>Describes one exception or control-flow region in a method body.</summary>
public sealed record ExtractedExceptionRegion(
    FlowRegionId Id,
    ExtractedRegionKind Kind,
    FlowRegionId? Parent,
    int Ordinal,
    int StartBlockOrdinal,
    int EndBlockOrdinal,
    string? ExceptionType,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

public sealed record ExtractedParameter(string Name, string Type, ParameterRefKind RefKind);

public sealed record ExtractedLocal(string Name, string Type);

/// <summary>Describes one extracted method body with its raw operations, blocks, and regions.</summary>
public sealed record ExtractedMethodBody(
    MethodId Method,
    string BodyFingerprint,
    ImmutableArray<ExtractedParameter> Parameters,
    ImmutableArray<ExtractedLocal> Locals,
    ImmutableArray<ExtractedOperation> Operations,
    ImmutableArray<ExtractedBasicBlock> Blocks,
    ImmutableArray<ExtractedExceptionRegion> Regions,
    ImmutableArray<EvidenceRef> Evidence);

/// <summary>Describes one type in the loaded hierarchy with completeness scope.</summary>
public sealed record ExtractedTypeNode(
    SymbolId Id,
    ProjectId Project,
    string MetadataName,
    SymbolId? BaseType,
    ImmutableArray<SymbolId> Interfaces,
    bool IsSealed,
    bool IsAbstract,
    bool IsInterface,
    bool IsSource,
    ImmutableArray<EvidenceRef> Evidence);

/// <summary>Contains the loaded type hierarchy used by call-resolution foundations.</summary>
public sealed record ExtractedTypeHierarchy(
    ImmutableArray<ExtractedTypeNode> Types,
    bool IsComplete);

/// <summary>Records one provable source type instantiation for Rapid Type Analysis foundations.</summary>
public sealed record TypeInstantiationFact(
    SymbolId InstantiatedType,
    MethodId CreatingMethod,
    OperationId CreatingOperation,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>
/// Records the compiler-proven relationship between one source method and the interface member it
/// implements, including explicit implementations and inherited default-interface fallbacks.
/// </summary>
public sealed record InterfaceImplementationFact(
    MethodId Implementation,
    MethodId InterfaceMember,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>Records that one source method overrides another source method.</summary>
public sealed record MethodOverrideFact(
    MethodId Override,
    MethodId BaseMethod,
    ImmutableArray<EvidenceRef> Evidence,
    CertaintyLevel Certainty);

/// <summary>
/// Carries Roslyn-extracted behavior facts to the framework-neutral behavior analyzer. Roslyn objects
/// never cross this boundary.
/// </summary>
public sealed record ExtractedBehaviorInput(
    CompilationProfile Profile,
    string ProgramIndexFingerprint,
    ImmutableArray<ExtractedMethodBody> Methods,
    ExtractedTypeHierarchy TypeHierarchy,
    ImmutableArray<TypeInstantiationFact> Instantiations,
    ImmutableArray<InterfaceImplementationFact> InterfaceImplementations,
    ImmutableArray<MethodOverrideFact> MethodOverrides,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    string InputFingerprint);
