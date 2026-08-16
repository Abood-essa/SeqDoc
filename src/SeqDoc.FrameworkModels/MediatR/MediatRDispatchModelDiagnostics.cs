using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;

namespace SeqDoc.FrameworkModels.MediatR;

internal static class MediatRDispatchModelDiagnostics
{
    internal const string UnsupportedShapeCode = "MR001";

    internal static AnalysisDiagnostic UnsupportedShape(CompilationProfileId profile, OperationId operation, string reason)
    {
        var detail = $"{operation.Value}|{reason}";
        return new AnalysisDiagnostic(
            StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
                UnsupportedShapeCode, AnalysisStage.FrameworkModel, profile, detail, 0)),
            UnsupportedShapeCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "A recognized MediatR 13 Send invocation has an unsupported shape.",
            new DiagnosticLocation("MediatR dispatch", profile),
            $"The ISender.Send invocation {reason}.",
            "No MediatR dispatch fact was emitted.",
            "Use the exact supported MediatR 13 request/response Send contract.",
            CertaintyLevel.Exact,
            internalDetail: detail);
    }
}
