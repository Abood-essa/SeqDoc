using System.Collections.Immutable;
using SeqDoc.Core.Behavior;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Application.Analysis;

/// <summary>
/// Carries the evidence-backed Program Index and the Roslyn-extracted analysis artifacts for one
/// profile. The behavior input remains the accepted extraction record; the memory-only semantic,
/// framework-model, dependency-injection, structural-result, non-Get semantic, configuration semantic,
/// conditional dependency-injection, callback-boundary, and predicate companion facts sit beside it
/// rather than inside it.
/// </summary>
public sealed record ProfileAnalysisExtraction(
    ProgramIndexSnapshot ProgramIndex,
    ProfileAnalysisArtifacts Artifacts)
{
    public ExtractedBehaviorInput BehaviorInput => Artifacts.BehaviorInput;

    public SemanticFactSet SemanticFacts => Artifacts.SemanticFacts;

    public ImmutableArray<OperationDescriptor> Operations => Artifacts.Operations;

    public ImmutableArray<SymbolDescriptor> Symbols => Artifacts.Symbols;

    public DependencyInjectionFactSet DependencyInjectionFacts => Artifacts.DependencyInjectionFacts;

    public StructuralResultFactSet StructuralResultFacts => Artifacts.StructuralResultFacts;

    public NonGetSemanticFactSet NonGetSemanticFacts => Artifacts.NonGetSemanticFacts;

    public ConfigurationSemanticFactSet ConfigurationSemanticFacts => Artifacts.ConfigurationSemanticFacts;

    public ConditionalDependencyInjectionFactSet ConditionalDependencyInjectionFacts => Artifacts.ConditionalDependencyInjectionFacts;

    public CallbackBoundaryFactSet CallbackBoundaryFacts => Artifacts.CallbackBoundaryFacts;

    public PredicateSemanticFactSet PredicateSemanticFacts => Artifacts.PredicateSemanticFacts;

    public MinimalApiHandlerFactSet? MinimalApiHandlerFacts => Artifacts.MinimalApiHandlerFacts;
}

/// <summary>
/// Groups the Roslyn-extracted behavior input, the memory-only semantic companion facts, the
/// Roslyn-neutral framework-model request inputs (operations and symbols), the memory-only
/// dependency-injection companion facts, the memory-only structural-result companion facts, the
/// memory-only non-Get semantic companion facts, the memory-only configuration semantic companion
/// facts, the memory-only conditional dependency-injection companion facts, the memory-only
/// callback-boundary companion facts, and the memory-only predicate companion facts produced during
/// one compilation session. Roslyn objects never cross this boundary.
/// </summary>
public sealed record ProfileAnalysisArtifacts(
    ExtractedBehaviorInput BehaviorInput,
    SemanticFactSet SemanticFacts,
    ImmutableArray<OperationDescriptor> Operations,
    ImmutableArray<SymbolDescriptor> Symbols,
    DependencyInjectionFactSet DependencyInjectionFacts,
    StructuralResultFactSet StructuralResultFacts,
    NonGetSemanticFactSet NonGetSemanticFacts,
    ConfigurationSemanticFactSet ConfigurationSemanticFacts,
    ConditionalDependencyInjectionFactSet ConditionalDependencyInjectionFacts,
    CallbackBoundaryFactSet CallbackBoundaryFacts,
    PredicateSemanticFactSet PredicateSemanticFacts,
    MinimalApiHandlerFactSet? MinimalApiHandlerFacts = null);

/// <summary>
/// Extracts the Program Index, behavior input, semantic companion facts, framework-model request
/// inputs, and dependency-injection companion facts from one compilation session per profile.
/// Implementations must not leak Roslyn, MSBuild, or workspace types through this port.
/// </summary>
public interface IProfileAnalysisExtractor
{
    Task<ApplicationResult<ProfileAnalysisExtraction>> ExtractAsync(
        CompilationAnalysisRequest request,
        CancellationToken cancellationToken);
}
