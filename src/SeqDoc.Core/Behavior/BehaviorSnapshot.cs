using System.Collections.Immutable;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Identity;

namespace SeqDoc.Core.Behavior;

/// <summary>
/// Contains the validated behavioral facts for one profile run.
/// </summary>
public sealed record BehaviorSnapshot(
    int SchemaVersion,
    string ProducerVersion,
    CompilationProfile Profile,
    string ProgramIndexFingerprint,
    ImmutableArray<MethodFlowSnapshot> MethodFlows,
    CallGraph CallGraph,
    RtaFoundation RtaFoundation,
    ImmutableArray<TypeInstantiationFact> TypeInstantiationFacts,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    string BehaviorFingerprint);
