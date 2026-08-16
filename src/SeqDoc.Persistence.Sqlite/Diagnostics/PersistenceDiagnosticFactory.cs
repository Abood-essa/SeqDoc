using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using CoreSeverity = SeqDoc.Core.Diagnostics.DiagnosticSeverity;

namespace SeqDoc.Persistence.Sqlite.Diagnostics;

internal static class PersistenceDiagnosticFactory
{
    public static AnalysisDiagnostic Create(string code, string summary, string cause, string nextAction, Exception? exception = null)
    {
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            code,
            AnalysisStage.Persistence,
            null,
            null,
            0));
        return new AnalysisDiagnostic(
            id,
            code,
            CoreSeverity.Error,
            AnalysisStage.Persistence,
            summary,
            new DiagnosticLocation("SQLite Program Index store"),
            cause,
            "The previous active Program Index remains unchanged.",
            nextAction,
            CertaintyLevel.Exact,
            internalDetail: exception?.GetType().FullName);
    }
}
