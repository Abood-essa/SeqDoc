using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SeqDoc.Analysis.Roslyn.ProgramIndex;
using SeqDoc.Core.Evidence;

namespace SeqDoc.Analysis.Roslyn.Frameworks;

/// <summary>
/// Proves the complete active CoreWCF generic-host dispatch chain from compiler evidence alone: a real
/// <c>Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(w =&gt; w.UseStartup&lt;TStartup&gt;())</c>
/// construction/execution chain selects <c>TStartup</c>; <c>TStartup</c>'s own exact
/// <c>Configure(IApplicationBuilder)</c> callback invokes the exact <c>UseServiceModel(Action&lt;IServiceBuilder&gt;)</c>
/// API; and, inside that callback's own lambda, an exact <c>AddServiceEndpoint&lt;TService,TContract&gt;</c>
/// call receives its instance from a matching <c>AddService&lt;TService&gt;()</c> call on the same builder.
/// Every step is an exact compiler-identity check (assembly, version, containing type, method metadata
/// name, arity, parameter/return types); nothing is matched by the name <c>Main</c>, <c>Startup</c>,
/// <c>Configure</c>, or any receiver/local-variable text. An <c>AddServiceEndpoint</c> invocation that
/// exists in source but is not reachable through this exact chain (an unused helper, a disconnected
/// callback, a startup type never selected, or an unchained <c>AddService</c>/<c>AddServiceEndpoint</c>
/// pair) is simply absent from the returned proof — it is never treated as registration evidence.
/// </summary>
internal static class CoreWcfHostChainScanner
{
    private const string HostingAssembly = "Microsoft.Extensions.Hosting";
    private const string HostingAbstractionsAssembly = "Microsoft.Extensions.Hosting.Abstractions";
    private const string AspNetCoreAssembly = "Microsoft.AspNetCore";
    private const string AspNetCoreHostingAssembly = "Microsoft.AspNetCore.Hosting";
    private const string AspNetCoreHostingAbstractionsAssembly = "Microsoft.AspNetCore.Hosting.Abstractions";
    private const string AspNetCoreHttpAbstractionsAssembly = "Microsoft.AspNetCore.Http.Abstractions";
    private const string CoreWcfAssembly = "CoreWCF.Primitives";
    private const string FrameworkVersion = "10.0.0.0";
    private const string CoreWcfVersion = "1.9.0.0";

    private const string HostType = "Microsoft.Extensions.Hosting.Host";
    private const string HostBuilderType = "Microsoft.Extensions.Hosting.IHostBuilder";
    private const string GenericHostBuilderExtensionsType = "Microsoft.Extensions.Hosting.GenericHostBuilderExtensions";
    private const string WebHostBuilderType = "Microsoft.AspNetCore.Hosting.IWebHostBuilder";
    private const string WebHostBuilderExtensionsType = "Microsoft.AspNetCore.Hosting.WebHostBuilderExtensions";
    private const string ApplicationBuilderType = "Microsoft.AspNetCore.Builder.IApplicationBuilder";
    private const string ServiceModelApplicationBuilderExtensionsType = "CoreWCF.Configuration.ServiceModelApplicationBuilderExtensions";
    private const string ServiceBuilderType = "CoreWCF.Configuration.IServiceBuilder";

    /// <summary>
    /// Scans one project's compilation for the complete active host chain and returns, for every exact
    /// <c>AddServiceEndpoint&lt;TService,TContract&gt;</c> invocation proven reachable through it, the
    /// union of source evidence for every link of its own proof (startup selection, the selected
    /// <c>Configure</c> declaration, its <c>UseServiceModel</c> call, and the matching <c>AddService</c>
    /// call). The result is keyed by the endpoint invocation's own <see cref="SyntaxNode"/>
    /// (<see cref="IOperation.Syntax"/>), which is stable within one compilation regardless of which
    /// <see cref="IOperation"/> instance later wraps it.
    /// </summary>
    public static ImmutableDictionary<SyntaxNode, ImmutableArray<EvidenceRef>> Scan(
        Compilation compilation,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents)
    {
        var admittedStartups = CollectAdmittedStartups(compilation, documents);
        var proven = ImmutableDictionary.CreateBuilder<SyntaxNode, ImmutableArray<EvidenceRef>>();

        foreach (var (startupType, startupEvidence) in admittedStartups)
        {
            var configureMethods = startupType.GetMembers("Configure")
                .OfType<IMethodSymbol>()
                .Where(candidate =>
                    !candidate.IsStatic
                    && candidate.MethodKind == MethodKind.Ordinary
                    && !candidate.IsAbstract
                    && SymbolEqualityComparer.Default.Equals(candidate.ContainingType, startupType)
                    && candidate.Arity == 0
                    && candidate.Parameters.Length == 1
                    && candidate.ReturnsVoid
                    && IsExactType(candidate.Parameters[0].Type, AspNetCoreHttpAbstractionsAssembly, FrameworkVersion, ApplicationBuilderType))
                .ToArray();
            var configureMethod = configureMethods.Length == 1 ? configureMethods[0] : null;
            if (configureMethod is null)
            {
                continue;
            }

            foreach (var reference in configureMethod.DeclaringSyntaxReferences)
            {
                var methodSyntax = reference.GetSyntax();
                var model = compilation.GetSemanticModel(methodSyntax.SyntaxTree);
                if (model.GetOperation(methodSyntax) is not { } configureOperation)
                {
                    continue;
                }

                var configureEvidence = CreateEvidence(reference, configureMethod.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat), documents);
                foreach (var useServiceModelCall in EnumerateInvocations(configureOperation))
                {
                    if (!IsExactUseServiceModel(useServiceModelCall.TargetMethod))
                    {
                        continue;
                    }

                    var lambdaOperation = UnwrapToAnonymousFunction(GetArgument(useServiceModelCall, ordinal: 1));
                    if (lambdaOperation is null)
                    {
                        continue;
                    }

                    var useServiceModelEvidence = CreateEvidence(useServiceModelCall.Syntax, documents);
                    foreach (var endpointCall in EnumerateInvocations(lambdaOperation))
                    {
                        if (!IsExactAddServiceEndpoint(endpointCall.TargetMethod)
                            || endpointCall.TargetMethod.TypeArguments.Length != 2
                            || endpointCall.TargetMethod.TypeArguments[0] is not INamedTypeSymbol tService)
                        {
                            continue;
                        }

                        // The receiver of AddServiceEndpoint<TService,TContract> must itself be the exact
                        // AddService<TService>() call for the SAME TService — this is what rejects an
                        // unchained AddService<TA>() followed by a separate AddServiceEndpoint<TB,...> pair.
                        if (UnwrapAllConversionsAndParentheses(endpointCall.Instance) is not IInvocationOperation addServiceCall
                            || !IsExactAddService(addServiceCall.TargetMethod)
                            || addServiceCall.TargetMethod.TypeArguments.Length != 1
                            || !SymbolEqualityComparer.Default.Equals(addServiceCall.TargetMethod.TypeArguments[0], tService))
                        {
                            continue;
                        }

                        var endpointEvidence = startupEvidence
                            .Concat(configureEvidence)
                            .Concat(useServiceModelEvidence)
                            .Concat(CreateEvidence(addServiceCall.Syntax, documents))
                            .DistinctBy(item => item.Id.Value)
                            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                            .ToImmutableArray();
                        proven[endpointCall.Syntax] = endpointEvidence;
                    }
                }
            }
        }

        return proven.ToImmutable();
    }

    /// <summary>
    /// Finds every exact <c>Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(w =&gt;
    /// w.UseStartup&lt;TStartup&gt;())</c> chain in the compilation and returns the admitted
    /// <c>TStartup</c> symbols with the union of source evidence for their own selection (the
    /// <c>CreateDefaultBuilder</c>, <c>ConfigureWebHostDefaults</c>, and <c>UseStartup</c> call sites).
    /// </summary>
    private static Dictionary<INamedTypeSymbol, ImmutableArray<EvidenceRef>> CollectAdmittedStartups(
        Compilation compilation,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents)
    {
        var admitted = new Dictionary<INamedTypeSymbol, ImmutableArray<EvidenceRef>>(SymbolEqualityComparer.Default);
        var entryPoint = compilation.GetEntryPoint(CancellationToken.None);
        if (entryPoint is null || entryPoint.Locations.Any(location => !location.IsInSource))
        {
            return admitted;
        }

        foreach (var reference in entryPoint.DeclaringSyntaxReferences)
        {
            var tree = reference.SyntaxTree;
            if (!documents.ContainsKey(tree))
            {
                continue;
            }

            var model = compilation.GetSemanticModel(tree);
            if (model.GetOperation(reference.GetSyntax()) is not { } entryPointOperation)
            {
                continue;
            }

            foreach (var configureCall in EnumerateInvocations(entryPointOperation))
            {
                if (!IsExactConfigureWebHostDefaults(configureCall.TargetMethod))
                {
                    continue;
                }

                if (UnwrapAllConversionsAndParentheses(GetArgument(configureCall, ordinal: 0)) is not IInvocationOperation createBuilderCall
                    || !IsExactCreateDefaultBuilder(createBuilderCall.TargetMethod))
                {
                    continue;
                }

                var lambdaOperation = UnwrapToAnonymousFunction(GetArgument(configureCall, ordinal: 1));
                if (lambdaOperation is null)
                {
                    continue;
                }

                if (!TryFindTerminal(configureCall, entryPointOperation, out var buildCall, out var terminalCall))
                {
                    continue;
                }

                var chainEvidence = CreateEvidence(createBuilderCall.Syntax, documents)
                    .Concat(CreateEvidence(configureCall.Syntax, documents))
                    .Concat(CreateEvidence(buildCall.Syntax, documents))
                    .Concat(CreateEvidence(terminalCall.Syntax, documents))
                    .ToImmutableArray();
                foreach (var useStartupCall in EnumerateInvocations(lambdaOperation))
                {
                    if (!IsExactUseStartup(useStartupCall.TargetMethod)
                        || useStartupCall.TargetMethod.TypeArguments.Length != 1
                        || useStartupCall.TargetMethod.TypeArguments[0] is not INamedTypeSymbol startupType
                        || startupType.TypeKind == TypeKind.Error
                        || startupType.IsUnboundGenericType)
                    {
                        continue;
                    }

                    var evidence = chainEvidence
                        .Concat(CreateEvidence(useStartupCall.Syntax, documents))
                        .DistinctBy(item => item.Id.Value)
                        .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                        .ToImmutableArray();
                    admitted.TryAdd(startupType, evidence);
                }
            }
        }

        return admitted;
    }

    private static IEnumerable<IInvocationOperation> EnumerateInvocations(IOperation root)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation && !ReferenceEquals(current, root))
            {
                continue;
            }

            if (current is IInvocationOperation invocation)
            {
                yield return invocation;
            }

            foreach (var child in current.ChildOperations.Reverse())
            {
                pending.Push(child);
            }
        }
    }

    private static IOperation? GetArgument(IInvocationOperation call, int ordinal)
        => call.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == ordinal)?.Value;

    private static IAnonymousFunctionOperation? UnwrapToAnonymousFunction(IOperation? operation)
    {
        var current = operation is null ? null : UnwrapAllConversionsAndParentheses(operation);
        return current switch
        {
            IAnonymousFunctionOperation lambda => lambda,
            IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda } => lambda,
            _ => null,
        };
    }

    private static IOperation? UnwrapAllConversionsAndParentheses(IOperation? operation)
    {
        var current = operation;
        while (current is IConversionOperation conversion)
        {
            current = conversion.Operand;
        }

        while (current is IParenthesizedOperation parenthesized)
        {
            current = parenthesized.Operand;
        }

        return current;
    }

    private static ImmutableArray<EvidenceRef> CreateEvidence(
        SyntaxNode syntax,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents)
        => RoslynProgramIndexExtractor.CreateSyntaxEvidence(syntax.GetReference(), syntax.ToString(), documents);

    private static ImmutableArray<EvidenceRef> CreateEvidence(
        SyntaxReference reference,
        string symbol,
        IReadOnlyDictionary<SyntaxTree, RoslynProgramIndexExtractor.DocumentContext> documents)
        => RoslynProgramIndexExtractor.CreateSyntaxEvidence(reference, symbol, documents);

    private static bool TryFindTerminal(
        IInvocationOperation configureCall,
        IOperation entryPointOperation,
        out IInvocationOperation buildCall,
        out IInvocationOperation terminalCall)
    {
        buildCall = null!;
        terminalCall = null!;
        foreach (var candidate in EnumerateInvocations(entryPointOperation))
        {
            if (!IsExactTerminal(candidate.TargetMethod))
            {
                continue;
            }

            if (UnwrapAllConversionsAndParentheses(GetArgument(candidate, ordinal: 0)) is not IInvocationOperation build
                || !IsExactBuild(build.TargetMethod))
            {
                continue;
            }

            if (UnwrapAllConversionsAndParentheses(build.Instance) is not IInvocationOperation receiver
                || !IsExactConfigureWebHostDefaults(receiver.TargetMethod)
                || !ReferenceEquals(receiver.Syntax, configureCall.Syntax))
            {
                continue;
            }

            buildCall = build;
            terminalCall = candidate;
            return true;
        }

        return false;
    }

    private static bool IsExactConfigureWebHostDefaults(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return IsExactAssembly(definition, AspNetCoreAssembly, FrameworkVersion)
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == GenericHostBuilderExtensionsType
            && definition.MetadataName == "ConfigureWebHostDefaults"
            && definition.Arity == 0
            && definition.Parameters.Length == 2
            && IsExactType(definition.Parameters[0].Type, HostingAbstractionsAssembly, FrameworkVersion, HostBuilderType)
            && IsExactActionOfWebHostBuilder(definition.Parameters[1].Type)
            && IsExactType(definition.ReturnType, HostingAbstractionsAssembly, FrameworkVersion, HostBuilderType);
    }

    private static bool IsExactCreateDefaultBuilder(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return IsExactAssembly(definition, HostingAssembly, FrameworkVersion)
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == HostType
            && definition.MetadataName == "CreateDefaultBuilder"
            && definition.Arity == 0
            && definition.Parameters.Length == 1
            && IsExactStringArray(definition.Parameters[0].Type)
            && IsExactType(definition.ReturnType, HostingAbstractionsAssembly, FrameworkVersion, HostBuilderType);
    }

    private static bool IsExactUseStartup(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return IsExactAssembly(definition, AspNetCoreHostingAssembly, FrameworkVersion)
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == WebHostBuilderExtensionsType
            && definition.MetadataName == "UseStartup"
            && definition.Arity == 1
            && definition.Parameters.Length == 1
            && IsExactType(definition.Parameters[0].Type, AspNetCoreHostingAbstractionsAssembly, FrameworkVersion, WebHostBuilderType)
            && IsExactType(definition.ReturnType, AspNetCoreHostingAbstractionsAssembly, FrameworkVersion, WebHostBuilderType);
    }

    private static bool IsExactUseServiceModel(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return IsExactAssembly(definition, CoreWcfAssembly, CoreWcfVersion)
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == ServiceModelApplicationBuilderExtensionsType
            && definition.MetadataName == "UseServiceModel"
            && definition.Arity == 0
            && definition.Parameters.Length == 2
            && IsExactType(definition.Parameters[0].Type, AspNetCoreHttpAbstractionsAssembly, FrameworkVersion, ApplicationBuilderType)
            && IsExactActionOfServiceBuilder(definition.Parameters[1].Type)
            && IsExactType(definition.ReturnType, AspNetCoreHttpAbstractionsAssembly, FrameworkVersion, ApplicationBuilderType);
    }

    // Require both the finite exact core facade identity of System.Action<T> and its exact constructed
    // framework type argument. IsExactCoreType admits the supported compile-time facade assemblies and
    // pinned framework version instead of allowing a namespace/name/arity lookalike delegate.
    private static bool IsExactActionOfServiceBuilder(ITypeSymbol type)
        => type is INamedTypeSymbol { Arity: 1 } action
            && IsExactCoreType(action, FrameworkVersion, "System.Action`1")
            && IsExactType(action.TypeArguments[0], CoreWcfAssembly, CoreWcfVersion, ServiceBuilderType);

    private static bool IsExactActionOfWebHostBuilder(ITypeSymbol type)
        => type is INamedTypeSymbol { Arity: 1 } action
            && IsExactCoreType(action, FrameworkVersion, "System.Action`1")
            && IsExactType(action.TypeArguments[0], AspNetCoreHostingAbstractionsAssembly, FrameworkVersion, WebHostBuilderType);

    private static bool IsExactBuild(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return IsExactAssembly(definition, HostingAbstractionsAssembly, FrameworkVersion)
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == HostBuilderType
            && definition.MetadataName == "Build"
            && definition.Arity == 0
            && definition.Parameters.Length == 0
            && IsExactType(definition.ReturnType, HostingAbstractionsAssembly, FrameworkVersion, "Microsoft.Extensions.Hosting.IHost");
    }

    private static bool IsExactTerminal(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        if (!IsExactAssembly(definition, HostingAbstractionsAssembly, FrameworkVersion)
            || RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) != "Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions"
            || definition.Arity != 0
            || definition.Parameters.Length == 0
            || !IsExactType(definition.Parameters[0].Type, HostingAbstractionsAssembly, FrameworkVersion, "Microsoft.Extensions.Hosting.IHost"))
        {
            return false;
        }

        return (definition.MetadataName == "Run"
            && definition.Parameters.Length == 1
            && definition.ReturnsVoid)
            || (definition.MetadataName == "RunAsync"
                && definition.Parameters.Length == 2
                && IsExactCoreType(definition.Parameters[1].Type, FrameworkVersion, "System.Threading.CancellationToken")
                && IsExactCoreType(definition.ReturnType, FrameworkVersion, "System.Threading.Tasks.Task"));
    }

    private static bool IsExactAddService(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return IsExactAssembly(definition, CoreWcfAssembly, CoreWcfVersion)
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == ServiceBuilderType
            && definition.MetadataName == "AddService"
            && definition.Arity == 1
            && definition.Parameters.Length == 0
            && IsExactType(definition.ReturnType, CoreWcfAssembly, CoreWcfVersion, ServiceBuilderType);
    }

    private static bool IsExactAddServiceEndpoint(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return IsExactAssembly(definition, CoreWcfAssembly, CoreWcfVersion)
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == ServiceBuilderType
            && definition.MetadataName == "AddServiceEndpoint"
            && definition.Arity == 2
            && definition.Parameters.Length == 2
            && IsExactType(definition.Parameters[0].Type, CoreWcfAssembly, CoreWcfVersion, "CoreWCF.Channels.Binding")
            && IsExactCoreType(definition.Parameters[1].Type, FrameworkVersion, "System.String")
            && IsExactType(definition.ReturnType, CoreWcfAssembly, CoreWcfVersion, ServiceBuilderType);
    }

    private static bool IsExactAssembly(IMethodSymbol method, string assembly, string version)
        => method.ContainingAssembly?.Identity.Name == assembly
            && method.ContainingAssembly.Identity.Version?.ToString() == version;

    private static bool IsExactType(ITypeSymbol type, string assembly, string version, string metadataName)
        => type is INamedTypeSymbol named
            && named.ContainingAssembly?.Identity.Name == assembly
            && named.ContainingAssembly.Identity.Version?.ToString() == version
            && RoslynProgramIndexExtractor.GetMetadataName(named) == metadataName;

    private static bool IsExactStringArray(ITypeSymbol type)
        => type is IArrayTypeSymbol { Rank: 1, ElementType: { } element }
            && IsExactCoreType(element, FrameworkVersion, "System.String");

    private static bool IsExactCoreType(ITypeSymbol type, string version, string metadataName)
        => type is INamedTypeSymbol named
            && named.ContainingAssembly?.Identity.Version?.ToString() == version
            && named.ContainingAssembly.Identity.Name is "System.Private.CoreLib" or "System.Runtime" or "System.Runtime.Extensions"
            && RoslynProgramIndexExtractor.GetMetadataName(named) == metadataName;
}
