using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;

namespace SeqDoc.FrameworkModels.OutboundHttp;

/// <summary>
/// Distinguishes why a recognized-but-unsupported <c>System.Net.Http.HttpClient</c> request overload
/// could not be admitted. The diagnostic code is always <see cref="OutboundHttpDiagnosticCodes.DiagnosticCode"/>;
/// only the reason text varies, and it never contributes to the diagnostic identity.
/// </summary>
internal enum OutboundHttpUnsupportedReason
{
    WrongAssemblyVersion,
    WrongShape,
    UriParameter,
    CancellationTokenOverload,
    CompletionOptionOverload,
    SendAsync,
    MismatchedSuppliedOrdinals,
}

/// <summary>
/// Builds the deterministic diagnostic the outbound HTTP model emits when an operation is recognizable
/// as the supported <c>HttpClient</c> request family (assembly name, public key token, containing type,
/// method name, arity all agree) and the profile's exact <c>System.Net.Http</c> assembly version
/// matches, but the specific overload/shape is not an admitted row. Identity derives only from the
/// exact profile and operation subject, never from an encounter count, so identical defects always
/// produce identical diagnostic IDs. A partial or foreign identity never reaches this builder.
/// </summary>
public static class OutboundHttpDiagnosticCodes
{
    public const string DiagnosticCode = "SEQHTTP001";

    internal static AnalysisDiagnostic RecognizedUnsupportedOverload(
        CompilationProfileId profileId,
        string operationId,
        string callerDetail,
        OutboundHttpUnsupportedReason reason)
    {
        var reasonText = reason switch
        {
            OutboundHttpUnsupportedReason.WrongAssemblyVersion => "wrong-assembly-version",
            OutboundHttpUnsupportedReason.UriParameter => "uri-parameter",
            OutboundHttpUnsupportedReason.CancellationTokenOverload => "cancellation-token",
            OutboundHttpUnsupportedReason.CompletionOptionOverload => "completion-option",
            OutboundHttpUnsupportedReason.SendAsync => "send-async",
            OutboundHttpUnsupportedReason.MismatchedSuppliedOrdinals => "mismatched-ordinals",
            _ => "wrong-shape",
        };

        var subjectId = operationId;
        var id = StableIdentity.CreateDiagnosticId(new DiagnosticIdentityDescriptor(
            DiagnosticCode,
            AnalysisStage.FrameworkModel,
            profileId,
            subjectId,
            Ordinal: 0));

        return new AnalysisDiagnostic(
            id,
            DiagnosticCode,
            DiagnosticSeverity.Warning,
            AnalysisStage.FrameworkModel,
            "A recognized System.Net.Http.HttpClient request overload is not a supported outbound HTTP request boundary.",
            new DiagnosticLocation("outbound http request", profileId),
            "The invocation's original definition is the recognizable HttpClient request family for this profile's exact System.Net.Http assembly version, but the overload/shape is not one of the admitted GetAsync(string) / PostAsync(string, HttpContent) rows.",
            "No outbound HTTP request boundary is documented for this call; it is retained as a recognized-but-unsupported boundary.",
            "Use HttpClient.GetAsync(string) or HttpClient.PostAsync(string, HttpContent), or document this overload through a separate accepted contract.",
            CertaintyLevel.Exact,
            evidence: default,
            internalDetail: $"operation={operationId}; caller={callerDetail}; reason={reasonText}");
    }
}
