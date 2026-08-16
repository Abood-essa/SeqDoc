using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SeqDoc.Analysis.Roslyn.Workspace;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Identity;
using SeqDoc.Core.Semantics;
using StableProjectId = SeqDoc.Core.Identity.ProjectId;

namespace SeqDoc.Analysis.Roslyn.Semantics;

/// <summary>
/// Accumulates configuration semantic companion fact drafts during one Roslyn compilation/extraction
/// session and builds the Roslyn-neutral, memory-only <see cref="ConfigurationSemanticFactSet"/>.
/// Admission is exact Microsoft symbol identity: only compiler-resolved
/// <c>ConfigurationBinder.GetValue&lt;bool&gt;</c> reads whose receiver is assignable to the exact
/// <c>IConfiguration</c>, whose key is one compile-time constant non-sensitive string, and whose
/// overload/default shape is explicitly supported project a read fact; receiver, key, and default
/// resolve from compiler-bound argument parameters so reordered named instance/static syntax never
/// depends on source position. Same-name lookalikes, dynamic keys, non-boolean generics, section
/// calls, and custom receivers fail closed. The exact <c>WebApplication.CreateBuilder</c> call
/// projects only the five standard-provider precedence observations, never runtime presence or
/// effective values. Checked-in appsettings observations come only from the explicit
/// repository-owned configuration-file inventory and are attributed per read-owning project; links,
/// reparse points, external, missing, or unreadable files fail closed. Analysis-profile known values
/// parse exactly as booleans with profile provenance; unsupported or conflicting values fail closed.
/// Sensitive keys and raw non-boolean values never enter payloads, and all file collection observes
/// cancellation.
/// </summary>
internal sealed class RoslynConfigurationSemanticFactCollector
{
    private const string ProducerVersion = "0.1.0-alpha";

    internal const string ConfigurationBinderMetadataName = "Microsoft.Extensions.Configuration.ConfigurationBinder";
    internal const string IConfigurationMetadataName = "Microsoft.Extensions.Configuration.IConfiguration";
    internal const string WebApplicationMetadataName = "Microsoft.AspNetCore.Builder.WebApplication";

    private readonly Dictionary<StableProjectId, AuthoritativeConfigurationSymbols> _authoritativeByProject = [];
    private readonly List<ReadDraft> _reads = [];
    private readonly List<ConditionDraft> _conditions = [];
    private ProviderPrecedenceDraft? _providerPrecedence;
    /// <summary>
    /// Records the authoritative Microsoft configuration symbols resolved from one loaded compilation.
    /// The compiler-proven boundary is exact symbol identity against these symbols; lookalike helpers
    /// in other assemblies and same-simple-name helpers never match. Without the authoritative symbols
    /// a project fails closed and admits nothing.
    /// </summary>
    public void SetAuthoritativeSymbols(
        StableProjectId project,
        INamedTypeSymbol? configurationBinder,
        INamedTypeSymbol? iConfiguration,
        INamedTypeSymbol? webApplication)
    {
        _authoritativeByProject[project] = new AuthoritativeConfigurationSymbols(
            configurationBinder,
            iConfiguration,
            webApplication);
    }

    /// <summary>
    /// Records one admitted read when the invocation is an exact Microsoft
    /// <c>ConfigurationBinder.GetValue&lt;bool&gt;</c> call over an <c>IConfiguration</c>-assignable
    /// receiver with a compile-time constant non-sensitive key and a supported overload/default
    /// shape; every other shape is ignored so lookalikes and unsupported forms fail closed. Receiver,
    /// key, and default resolve from the compiler-bound argument parameters against the unreduced
    /// authoritative method, so reordered named instance/static syntax never depends on source
    /// position. Returns the canonical key and optional compiler-proven boolean default of the
    /// admitted read; the read retains the owning project for per-project checked-in attribution.
    /// </summary>
    public bool TryAdmitRead(
        StableProjectId project,
        MethodId methodId,
        IInvocationOperation call,
        OperationId operationId,
        out string key,
        out bool? defaultValue,
        ImmutableArray<EvidenceRef> evidence)
    {
        key = string.Empty;
        defaultValue = null;
        if (!_authoritativeByProject.TryGetValue(project, out var authoritative)
            || authoritative.ConfigurationBinder is null
            || authoritative.IConfiguration is null)
        {
            return false;
        }

        var target = call.TargetMethod;
        if (target is null || !target.IsExtensionMethod || target.Name != "GetValue")
        {
            return false;
        }

        var unreduced = target.ReducedFrom ?? target;
        if (!IsExactBooleanGetValue(call, unreduced, authoritative, out _, out var keyArgument))
        {
            return false;
        }

        if (!TryResolveConstantKey(keyArgument, out key) || ConfigurationSemanticKeyPolicy.IsSensitive(key))
        {
            return false;
        }

        if (!TryResolveSupportedDefault(call, unreduced, out defaultValue))
        {
            return false;
        }

        _reads.Add(new ReadDraft(project, methodId, operationId, key, defaultValue, evidence));
        return true;
    }

    /// <summary>
    /// Records one exact boolean condition association between an admitted read and the direct
    /// local-to-<c>if</c> condition operation. The evidence is the canonical deterministic union of
    /// the admitted read evidence and the exact if-condition operation evidence supplied by the
    /// extractor; the projected fact certainty degrades to the weakest contributor.
    /// </summary>
    public void AddCondition(
        MethodId methodId,
        OperationId readOperation,
        OperationId conditionOperation,
        ImmutableArray<EvidenceRef> evidence) =>
        _conditions.Add(new ConditionDraft(methodId, readOperation, conditionOperation, evidence));

    /// <summary>
    /// True when the invocation is an exact static <c>WebApplication.CreateBuilder</c> call. The
    /// method must live on the authoritative <c>WebApplication</c> type; lookalikes never match.
    /// </summary>
    public bool TryAdmitProviderPrecedence(StableProjectId project, IInvocationOperation call)
    {
        if (!_authoritativeByProject.TryGetValue(project, out var authoritative)
            || authoritative.WebApplication is null)
        {
            return false;
        }

        var target = call.TargetMethod;
        if (target is null)
        {
            return false;
        }

        var original = target.OriginalDefinition ?? target;
        return original.MethodKind == MethodKind.Ordinary
            && original.IsStatic
            && original.Name == "CreateBuilder"
            && original.ContainingType is not null
            && SymbolEqualityComparer.Default.Equals(original.ContainingType.OriginalDefinition, authoritative.WebApplication);
    }

    /// <summary>
    /// Records the five standard-provider precedence observations exactly once per analysis for the
    /// exact <c>WebApplication.CreateBuilder</c> default configuration chain. Repeated calls are
    /// ignored so the observation set is deterministic regardless of call count.
    /// </summary>
    public void AddProviderPrecedence(
        MethodId methodId,
        OperationId operationId,
        ImmutableArray<EvidenceRef> evidence)
    {
        if (_providerPrecedence is not null)
        {
            return;
        }

        _providerPrecedence = new ProviderPrecedenceDraft(methodId, operationId, evidence);
    }

    public async Task<ConfigurationSemanticFactSet> BuildAsync(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        string repositoryRoot,
        ImmutableArray<string> repositoryOwnedConfigurationFiles,
        ImmutableArray<LoadedProject> loadedProjects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(programIndexFingerprint, nameof(programIndexFingerprint));
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot, nameof(repositoryRoot));

        cancellationToken.ThrowIfCancellationRequested();
        var orderedDrafts = _reads
            .DistinctBy(draft => draft.Operation.Value)
            .OrderBy(draft => draft.Project.Value, StringComparer.Ordinal)
            .ThenBy(draft => draft.Method.Value, StringComparer.Ordinal)
            .ThenBy(draft => draft.Operation.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var reads = orderedDrafts
            .Select(draft => ProjectRead(profile.Id, draft))
            .ToImmutableArray();
        var conditions = _conditions
            .DistinctBy(draft => draft.ConditionOperation.Value)
            .OrderBy(draft => draft.Method.Value, StringComparer.Ordinal)
            .ThenBy(draft => draft.ConditionOperation.Value, StringComparer.Ordinal)
            .Select(draft => ProjectCondition(profile.Id, draft))
            .ToImmutableArray();
        var providerObservations = ProjectProviderObservations(profile.Id, _providerPrecedence);
        var candidateKeysByProject = orderedDrafts
            .GroupBy(draft => draft.Project)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(draft => draft.Key)
                    .Distinct()
                    .Order(StringComparer.Ordinal)
                    .ToImmutableArray(),
                EqualityComparer<StableProjectId>.Default);
        var candidateKeys = candidateKeysByProject.Values
            .SelectMany(keys => keys)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        var checkedInValues = await CollectCheckedInValuesAsync(
            profile,
            repositoryRoot,
            repositoryOwnedConfigurationFiles,
            loadedProjects,
            candidateKeysByProject,
            cancellationToken).ConfigureAwait(false);
        var profileKnownValues = CollectProfileKnownValues(profile, candidateKeys);
        var debugProjection = BuildDebugProjection(
            profile,
            programIndexFingerprint,
            reads,
            conditions,
            providerObservations,
            checkedInValues,
            profileKnownValues,
            diagnostics.Length);

        return new ConfigurationSemanticFactSet(
            1,
            ProducerVersion,
            profile,
            programIndexFingerprint,
            reads,
            conditions,
            providerObservations,
            checkedInValues,
            profileKnownValues,
            diagnostics,
            debugProjection);
    }

    private static ConfigurationReadSemanticFact ProjectRead(CompilationProfileId profileId, ReadDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "configuration-read",
            draft.Method,
            draft.Operation,
            FormatReadDetail(draft.Key, draft.DefaultValue)));
        return new ConfigurationReadSemanticFact(
            id,
            draft.Method,
            draft.Operation,
            draft.Key,
            draft.DefaultValue,
            draft.Evidence,
            CertaintyLevel.Exact);
    }

    private static ConfigurationConditionSemanticFact ProjectCondition(CompilationProfileId profileId, ConditionDraft draft)
    {
        var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
            profileId,
            "configuration-condition",
            draft.Method,
            draft.ConditionOperation,
            null));
        return new ConfigurationConditionSemanticFact(
            id,
            draft.Method,
            draft.ReadOperation,
            draft.ConditionOperation,
            trueWhenReadTrue: true,
            draft.Evidence,
            // The condition fact derives from both the read and the if-condition operation; its
            // certainty degrades to the weakest contributor and can never promote beyond it.
            draft.Evidence.Max(item => item.Certainty));
    }

    private static ImmutableArray<StandardProviderObservationFact> ProjectProviderObservations(
        CompilationProfileId profileId,
        ProviderPrecedenceDraft? draft)
    {
        if (draft is null)
        {
            return [];
        }

        StandardConfigurationProviderKind[] kinds =
        [
            StandardConfigurationProviderKind.BaseJson,
            StandardConfigurationProviderKind.EnvironmentJson,
            StandardConfigurationProviderKind.DevelopmentUserSecrets,
            StandardConfigurationProviderKind.EnvironmentVariables,
            StandardConfigurationProviderKind.CommandLine,
        ];
        var builder = ImmutableArray.CreateBuilder<StandardProviderObservationFact>(kinds.Length);
        for (var ordinal = 0; ordinal < kinds.Length; ordinal++)
        {
            var kind = kinds[ordinal];
            var id = StableIdentity.CreateSemanticFactId(new SemanticFactIdentityDescriptor(
                profileId,
                "configuration-provider",
                draft.Method,
                draft.Operation,
                $"{kind.ToString()}|{ordinal.ToString(CultureInfo.InvariantCulture)}"));
            builder.Add(new StandardProviderObservationFact(
                id,
                draft.Method,
                draft.Operation,
                kind,
                ordinal,
                draft.Evidence,
                CertaintyLevel.Conservative));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Reads only the explicitly owned configuration-file inventory and attributes every checked-in
    /// observation to the read-owning project whose repository directory contains the file. A missing,
    /// default, or empty inventory withholds all checked-in observations. Each listed file must be a
    /// repository-contained, regular, non-reparse file under the owning project; external, missing,
    /// linked, ambiguous, or unreadable files fail closed with no observation. Reads are cancellable
    /// and expected file I/O fails closed without ever swallowing cancellation.
    /// </summary>
    private static async Task<ImmutableArray<CheckedInConfigurationValueFact>> CollectCheckedInValuesAsync(
        CompilationProfile profile,
        string repositoryRoot,
        ImmutableArray<string> repositoryOwnedConfigurationFiles,
        ImmutableArray<LoadedProject> loadedProjects,
        Dictionary<StableProjectId, ImmutableArray<string>> candidateKeysByProject,
        CancellationToken cancellationToken)
    {
        if (repositoryOwnedConfigurationFiles.IsDefaultOrEmpty
            || candidateKeysByProject.Count == 0)
        {
            return [];
        }

        // Normalize the explicit ownership inventory once; malformed, rooted, escaping, or non-
        // appsettings entries fail closed by contributing no observation.
        var ownedFiles = repositoryOwnedConfigurationFiles
            .Select(TryNormalizeOwnedFile)
            .Where(entry => entry is not null)
            .Select(entry => entry!.Value)
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToImmutableArray();
        if (ownedFiles.IsEmpty)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<CheckedInConfigurationValueFact>();
        foreach (var project in loadedProjects.OrderBy(project => project.StableId.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!candidateKeysByProject.TryGetValue(project.StableId, out var projectKeys)
                || project.Project.FilePath is null)
            {
                continue;
            }

            var projectDirectory = Path.GetDirectoryName(project.Project.FilePath);
            if (projectDirectory is null || !Directory.Exists(projectDirectory))
            {
                continue;
            }

            var relativeProjectDirectoryName = Path.GetDirectoryName(project.RepositoryRelativePath) ?? string.Empty;
            var projectRelativeDirectory = relativeProjectDirectoryName.Length == 0
                ? string.Empty
                : RepositoryRelativePath.Normalize(relativeProjectDirectoryName);
            foreach (var ownedFile in ownedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsContainedInProject(ownedFile.RelativePath, projectRelativeDirectory))
                {
                    continue;
                }

                var absolutePath = Path.Combine(
                    repositoryRoot,
                    ownedFile.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!IsEligibleRegularFile(absolutePath))
                {
                    continue;
                }

                JsonElement root;
                try
                {
                    string jsonText = await File.ReadAllTextAsync(absolutePath, cancellationToken).ConfigureAwait(false);
                    using var document = JsonDocument.Parse(
                        jsonText,
                        new JsonDocumentOptions
                        {
                            CommentHandling = JsonCommentHandling.Skip,
                            AllowTrailingCommas = true,
                        });
                    root = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    // An unreadable or malformed owned appsettings file contributes no observation and
                    // never fails the analysis.
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                // OperationCanceledException intentionally propagates; optional observations never
                // swallow cancellation.

                foreach (var key in projectKeys)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryResolveJsonBoolean(root, key, out bool value))
                    {
                        continue;
                    }

                    builder.Add(new CheckedInConfigurationValueFact(
                        CreateFileFactId(profile.Id, "checked-in-configuration-value", $"{key}|{ownedFile.RelativePath}"),
                        key,
                        value,
                        ownedFile.RelativePath,
                        CreateConfigurationEvidence(ownedFile.RelativePath, key, CertaintyLevel.Conservative),
                        CertaintyLevel.Conservative,
                        mayBeOverridden: true));
                }
            }
        }

        return builder
            .OrderBy(fact => fact.Key, StringComparer.Ordinal)
            .ThenBy(fact => fact.SourceFile, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    /// Normalizes one inventory entry to a repository-relative appsettings path. Rooted, escaping,
    /// empty, or non-appsettings entries fail closed by yielding no file.
    /// </summary>
    private static (string RelativePath, string FileName)? TryNormalizeOwnedFile(string file)
    {
        try
        {
            var relativePath = RepositoryRelativePath.Normalize(file);
            string fileName = Path.GetFileName(relativePath);
            return IsOwnedAppSettingsFileName(fileName)
                ? (relativePath, fileName)
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsContainedInProject(string relativePath, string projectRelativeDirectory)
    {
        if (string.IsNullOrEmpty(projectRelativeDirectory))
        {
            // A repository-root project owns every contained inventory file.
            return true;
        }

        return relativePath.StartsWith(projectRelativeDirectory + "/", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when the absolute file is a regular file with no reparse point on the file or on any
    /// relevant path component up to the repository root. Missing, directory, device, linked, or
    /// inaccessible files fail closed.
    /// </summary>
    private static bool IsEligibleRegularFile(string absolutePath)
    {
        try
        {
            var attributes = File.GetAttributes(absolutePath);
            if ((attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
            {
                return false;
            }

            var current = Path.GetDirectoryName(absolutePath);
            while (current is not null)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                current = Path.GetDirectoryName(current);
            }

            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Observes a matching <c>CompilationProfile.AnalysisProperties</c> value only when its key is
    /// also one of the admitted read candidate keys, the key is non-sensitive, and the value parses
    /// exactly as a boolean. Unrelated boolean properties fail closed just like non-boolean values so
    /// the profile-known facts never broaden the observed configuration surface beyond the read keys.
    /// </summary>
    private static ImmutableArray<ProfileKnownConfigurationValueFact> CollectProfileKnownValues(
        CompilationProfile profile,
        ImmutableArray<string> candidateKeys)
    {
        var builder = ImmutableArray.CreateBuilder<ProfileKnownConfigurationValueFact>();
        foreach (var property in profile.AnalysisProperties)
        {
            if (!candidateKeys.Contains(property.Key, StringComparer.Ordinal)
                || ConfigurationSemanticKeyPolicy.IsSensitive(property.Key))
            {
                continue;
            }

            if (!bool.TryParse(property.Value, out bool value))
            {
                continue;
            }

            string provenance = "analysis-profile";
            builder.Add(new ProfileKnownConfigurationValueFact(
                CreateFileFactId(profile.Id, "profile-known-configuration-value", $"{property.Key}|{value.ToString()}"),
                property.Key,
                value,
                provenance,
                CreateConfigurationEvidence(profile.Id.Value, property.Key, CertaintyLevel.Conservative),
                CertaintyLevel.Conservative));
        }

        return builder
            .OrderBy(fact => fact.Key, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static bool IsExactBooleanGetValue(
        IInvocationOperation call,
        IMethodSymbol unreduced,
        AuthoritativeConfigurationSymbols authoritative,
        out IOperation? receiver,
        out IArgumentOperation? keyArgument)
    {
        receiver = null;
        keyArgument = null;
        var original = unreduced.OriginalDefinition ?? unreduced;
        if (original.MethodKind != MethodKind.Ordinary
            || original.Arity != 1
            || original.ContainingType is null
            || !SymbolEqualityComparer.Default.Equals(original.ContainingType.OriginalDefinition, authoritative.ConfigurationBinder))
        {
            return false;
        }

        var target = call.TargetMethod!;
        if (target.TypeArguments.Length != 1
            || target.TypeArguments[0].SpecialType != SpecialType.System_Boolean)
        {
            return false;
        }

        var iConfiguration = authoritative.IConfiguration;
        if (iConfiguration is null)
        {
            return false;
        }

        var (boundReceiver, boundKey, _) = ResolveBoundArguments(call, unreduced);
        receiver = boundReceiver;
        keyArgument = boundKey;
        if (receiver is null
            || receiver.Type is null
            || !IsAssignableTo(receiver.Type, iConfiguration))
        {
            return false;
        }

        // The supported overloads take exactly the key parameter and optionally the typed default.
        if (unreduced.Parameters.Length is not (2 or 3)
            || unreduced.Parameters[1].Type.SpecialType != SpecialType.System_String)
        {
            return false;
        }

        return keyArgument is not null;
    }

    /// <summary>
    /// Resolves the receiver, key, and optional default of a <c>GetValue</c> call from the
    /// compiler-bound <see cref="IArgumentOperation.Parameter"/> mapped to the unreduced authoritative
    /// method's ordinal space (this=0, key=1, default=2). Instance syntax reports the reduced
    /// extension method (key=0, default=1) and is mapped back by +1; static syntax reports the
    /// original ordinals directly. Reordered named arguments therefore resolve exactly and source
    /// positions are never consulted.
    /// </summary>
    private static (IOperation? Receiver, IArgumentOperation? Key, IArgumentOperation? Default) ResolveBoundArguments(
        IInvocationOperation call,
        IMethodSymbol unreduced)
    {
        IOperation? receiver = call.Instance;
        IArgumentOperation? key = null;
        IArgumentOperation? defaultValue = null;
        var unreducedDefinition = unreduced.OriginalDefinition ?? unreduced;
        foreach (var argument in call.Arguments)
        {
            if (argument.Parameter is null)
            {
                continue;
            }

            var containingMethod = argument.Parameter.ContainingSymbol as IMethodSymbol;
            if (containingMethod is null)
            {
                continue;
            }

            int ordinal;
            var containingReducedFrom = containingMethod.ReducedFrom;
            if (containingReducedFrom is not null
                && SymbolEqualityComparer.Default.Equals((containingReducedFrom.OriginalDefinition ?? containingReducedFrom), unreducedDefinition))
            {
                // Reduced instance syntax: key=0 and default=1 in the reduced space map to the
                // unreduced key=1 and default=2 ordinals.
                ordinal = argument.Parameter.Ordinal + 1;
            }
            else if (SymbolEqualityComparer.Default.Equals((containingMethod.OriginalDefinition ?? containingMethod), unreducedDefinition))
            {
                ordinal = argument.Parameter.Ordinal;
            }
            else
            {
                continue;
            }

            switch (ordinal)
            {
                case 0:
                    receiver ??= argument.Value;
                    break;
                case 1:
                    key = argument;
                    break;
                case 2:
                    defaultValue = argument;
                    break;
            }
        }

        return (receiver, key, defaultValue);
    }

    private static bool TryResolveConstantKey(IArgumentOperation? keyArgument, out string key)
    {
        key = string.Empty;
        if (keyArgument is null)
        {
            return false;
        }

        var value = UnwrapImplicitConversions(keyArgument.Value);
        if (value.ConstantValue is not { HasValue: true, Value: string candidate } || candidate.Length == 0)
        {
            return false;
        }

        key = candidate;
        return true;
    }

    private static bool TryResolveSupportedDefault(
        IInvocationOperation call,
        IMethodSymbol unreduced,
        out bool? defaultValue)
    {
        defaultValue = null;
        var (_, _, defaultArgument) = ResolveBoundArguments(call, unreduced);
        if (defaultArgument is null)
        {
            // The no-default overload carries no explicit default.
            return true;
        }

        var value = UnwrapImplicitConversions(defaultArgument.Value);
        if (value.ConstantValue is not { HasValue: true, Value: bool parsed })
        {
            // A non-constant or non-boolean default is an unsupported default shape and fails closed.
            return false;
        }

        defaultValue = parsed;
        return true;
    }

    private static bool IsAssignableTo(ITypeSymbol source, INamedTypeSymbol destination)
    {
        if (SymbolEqualityComparer.Default.Equals(source.OriginalDefinition, destination.OriginalDefinition))
        {
            return true;
        }

        if (source is INamedTypeSymbol named)
        {
            if (named.AllInterfaces.Any(iface =>
                    SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, destination.OriginalDefinition)))
            {
                return true;
            }

            if (named.BaseType is { } baseType && IsAssignableTo(baseType, destination))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveJsonBoolean(JsonElement element, string key, out bool value)
    {
        value = false;
        var current = element;
        foreach (var segment in key.Split(':'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return false;
            }

            current = next;
        }

        if (current.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (current.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool IsOwnedAppSettingsFileName(string fileName)
    {
        if (string.Equals(fileName, "appsettings.json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && fileName.Length > "appsettings..json".Length;
    }

    private static IOperation UnwrapImplicitConversions(IOperation operation)
    {
        IOperation current = operation;
        while (current is IConversionOperation { IsImplicit: true } conversion)
        {
            current = conversion.Operand;
        }

        return current;
    }

    private static string FormatReadDetail(string key, bool? defaultValue)
        => $"{key}|bool|{(defaultValue.HasValue ? defaultValue.Value.ToString() : "null")}";

    /// <summary>
    /// Creates the deterministic identity of a file- or profile-scoped configuration fact. These facts
    /// have no method/operation anchor, so their identity derives from the profile, fact kind, and a
    /// canonical root-independent discriminator (key, source file, or value) without consulting the
    /// checkout path or enumeration order. The canonical text is produced by serializing one fixed
    /// record through <see cref="JsonSerializer"/> so quotes, separators, and backslashes inside safe
    /// keys or source files are escaped and can never collide with the identity framing.
    /// </summary>
    private static SemanticFactId CreateFileFactId(
        CompilationProfileId profileId,
        string factKind,
        string discriminator)
    {
        string canonicalJson = JsonSerializer.Serialize(new FileFactIdentityPayload(
            SchemaVersion: 1,
            ProfileId: profileId.Value,
            FactKind: factKind,
            Discriminator: discriminator));
        byte[] digest = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return new SemanticFactId($"semantic-fact:v1:{Convert.ToHexStringLower(digest)}");
    }

    private sealed record FileFactIdentityPayload(
        int SchemaVersion,
        string ProfileId,
        string FactKind,
        string Discriminator);

    private static ImmutableArray<EvidenceRef> CreateConfigurationEvidence(
        string artifact,
        string? symbol,
        CertaintyLevel certainty)
    {
        var id = StableIdentity.CreateEvidenceIdV2(new EvidenceIdentityDescriptor(
            EvidenceKind.Configuration,
            artifact,
            Document: null,
            SourceStart: null,
            SourceLength: null,
            Symbol: symbol,
            Certainty: certainty));
        return ImmutableArray.Create(new EvidenceRef(
            id,
            EvidenceKind.Configuration,
            artifact,
            range: null,
            symbol: symbol,
            detail: null,
            certainty));
    }

    private static string BuildDebugProjection(
        CompilationProfile profile,
        string programIndexFingerprint,
        ImmutableArray<ConfigurationReadSemanticFact> reads,
        ImmutableArray<ConfigurationConditionSemanticFact> conditions,
        ImmutableArray<StandardProviderObservationFact> providerObservations,
        ImmutableArray<CheckedInConfigurationValueFact> checkedInValues,
        ImmutableArray<ProfileKnownConfigurationValueFact> profileKnownValues,
        int diagnosticCount)
    {
        var lines = new List<(string Id, string Line)>();
        foreach (var fact in reads)
        {
            lines.Add((fact.Id.Value, $"read {fact.Id.Value} method={fact.Method.Value} operation={fact.Operation.Value} key={fact.Key} default={(fact.DefaultValue.HasValue ? fact.DefaultValue.Value.ToString() : "null")} certainty={fact.Certainty.ToString()}"));
        }

        foreach (var fact in conditions)
        {
            lines.Add((fact.Id.Value, $"condition {fact.Id.Value} method={fact.Method.Value} read={fact.ReadOperation.Value} condition={fact.ConditionOperation.Value} trueWhenReadTrue={fact.TrueWhenReadTrue.ToString()} certainty={fact.Certainty.ToString()}"));
        }

        foreach (var fact in providerObservations)
        {
            lines.Add((fact.Id.Value, $"provider {fact.Id.Value} kind={fact.ProviderKind.ToString()} ordinal={fact.PrecedenceOrdinal.ToString(CultureInfo.InvariantCulture)} certainty={fact.Certainty.ToString()}"));
        }

        foreach (var fact in checkedInValues)
        {
            lines.Add((fact.Id.Value, $"checked-in {fact.Id.Value} key={fact.Key} value={fact.Value.ToString()} source={fact.SourceFile} mayBeOverridden={fact.MayBeOverridden.ToString()} certainty={fact.Certainty.ToString()}"));
        }

        foreach (var fact in profileKnownValues)
        {
            lines.Add((fact.Id.Value, $"profile-known {fact.Id.Value} key={fact.Key} value={fact.Value.ToString()} provenance={fact.AnalysisProfileSource} certainty={fact.Certainty.ToString()}"));
        }

        var builder = new StringBuilder();
        builder.Append("configuration-facts:v1").Append('\n');
        builder.Append("producer=").Append(ProducerVersion).Append('\n');
        builder.Append("profile=").Append(profile.Id.Value).Append('\n');
        builder.Append("programIndexFingerprint=").Append(programIndexFingerprint).Append('\n');
        builder.Append("diagnosticCount=").Append(diagnosticCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var line in lines.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.Append(line.Line).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    private sealed record AuthoritativeConfigurationSymbols(
        INamedTypeSymbol? ConfigurationBinder,
        INamedTypeSymbol? IConfiguration,
        INamedTypeSymbol? WebApplication);

    private sealed record ReadDraft(
        StableProjectId Project,
        MethodId Method,
        OperationId Operation,
        string Key,
        bool? DefaultValue,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record ConditionDraft(
        MethodId Method,
        OperationId ReadOperation,
        OperationId ConditionOperation,
        ImmutableArray<EvidenceRef> Evidence);

    private sealed record ProviderPrecedenceDraft(
        MethodId Method,
        OperationId Operation,
        ImmutableArray<EvidenceRef> Evidence);
}
