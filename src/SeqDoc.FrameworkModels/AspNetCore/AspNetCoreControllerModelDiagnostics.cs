using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.FrameworkModels.AspNetCore;

/// <summary>
/// Builds the deterministic diagnostics the ASP.NET Core controller model emits when an admitted
/// pattern cannot produce an exact result. Identities derive from stable subjects supplied by the
/// model, never from encounter counts, so identical defects always produce identical diagnostic IDs.
/// </summary>
internal static class AspNetCoreControllerModelDiagnostics
{
    internal const string RouteUnavailableCode = "SEQAS001";
    internal const string UnsupportedOutcomeOverloadCode = "SEQAS002";
    internal const string NonConstantStatusCodeCode = "SEQAS003";
    internal const string MalformedRouteTemplateCode = "SEQAS004";
    internal const string RouteWithoutHttpVerbCode = "SEQAS005";
    internal const string EligibilityShapeUnavailableCode = "SEQAS006";
    internal const string DegradedInputCertaintyCode = "SEQAS007";

    internal static AnalysisDiagnostic RouteUnavailable(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(RouteUnavailableCode, profileId, subjectId),
            RouteUnavailableCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "An admitted controller action has no attribute route to combine.",
            new DiagnosticLocation("aspnet core controller action", profileId),
            "The [ApiController] action has no route template on the controller or the action, so a conventional-route guess would be required.",
            "No HTTP entry point was emitted; the action is not presented as reachable.",
            "Add an explicit controller or action route template to the admitted action.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic UnsupportedOutcomeOverload(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(UnsupportedOutcomeOverloadCode, profileId, subjectId),
            UnsupportedOutcomeOverloadCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "A ControllerBase result-helper name was matched without an admitted signature.",
            new DiagnosticLocation("aspnet core controller outcome", profileId),
            "The invoked method has an admitted ControllerBase helper name but a parameter signature outside the supported version table, so no exact status can be proven.",
            "No HTTP outcome fact was emitted; documentation never presents a guessed status as proven.",
            "Add the helper overload to the supported version table or adjust the call to an admitted signature.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic NonConstantStatusCode(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(NonConstantStatusCodeCode, profileId, subjectId),
            NonConstantStatusCodeCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "StatusCode was not called with exactly one compiler-proven integer constant at ordinal 0.",
            new DiagnosticLocation("aspnet core controller outcome", profileId),
            "The StatusCode helper requires exactly one compiler-proven constant argument at ordinal 0 with an integer type whose value parses as an int. Missing, duplicate, wrong-ordinal, wrong-type, overflow, and non-integer constants cannot be resolved statically.",
            "No HTTP outcome fact was emitted; the status is not guessed from surrounding context.",
            "Pass a single constant integer to StatusCode or use an admitted result helper.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic MalformedRouteTemplate(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(MalformedRouteTemplateCode, profileId, subjectId),
            MalformedRouteTemplateCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "An admitted route attribute carries a present but malformed route template.",
            new DiagnosticLocation("aspnet core controller action", profileId),
            "The route attribute has a template argument that cannot be decoded as a string literal or uses an unsupported rooted form, so combining it would invent a route from ambiguous input.",
            "No HTTP entry point was emitted for the malformed template; a partial or invented route is never produced.",
            "Use a plain, well-formed string literal route template on the attribute.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic RouteWithoutHttpVerb(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(RouteWithoutHttpVerbCode, profileId, subjectId),
            RouteWithoutHttpVerbCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "A method-level RouteAttribute has no admitted HTTP verb attribute.",
            new DiagnosticLocation("aspnet core controller action", profileId),
            "The action declares route templates but no admitted HttpGet/HttpPost/HttpPut/HttpDelete attribute, so no HTTP verb can be proven and no Any/all-verb route is invented.",
            "No HTTP entry point was emitted for the route-only method.",
            "Add an admitted HTTP verb attribute to the method.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic EligibilityShapeUnavailable(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(EligibilityShapeUnavailableCode, profileId, subjectId),
            EligibilityShapeUnavailableCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "Controller/action eligibility shape is unavailable or incomplete.",
            new DiagnosticLocation("aspnet core controller action", profileId),
            "The compiler-proven method/type shape required to establish MVC controller and action eligibility was not supplied, so an exact root cannot be proven and none is emitted.",
            "No HTTP entry point or exact outcome was emitted for the unproven symbol.",
            "Project the controlled compiler-shape facts for the method and retry analysis.",
            CertaintyLevel.Exact);
    }

    internal static AnalysisDiagnostic DegradedInputCertainty(CompilationProfileId profileId, string subjectId)
    {
        return new AnalysisDiagnostic(
            CreateDiagnosticId(DegradedInputCertaintyCode, profileId, subjectId),
            DegradedInputCertaintyCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "Non-exact input certainty degraded framework facts.",
            new DiagnosticLocation("aspnet core controller action", profileId),
            "The symbol or operation input was not Exact, so emitted facts and model evidence carry the degraded certainty instead of being promoted to Exact.",
            "Documentation distinguishes degraded facts from exact compiler-proven facts.",
            "Provide exact compiler-proven input to restore Exact certainty.",
            CertaintyLevel.Exact);
    }

    private static DiagnosticId CreateDiagnosticId(string code, CompilationProfileId profileId, string subjectId)
    {
        return StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.FrameworkModel,
            profileId,
            subjectId,
            Ordinal: 0));
    }
}
