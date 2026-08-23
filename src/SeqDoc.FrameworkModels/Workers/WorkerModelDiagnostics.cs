using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.FrameworkModels.Workers;

internal static class WorkerModelDiagnostics
{
    internal const string UnsupportedTimerCallbackCode = "SEQWRK001";

    internal static AnalysisDiagnostic UnsupportedTimerCallback(
        CompilationProfileId profile,
        OperationId operation,
        string reason)
    {
        var detail = $"{operation.Value}|{reason}";
        return new AnalysisDiagnostic(
            StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
                UnsupportedTimerCallbackCode,
                AnalysisStage.FrameworkModel,
                profile,
                detail,
                0)),
            UnsupportedTimerCallbackCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "A timer registration has an unsupported callback shape.",
            new DiagnosticLocation("System.Threading.Timer registration", profile),
            $"The exact Timer constructor was found, but its callback {reason}.",
            "No scheduler job fact was emitted and callback behavior is not presented as a proven job.",
            "Use the supported exact method-group callback form.",
            CertaintyLevel.Exact,
            internalDetail: detail);
    }
}
