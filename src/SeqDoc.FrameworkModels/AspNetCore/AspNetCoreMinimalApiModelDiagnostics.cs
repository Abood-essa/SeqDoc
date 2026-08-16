using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using SeqDoc.Core.Evidence;

namespace SeqDoc.FrameworkModels.AspNetCore;

internal static class AspNetCoreMinimalApiModelDiagnostics
{
    internal const string UnsupportedShapeCode = "MA001";

    internal static AnalysisDiagnostic UnsupportedShape(
        CompilationProfileId profileId,
        OperationId operationId,
        string reason)
    {
        var detail = $"{operationId.Value}|{reason}";
        return new AnalysisDiagnostic(
            StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
                UnsupportedShapeCode, AnalysisStage.FrameworkModel, profileId, detail, 0)),
            UnsupportedShapeCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "A recognized ASP.NET Core Minimal API registration has an unsupported shape.",
            new DiagnosticLocation("aspnet core minimal api registration", profileId),
            $"The EndpointRouteBuilderExtensions {reason}.",
            "No Minimal API route fact was emitted.",
            "Use an exact supported ASP.NET Core Minimal API registration shape.",
            CertaintyLevel.Exact,
            internalDetail: detail);
    }
}
