namespace SeqDoc.Core.Identity;

/// <summary>Identifies one exact build and analysis profile.</summary>
public readonly record struct CompilationProfileId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies one attempted analysis run.</summary>
public readonly record struct AnalysisRunId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies a project within one compilation profile.</summary>
public readonly record struct ProjectId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies a source or generated document in a project compilation.</summary>
public readonly record struct DocumentId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies a source or metadata symbol.</summary>
public readonly record struct SymbolId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies a callable symbol represented as a Program Index method.</summary>
public readonly record struct MethodId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies a revision-local source operation. Operation IDs are not durable override keys.
/// </summary>
public readonly record struct OperationId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies one evidence record.</summary>
public readonly record struct EvidenceId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one framework or application entry point scoped to a compilation profile. Entry-point
/// identities are canonical across repeated analysis of unchanged code and are independent of
/// registration, source enumeration, and request input order.
/// </summary>
public readonly record struct EntryPointId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies one structured diagnostic.</summary>
public readonly record struct DiagnosticId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies one revision-local node in a method flow graph.</summary>
public readonly record struct FlowNodeId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies one revision-local edge in a method flow graph.</summary>
public readonly record struct FlowEdgeId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies one revision-local exception or loop region in a method flow.</summary>
public readonly record struct FlowRegionId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies one revision-local call site in a method flow.</summary>
public readonly record struct CallSiteId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies one revision-local node in a local value graph.</summary>
public readonly record struct ValueNodeId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identifies one revision-local edge in a local value graph.</summary>
public readonly record struct ValueEdgeId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one revision-local framework-model behavior fact. Facts are revision-local because
/// their underlying evidence anchors can move when source edits change.
/// </summary>
public readonly record struct BehaviorFactId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one revision-local semantic companion fact. Facts are revision-local because their
/// underlying operation and evidence anchors can move when source edits change.
/// </summary>
public readonly record struct SemanticFactId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one revision-local callback boundary fact. Boundary identities are canonical across
/// repeated analysis of unchanged code and independent of candidate construction order; the value
/// must be non-blank.
/// </summary>
public readonly record struct CallbackBoundaryId
{
    public CallbackBoundaryId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one scenario callback region scoped to a compilation profile and entry point. Region
/// identities are canonical across repeated analysis of unchanged code and independent of boundary,
/// member, and edge construction order.
/// </summary>
public readonly record struct ScenarioCallbackRegionId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one scenario-graph node scoped to a compilation profile and entry point. Scenario
/// identities are canonical across repeated analysis of unchanged code and independent of candidate
/// construction order.
/// </summary>
public readonly record struct ScenarioNodeId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one scenario-graph edge scoped to a compilation profile and entry point. Scenario
/// identities are canonical across repeated analysis of unchanged code and independent of candidate
/// construction order.
/// </summary>
public readonly record struct ScenarioEdgeId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one scenario decision scoped to a compilation profile, root/containing method, and
/// controlling flow-node identity. Scenario identities are canonical across repeated analysis of
/// unchanged code and independent of candidate construction order and entry-point identity.
/// </summary>
public readonly record struct ScenarioDecisionId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one semantic true/false arm of a scenario decision scoped to a compilation profile,
/// root/containing method, and the parent decision identity. Scenario identities are canonical across
/// repeated analysis of unchanged code and independent of candidate construction order and
/// entry-point identity.
/// </summary>
public readonly record struct ScenarioArmId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one membership of a scenario node in one decision arm scoped to a compilation profile,
/// root/containing method, and the parent arm identity. Scenario identities are canonical across
/// repeated analysis of unchanged code and independent of candidate construction order and
/// entry-point identity.
/// </summary>
public readonly record struct ScenarioMembershipId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one scenario service composition scoped to a compilation profile and entry point. The
/// composition identity is canonical across repeated analysis of unchanged code and independent of
/// registration, arm, target, and edge construction order.
/// </summary>
public readonly record struct ScenarioCompositionId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one wording phrase scoped to a compilation profile and entry point. Wording
/// identities are canonical across repeated planning of unchanged scenario graphs and independent of
/// phrase construction order.
/// </summary>
public readonly record struct WordingPhraseId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// Identifies one diagram-plan element (participant, message, or branch) scoped to a compilation
/// profile and entry point. Diagram identities are canonical across repeated planning of unchanged
/// scenario graphs and independent of element construction order.
/// </summary>
public readonly record struct DiagramPlanElementId(string Value)
{
    public override string ToString() => Value ?? string.Empty;
}
