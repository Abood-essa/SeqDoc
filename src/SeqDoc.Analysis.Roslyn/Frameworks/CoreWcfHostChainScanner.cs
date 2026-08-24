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
            var configureMethod = startupType.GetMembers("Configure")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(candidate =>
                    !candidate.IsStatic
                    && candidate.Parameters.Length == 1
                    && IsExactType(candidate.Parameters[0].Type, AspNetCoreHttpAbstractionsAssembly, ApplicationBuilderType));
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
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (!documents.ContainsKey(tree))
            {
                continue;
            }

            var model = compilation.GetSemanticModel(tree);
            foreach (var invocationSyntax in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetOperation(invocationSyntax) is not IInvocationOperation configureCall
                    || !IsExactConfigureWebHostDefaults(configureCall.TargetMethod))
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

                var chainEvidence = CreateEvidence(createBuilderCall.Syntax, documents)
                    .Concat(CreateEvidence(configureCall.Syntax, documents))
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
        => root.DescendantsAndSelf().OfType<IInvocationOperation>();

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

    private static bool IsExactConfigureWebHostDefaults(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return definition.ContainingAssembly?.Identity.Name == AspNetCoreAssembly
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == GenericHostBuilderExtensionsType
            && definition.MetadataName == "ConfigureWebHostDefaults"
            && definition.Arity == 0
            && definition.Parameters.Length == 2
            && IsExactType(definition.Parameters[0].Type, HostingAbstractionsAssembly, HostBuilderType);
    }

    private static bool IsExactCreateDefaultBuilder(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return definition.ContainingAssembly?.Identity.Name == HostingAssembly
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == HostType
            && definition.MetadataName == "CreateDefaultBuilder"
            && definition.Arity == 0
            && definition.Parameters.Length == 1
            && definition.Parameters[0].Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String };
    }

    private static bool IsExactUseStartup(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return definition.ContainingAssembly?.Identity.Name == AspNetCoreHostingAssembly
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == WebHostBuilderExtensionsType
            && definition.MetadataName == "UseStartup"
            && definition.Arity == 1
            && definition.Parameters.Length == 1
            && IsExactType(definition.Parameters[0].Type, AspNetCoreHostingAbstractionsAssembly, WebHostBuilderType);
    }

    private static bool IsExactUseServiceModel(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return definition.ContainingAssembly?.Identity.Name == CoreWcfAssembly
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == ServiceModelApplicationBuilderExtensionsType
            && definition.MetadataName == "UseServiceModel"
            && definition.Arity == 0
            && definition.Parameters.Length == 2
            && IsExactType(definition.Parameters[0].Type, AspNetCoreHttpAbstractionsAssembly, ApplicationBuilderType)
            && IsExactActionOfServiceBuilder(definition.Parameters[1].Type);
    }

    // System.Action<T>'s exact compile-time containing assembly varies by target framework/reference
    // assembly facade (the same reason GeneratedCodeAttribute's compile-time assembly differs from its
    // run-time one); the security-relevant fact here is the constructed type argument identity, which is
    // checked exactly, so the delegate's own namespace/name/arity is enough to anchor it.
    private static bool IsExactActionOfServiceBuilder(ITypeSymbol type)
        => type is INamedTypeSymbol { MetadataName: "Action`1", Arity: 1 } action
            && action.ContainingNamespace.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "System"
            && IsExactType(action.TypeArguments[0], CoreWcfAssembly, ServiceBuilderType);

    private static bool IsExactAddService(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return definition.ContainingAssembly?.Identity.Name == CoreWcfAssembly
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == ServiceBuilderType
            && definition.MetadataName == "AddService"
            && definition.Arity == 1
            && definition.Parameters.Length == 0;
    }

    private static bool IsExactAddServiceEndpoint(IMethodSymbol target)
    {
        var definition = target.OriginalDefinition;
        return definition.ContainingAssembly?.Identity.Name == CoreWcfAssembly
            && RoslynProgramIndexExtractor.GetMetadataName(definition.ContainingType) == ServiceBuilderType
            && definition.MetadataName == "AddServiceEndpoint"
            && definition.Arity == 2
            && definition.Parameters.Length == 2
            && definition.Parameters[0].Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "CoreWCF.Channels.Binding"
            && definition.Parameters[1].Type.ToDisplayString(RoslynProgramIndexExtractor.IdentityFormat) == "System.String";
    }

    private static bool IsExactType(ITypeSymbol type, string assembly, string metadataName)
        => type is INamedTypeSymbol named
            && named.ContainingAssembly?.Identity.Name == assembly
            && RoslynProgramIndexExtractor.GetMetadataName(named) == metadataName;
}
