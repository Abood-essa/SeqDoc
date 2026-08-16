using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;
using SeqDoc.Core.Semantics;

namespace SeqDoc.Core.Frameworks;

/// <summary>
/// Describes one versioned framework model. The host sorts models by <see cref="Order"/> and then
/// <see cref="ModelId"/> so registration order never influences analysis results.
/// </summary>
public sealed record FrameworkModelDescriptor(
    string ModelId,
    string Version,
    string DisplayName,
    int Order);

/// <summary>
/// Carries the profile-level inputs a model needs to decide whether it applies to a profile. The
/// Program Index snapshot is the Roslyn-neutral semantic inventory: exact type, method, attribute,
/// and package-reference identities that future models use instead of raw name matching.
/// </summary>
public sealed record FrameworkDetectionContext(
    CompilationProfile Profile,
    ProgramIndexSnapshot ProgramIndex);

/// <summary>
/// Carries the profile-level inputs a model needs while producing facts and hints. The Program Index
/// snapshot supplies exact symbol and package-reference evidence without exposing Roslyn.
/// <see cref="CallbackBoundaryFacts"/> is the final additive accepted contract companion set; it stays null for
/// callers that do not project callback boundaries, and models that consume it treat a null or
/// profile/fingerprint-mismatched set as unsupported rather than inferring a callback target.
/// </summary>
public sealed record FrameworkAnalysisContext(
    CompilationProfile Profile,
    ProgramIndexSnapshot ProgramIndex,
    CallbackBoundaryFactSet? CallbackBoundaryFacts = null);
