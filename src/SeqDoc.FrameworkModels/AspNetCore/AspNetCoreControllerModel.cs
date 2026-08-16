using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using SeqDoc.Core.Diagnostics;
using SeqDoc.Core.Evidence;
using SeqDoc.Core.Frameworks;
using SeqDoc.Core.Identity;
using SeqDoc.Core.ProgramIndex;

namespace SeqDoc.FrameworkModels.AspNetCore;

/// <summary>
/// Versioned ASP.NET Core controller model. Discovers admitted [ApiController] attribute-routed
/// actions by joining exact Program Index type/method/attribute symbols and the controlled
/// compiler-proven method/type shape supplied by the eligibility projector. It emits deterministic
/// HTTP entry points, route bindings, and direct ControllerBase outcome facts by exact assembly,
/// assembly version, containing metadata type, metadata method name, arity, parameter ref-kind and
/// type, return type, and the supported version table. It never matches raw class/method names or
/// ProgramInvocation display targets, never guesses a route, binding, or status it cannot prove, and
/// never promotes non-exact input certainty. The eligibility projector exposes compiler facts only;
/// MVC eligibility rules live in this model. Production Roslyn projection that fills the facade is
/// deferred to C-5.
/// </summary>
public sealed class AspNetCoreControllerModel : IFrameworkBehaviorModel
{
    public const string ModelIdValue = "seqdoc.aspnetcore.controllers";
    public const string ModelVersionValue = "1.0.0";

    /// <summary>Exact fully qualified framework identities admitted by this model version.</summary>
    internal static class Identity
    {
        public const string ApiControllerAttribute = "Microsoft.AspNetCore.Mvc.ApiControllerAttribute";
        public const string RouteAttribute = "Microsoft.AspNetCore.Mvc.RouteAttribute";
        public const string NonActionAttribute = "Microsoft.AspNetCore.Mvc.NonActionAttribute";
        public const string NonControllerAttribute = "Microsoft.AspNetCore.Mvc.NonControllerAttribute";
        public const string HttpGetAttribute = "Microsoft.AspNetCore.Mvc.HttpGetAttribute";
        public const string HttpPostAttribute = "Microsoft.AspNetCore.Mvc.HttpPostAttribute";
        public const string HttpPutAttribute = "Microsoft.AspNetCore.Mvc.HttpPutAttribute";
        public const string HttpDeleteAttribute = "Microsoft.AspNetCore.Mvc.HttpDeleteAttribute";
        public const string ControllerBaseAssembly = "Microsoft.AspNetCore.Mvc.Core";
        public const string ControllerBaseType = "Microsoft.AspNetCore.Mvc.ControllerBase";
        public const string ControllerBaseAssemblyVersion = "10.0.0.0";
        public const string MvcReference = "Microsoft.AspNetCore.Mvc";
        public const string MvcCoreReference = "Microsoft.AspNetCore.Mvc.Core";
        public const string AspNetCoreAppReference = "Microsoft.AspNetCore.App";
    }

    private static readonly ImmutableArray<AdmittedOutcome> AdmittedOutcomes = ImmutableArray.Create(
        new AdmittedOutcome(HttpOutcomeHelperKind.Ok, [], "Microsoft.AspNetCore.Mvc.OkResult", 200),
        new AdmittedOutcome(HttpOutcomeHelperKind.Ok, [Param("object")], "Microsoft.AspNetCore.Mvc.OkObjectResult", 200),
        new AdmittedOutcome(HttpOutcomeHelperKind.CreatedAtAction, [Param("string"), Param("object"), Param("object")], "Microsoft.AspNetCore.Mvc.CreatedAtActionResult", 201),
        new AdmittedOutcome(HttpOutcomeHelperKind.CreatedAtAction, [Param("string"), Param("string"), Param("object"), Param("object")], "Microsoft.AspNetCore.Mvc.CreatedAtActionResult", 201),
        new AdmittedOutcome(HttpOutcomeHelperKind.BadRequest, [], "Microsoft.AspNetCore.Mvc.BadRequestResult", 400),
        new AdmittedOutcome(HttpOutcomeHelperKind.BadRequest, [Param("object")], "Microsoft.AspNetCore.Mvc.BadRequestObjectResult", 400),
        new AdmittedOutcome(HttpOutcomeHelperKind.NotFound, [], "Microsoft.AspNetCore.Mvc.NotFoundResult", 404),
        new AdmittedOutcome(HttpOutcomeHelperKind.NotFound, [Param("object")], "Microsoft.AspNetCore.Mvc.NotFoundObjectResult", 404),
        new AdmittedOutcome(HttpOutcomeHelperKind.Conflict, [], "Microsoft.AspNetCore.Mvc.ConflictResult", 409),
        new AdmittedOutcome(HttpOutcomeHelperKind.Conflict, [Param("object")], "Microsoft.AspNetCore.Mvc.ConflictObjectResult", 409),
        new AdmittedOutcome(HttpOutcomeHelperKind.StatusCode, [Param("int")], "Microsoft.AspNetCore.Mvc.StatusCodeResult", null),
        new AdmittedOutcome(HttpOutcomeHelperKind.StatusCode, [Param("int"), Param("object")], "Microsoft.AspNetCore.Mvc.ObjectResult", null));

    public FrameworkModelDescriptor Descriptor { get; } = new(
        ModelIdValue,
        ModelVersionValue,
        "ASP.NET Core Controllers",
        Order: 100);

    /// <summary>
    /// Applies when the unmodified Program Index contains exact applied ASP.NET Core attribute
    /// identities (ApiController, Route, or an admitted HTTP method attribute). ProjectKind.Web and
    /// framework references may corroborate but are never required, because the current extractor can
    /// report Web SDK libraries as Library and may omit framework references. A lookalike-only index
    /// without exact attribute identities remains non-applicable.
    /// </summary>
    public bool IsApplicable(FrameworkDetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ProgramIndex.Attributes.Any(attribute =>
            attribute.AttributeType is Identity.ApiControllerAttribute
                or Identity.RouteAttribute
                or Identity.HttpGetAttribute
                or Identity.HttpPostAttribute
                or Identity.HttpPutAttribute
                or Identity.HttpDeleteAttribute);
    }

    public ValueTask<ModelResult> AnalyzeSymbolAsync(
        SymbolDescriptor symbol,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(AnalyzeMethod(symbol, context));
    }

    public ValueTask<ModelResult> AnalyzeOperationAsync(
        OperationDescriptor operation,
        FrameworkAnalysisContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult(AnalyzeOutcome(operation, context));
    }

    private ModelResult AnalyzeMethod(SymbolDescriptor symbol, FrameworkAnalysisContext context)
    {
        if (!string.Equals(symbol.Kind, "Method", StringComparison.Ordinal))
        {
            return ModelResult.Unrecognized;
        }

        var index = context.ProgramIndex;
        var method = index.Methods.FirstOrDefault(candidate => candidate.Symbol == symbol.Id);
        if (method is null)
        {
            return ModelResult.Unrecognized;
        }

        var type = index.Types.FirstOrDefault(candidate => candidate.Id == method.ContainingType);
        if (type is null)
        {
            return ModelResult.Unrecognized;
        }

        var typeAttributes = index.Attributes
            .Where(attribute => attribute.Target == type.Id)
            .OrderBy(attribute => attribute.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        if (!typeAttributes.Any(attribute => attribute.AttributeType == Identity.ApiControllerAttribute))
        {
            return ModelResult.Unrecognized;
        }

        if (typeAttributes.Any(attribute => attribute.AttributeType == Identity.NonControllerAttribute))
        {
            // NonController is honored exactly: an otherwise ControllerBase-derived type carrying the
            // exact attribute is deliberately not a controller and emits no root.
            return ModelResult.Unrecognized;
        }

        var methodAttributes = index.Attributes
            .Where(attribute => attribute.Target == method.Symbol)
            .OrderBy(attribute => attribute.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        if (methodAttributes.Any(attribute => attribute.AttributeType == Identity.NonActionAttribute))
        {
            // NonAction is honored exactly: the method is deliberately not an action.
            return ModelResult.Unrecognized;
        }

        var httpMethodAttributes = methodAttributes
            .Where(attribute => IsAdmittedHttpMethodAttribute(attribute.AttributeType))
            .OrderBy(attribute => attribute.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        var methodRouteAttributes = methodAttributes
            .Where(attribute => attribute.AttributeType == Identity.RouteAttribute)
            .OrderBy(attribute => attribute.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        var diagnostics = new List<AnalysisDiagnostic>();
        if (httpMethodAttributes.IsEmpty)
        {
            if (methodRouteAttributes.Length > 0)
            {
                // C-1-F2: a method RouteAttribute without an admitted HTTP verb is unsupported; no
                // Any/all verb is invented.
                diagnostics.Add(AspNetCoreControllerModelDiagnostics.RouteWithoutHttpVerb(
                    context.Profile.Id,
                    method.Id.Value));
                return new ModelResult(false, diagnostics: diagnostics.ToImmutableArray());
            }

            return ModelResult.Unrecognized;
        }

        var profileId = context.Profile.Id;

        // C-1-F1: controlled eligibility facts. Missing, mismatched, or incomplete shape input fails
        // closed with a stable diagnostic and no exact root; complete-but-genuinely-ineligible
        // shapes produce no root without fabricated facts.
        if (symbol.MethodShape is null)
        {
            diagnostics.Add(AspNetCoreControllerModelDiagnostics.EligibilityShapeUnavailable(
                profileId,
                method.Id.Value));
            return new ModelResult(false, diagnostics: diagnostics.ToImmutableArray());
        }

        var shape = symbol.MethodShape;
        if (shape.MethodSymbol != method.Symbol || shape.DeclaringTypeSymbol != type.Id)
        {
            // The shape must be bound to the exact indexed method and containing type; a shape from
            // another symbol can never support this root.
            diagnostics.Add(AspNetCoreControllerModelDiagnostics.EligibilityShapeUnavailable(
                profileId,
                $"{method.Id.Value}\u001fshape-symbol-mismatch"));
            return new ModelResult(false, diagnostics: diagnostics.ToImmutableArray());
        }

        var shapeValidation = ValidateShapeCompleteness(shape, type.MetadataName);
        if (shapeValidation is not null)
        {
            diagnostics.Add(AspNetCoreControllerModelDiagnostics.EligibilityShapeUnavailable(
                profileId,
                $"{method.Id.Value}\u001f{shapeValidation}"));
            return new ModelResult(false, diagnostics: diagnostics.ToImmutableArray());
        }

        if (!IsEligibleAction(shape))
        {
            return ModelResult.Unrecognized;
        }

        var inputCertainty = symbol.Certainty;
        var effectiveCertainty = inputCertainty == CertaintyLevel.Exact ? CertaintyLevel.Exact : inputCertainty;
        if (inputCertainty != CertaintyLevel.Exact)
        {
            // C-1-F3: non-exact input certainty is never promoted.
            diagnostics.Add(AspNetCoreControllerModelDiagnostics.DegradedInputCertainty(
                profileId,
                method.Id.Value));
        }

        var controllerName = GetControllerName(type.MetadataName);
        var controllerRouteOptions = BuildControllerRouteOptions(typeAttributes, controllerName, profileId, type.Id, method.Id, diagnostics);

        var methodRouteOptions = BuildMethodRouteOptions(methodRouteAttributes, profileId, method.Id, diagnostics);

        var candidates = new List<EntryPointCandidate>();
        foreach (var attribute in httpMethodAttributes)
        {
            var httpMethod = ToHttpMethodKind(attribute.AttributeType);
            var firstArgument = FirstArgument(attribute);
            string? httpTemplate = null;
            if (firstArgument is not null)
            {
                var parsed = TryUnquoteStringLiteral(firstArgument);
                if (parsed is null)
                {
                    // Present but malformed/unquoted template: unsupported. Never invent a
                    // controller-only route from ambiguous input.
                    diagnostics.Add(AspNetCoreControllerModelDiagnostics.MalformedRouteTemplate(
                        profileId,
                        $"{method.Id.Value}\u001f{attribute.AttributeType}\u001f{firstArgument}"));
                    continue;
                }

                httpTemplate = parsed;
            }

            // C-1-F2: the action template sources are the HTTP template (when present) plus every
            // method-level RouteAttribute template. An HTTP attribute with no template contributes an
            // empty template only when no method RouteAttribute supplies the action route, so
            // [HttpGet][Route("{id}")] never also emits a controller-only route.
            var actionTemplates = new List<ActionTemplateSource>();
            if (httpTemplate is not null)
            {
                actionTemplates.Add(new ActionTemplateSource(httpTemplate, [attribute], []));
            }

            foreach (var routeOption in methodRouteOptions)
            {
                // The Route-derived template always retains the exact admitted HTTP-method attribute
                // as evidence for the verb, so [HttpGet("a")][Route("b")] keeps the proof that GET
                // serves route "b".
                actionTemplates.Add(new ActionTemplateSource(
                    routeOption.Template,
                    [attribute],
                    routeOption.SourceAttributes
                        .OrderBy(source => source.Id, StringComparer.Ordinal)
                        .ToImmutableArray()));
            }

            if (actionTemplates.Count == 0)
            {
                actionTemplates.Add(new ActionTemplateSource(string.Empty, [attribute], []));
            }

            foreach (var actionTemplate in actionTemplates)
            {
                if (actionTemplate.Template.StartsWith('~') && !actionTemplate.Template.StartsWith("~/", StringComparison.Ordinal))
                {
                    // '~' without '/' is malformed; never guess a rooted or relative route.
                    diagnostics.Add(AspNetCoreControllerModelDiagnostics.MalformedRouteTemplate(
                        profileId,
                        $"{method.Id.Value}\u001f~route\u001f{actionTemplate.Template}"));
                    continue;
                }

                if (IsRootedTemplate(actionTemplate.Template))
                {
                    // C-1-F2: rooted templates override every controller prefix exactly once and
                    // canonicalize from the application root.
                    var canonicalRoute = CanonicalizeRootedTemplate(actionTemplate.Template);
                    if (canonicalRoute.Length == 0)
                    {
                        diagnostics.Add(AspNetCoreControllerModelDiagnostics.RouteUnavailable(
                            profileId,
                            $"{method.Id.Value}\u001f{attribute.AttributeType}"));
                        continue;
                    }

                    candidates.Add(new EntryPointCandidate(
                        httpMethod,
                        canonicalRoute,
                        actionTemplate.HttpAttributes,
                        actionTemplate.RouteAttributes,
                        []));
                    continue;
                }

                foreach (var controllerRoute in controllerRouteOptions)
                {
                    var canonicalRoute = CombineRoute(controllerRoute.Template, actionTemplate.Template);
                    if (canonicalRoute.Length == 0)
                    {
                        diagnostics.Add(AspNetCoreControllerModelDiagnostics.RouteUnavailable(
                            profileId,
                            $"{method.Id.Value}\u001f{attribute.AttributeType}"));
                        continue;
                    }

                    candidates.Add(new EntryPointCandidate(
                        httpMethod,
                        canonicalRoute,
                        actionTemplate.HttpAttributes,
                        actionTemplate.RouteAttributes,
                        controllerRoute.SourceAttributes
                            .OrderBy(source => source.Id, StringComparer.Ordinal)
                            .ToImmutableArray()));
                }
            }
        }

        // Canonical dedupe: identical HTTP method + canonical route declarations emit one entry point
        // whose source-attribute evidence is merged deterministically.
        var entryPoints = candidates
            .GroupBy(candidate => (candidate.HttpMethod, candidate.CanonicalRoute))
            .Select(group => new EntryPointCandidate(
                group.Key.HttpMethod,
                group.Key.CanonicalRoute,
                group.SelectMany(candidate => candidate.HttpMethodAttributes)
                    .OrderBy(attribute => attribute.Id, StringComparer.Ordinal)
                    .ToImmutableArray(),
                group.SelectMany(candidate => candidate.MethodRouteAttributes)
                    .OrderBy(attribute => attribute.Id, StringComparer.Ordinal)
                    .ToImmutableArray(),
                group.SelectMany(candidate => candidate.ControllerRouteAttributes)
                    .OrderBy(attribute => attribute.Id, StringComparer.Ordinal)
                    .ToImmutableArray()))
            .OrderBy(candidate => candidate.HttpMethod)
            .ThenBy(candidate => candidate.CanonicalRoute, StringComparer.Ordinal)
            .ToArray();

        if (entryPoints.Length == 0)
        {
            return new ModelResult(false, diagnostics: diagnostics.ToImmutableArray());
        }

        var apiControllerAttributes = typeAttributes
            .Where(attribute => attribute.AttributeType == Identity.ApiControllerAttribute)
            .OrderBy(attribute => attribute.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        var resolved = entryPoints
            .Select(candidate => new ResolvedEntryPoint(
                candidate.HttpMethod,
                candidate.CanonicalRoute,
                StableIdentity.CreateEntryPointId(new HttpEntryPointIdentityDescriptor(
                    profileId,
                    method.Id,
                    candidate.HttpMethod,
                    candidate.CanonicalRoute)),
                candidate.HttpMethodAttributes,
                candidate.MethodRouteAttributes,
                candidate.ControllerRouteAttributes))
            .ToArray();
        var facts = new List<BehaviorFact>();
        var entryPointSibling = 0;
        foreach (var candidate in resolved)
        {
            var underlying = BuildCandidateUnderlyingEvidence(
                method,
                type,
                apiControllerAttributes,
                candidate.MethodRouteAttributes,
                candidate.ControllerRouteAttributes,
                candidate.HttpMethodAttributes);
            facts.Add(new HttpEntryPointFact
            {
                Id = CreateBehaviorFactId(
                    profileId,
                    "http-entry-point",
                    new SymbolBehaviorFactAnchor(type.Project, symbol.Id),
                    entryPointSibling++),
                EntryPointId = candidate.EntryPointId,
                RootMethod = method.Id,
                HttpMethod = candidate.HttpMethod,
                CanonicalRoute = candidate.CanonicalRoute,
                OperationKey = $"{HttpMethodCanonicalToken.Get(candidate.HttpMethod)} {candidate.CanonicalRoute}",
                Evidence = CreateModelEvidence($"entry-point:{candidate.EntryPointId.Value}", underlying, effectiveCertainty),
                Certainty = effectiveCertainty,
            });
        }

        var bindingSibling = 0;
        foreach (var candidate in resolved)
        {
            var placeholders = ExtractPlaceholderNames(candidate.CanonicalRoute);
            var underlying = BuildCandidateUnderlyingEvidence(
                method,
                type,
                apiControllerAttributes,
                candidate.MethodRouteAttributes,
                candidate.ControllerRouteAttributes,
                candidate.HttpMethodAttributes);
            foreach (var parameter in method.Parameters)
            {
                var matched = placeholders.Contains(parameter.Name, StringComparer.Ordinal);
                var bindingCertainty = matched ? effectiveCertainty : CertaintyLevel.Unknown;
                facts.Add(new HttpRequestBindingFact
                {
                    Id = CreateBehaviorFactId(
                        profileId,
                        "http-binding",
                        new SymbolBehaviorFactAnchor(type.Project, symbol.Id),
                        bindingSibling++),
                    EntryPointId = candidate.EntryPointId,
                    RootMethod = method.Id,
                    ParameterName = parameter.Name,
                    BindingKind = matched ? HttpBindingKind.Route : HttpBindingKind.Unknown,
                    RoutePlaceholder = matched ? parameter.Name : null,
                    Evidence = CreateModelEvidence(
                        $"binding:{candidate.EntryPointId.Value}:{parameter.Name}",
                        underlying,
                        bindingCertainty),
                    Certainty = bindingCertainty,
                });
            }
        }

        return new ModelResult(true, facts: facts.ToImmutableArray(), diagnostics: diagnostics.ToImmutableArray());
    }

    private ModelResult AnalyzeOutcome(OperationDescriptor operation, FrameworkAnalysisContext context)
    {
        if (!string.Equals(operation.Kind, "Invocation", StringComparison.Ordinal)
            || operation.TargetIdentity is null)
        {
            return ModelResult.Unrecognized;
        }

        var identity = operation.TargetIdentity;
        if (!string.Equals(identity.AssemblyIdentity, Identity.ControllerBaseAssembly, StringComparison.Ordinal)
            || !string.Equals(identity.AssemblyVersion, Identity.ControllerBaseAssemblyVersion, StringComparison.Ordinal)
            || !string.Equals(identity.ContainingMetadataType, Identity.ControllerBaseType, StringComparison.Ordinal)
            || identity.GenericArity != 0)
        {
            // A different assembly, a missing/unsupported assembly version, or a lookalike containing
            // type never produces an exact outcome; nothing is guessed and no diagnostic is emitted.
            return ModelResult.Unrecognized;
        }

        var helper = ToOutcomeHelperKind(identity.MethodMetadataName);
        if (helper is null)
        {
            return ModelResult.Unrecognized;
        }

        var admitted = AdmittedOutcomes.FirstOrDefault(outcome =>
            outcome.Helper == helper
            && ParameterSignaturesEqual(outcome.ParameterTypes, identity.Parameters)
            && string.Equals(outcome.ReturnType, identity.ReturnType, StringComparison.Ordinal));
        if (admitted is null)
        {
            return new ModelResult(false, diagnostics: [AspNetCoreControllerModelDiagnostics.UnsupportedOutcomeOverload(
                context.Profile.Id,
                BuildOutcomeSubject(operation, identity))]);
        }

        int statusCode;
        if (helper.Value == HttpOutcomeHelperKind.StatusCode)
        {
            if (!TryResolveStatusCode(operation.ConstantArguments, out statusCode))
            {
                return new ModelResult(false, diagnostics: [AspNetCoreControllerModelDiagnostics.NonConstantStatusCode(
                    context.Profile.Id,
                    BuildOutcomeSubject(operation, identity))]);
            }
        }
        else
        {
            statusCode = admitted.StatusCode!.Value;
        }

        var inputCertainty = operation.Certainty;
        var effectiveCertainty = inputCertainty == CertaintyLevel.Exact ? CertaintyLevel.Exact : inputCertainty;
        var diagnostics = ImmutableArray<AnalysisDiagnostic>.Empty;
        if (inputCertainty != CertaintyLevel.Exact)
        {
            // C-1-F3: non-exact operation certainty is never promoted.
            diagnostics = [AspNetCoreControllerModelDiagnostics.DegradedInputCertainty(
                context.Profile.Id,
                operation.Id.Value)];
        }

        var fact = new HttpDirectOutcomeFact
        {
            Id = CreateBehaviorFactId(
                context.Profile.Id,
                "http-direct-outcome",
                new OperationBehaviorFactAnchor(operation.Method, operation.Id),
                0),
            RootMethod = operation.Method,
            Operation = operation.Id,
            HelperKind = helper.Value,
            StatusCode = statusCode,
            Evidence = CreateModelEvidence(
                $"outcome:{operation.Id.Value}:{helper.Value}:{statusCode}",
                operation.Evidence,
                effectiveCertainty),
            Certainty = effectiveCertainty,
        };
        return new ModelResult(true, facts: [fact], diagnostics: diagnostics);
    }

    private static bool IsEligibleAction(FrameworkMethodShape shape)
        => shape.IsOrdinary
            && shape.IsPublic
            && !shape.IsStatic
            && !shape.IsAbstract
            && shape.GenericArity == 0
            && IsEligibleController(shape.DeclaringType);

    /// <summary>
    /// Returns a stable reason when the shape is incomplete or inconsistent, or null when it is
    /// complete enough to evaluate MVC eligibility. Missing, defaulted, or mismatched shape data
    /// fails closed with the eligibility diagnostic; genuinely ineligible but complete shapes are
    /// handled separately as Unrecognized without fabricated facts.
    /// </summary>
    private static string? ValidateShapeCompleteness(FrameworkMethodShape shape, string expectedDeclaringMetadataName)
    {
        if (shape.GenericArity < 0)
        {
            return "negative-method-arity";
        }

        var declaring = shape.DeclaringType;
        if (declaring.GenericArity < 0)
        {
            return "negative-type-arity";
        }

        if (declaring.BaseTypeChain.IsDefault)
        {
            return "uninitialized-base-chain";
        }

        if (declaring.BaseTypeChain.Any(identity =>
                string.IsNullOrWhiteSpace(identity.AssemblyIdentity)
                || string.IsNullOrWhiteSpace(identity.AssemblyVersion)
                || string.IsNullOrWhiteSpace(identity.MetadataName)))
        {
            return "blank-base-type-identity";
        }

        if (string.IsNullOrWhiteSpace(declaring.Identity.AssemblyIdentity)
            || string.IsNullOrWhiteSpace(declaring.Identity.AssemblyVersion)
            || string.IsNullOrWhiteSpace(declaring.Identity.MetadataName))
        {
            return "blank-declaring-type-identity";
        }

        if (!string.Equals(declaring.Identity.MetadataName, expectedDeclaringMetadataName, StringComparison.Ordinal))
        {
            return "declaring-metadata-name-mismatch";
        }

        return null;
    }

    private static bool IsEligibleController(FrameworkTypeShape type)
        => type.IsClass
            && type.IsPublicOrNestedPublic
            && !type.IsAbstract
            && !type.IsStatic
            && type.GenericArity == 0
            && type.BaseTypeChain.Any(HasExactControllerBaseIdentity);

    private static bool HasExactControllerBaseIdentity(FrameworkTypeIdentity identity)
        => string.Equals(identity.AssemblyIdentity, Identity.ControllerBaseAssembly, StringComparison.Ordinal)
            && string.Equals(identity.AssemblyVersion, Identity.ControllerBaseAssemblyVersion, StringComparison.Ordinal)
            && string.Equals(identity.MetadataName, Identity.ControllerBaseType, StringComparison.Ordinal);

    private static bool IsAdmittedHttpMethodAttribute(string attributeType)
        => attributeType is Identity.HttpGetAttribute
            or Identity.HttpPostAttribute
            or Identity.HttpPutAttribute
            or Identity.HttpDeleteAttribute;

    private static HttpMethodKind ToHttpMethodKind(string attributeType)
        => attributeType switch
        {
            Identity.HttpGetAttribute => HttpMethodKind.Get,
            Identity.HttpPostAttribute => HttpMethodKind.Post,
            Identity.HttpPutAttribute => HttpMethodKind.Put,
            Identity.HttpDeleteAttribute => HttpMethodKind.Delete,
            _ => throw new ArgumentOutOfRangeException(
                nameof(attributeType),
                $"Unadmitted HTTP method attribute '{attributeType}'."),
        };

    private static HttpOutcomeHelperKind? ToOutcomeHelperKind(string methodMetadataName)
        => methodMetadataName switch
        {
            "Ok" => HttpOutcomeHelperKind.Ok,
            "CreatedAtAction" => HttpOutcomeHelperKind.CreatedAtAction,
            "BadRequest" => HttpOutcomeHelperKind.BadRequest,
            "NotFound" => HttpOutcomeHelperKind.NotFound,
            "Conflict" => HttpOutcomeHelperKind.Conflict,
            "StatusCode" => HttpOutcomeHelperKind.StatusCode,
            _ => null,
        };

    private static string? FirstArgument(ProgramAttributeApplication attribute)
        => attribute.Arguments.IsEmpty ? null : attribute.Arguments[0];

    private static ImmutableArray<ControllerRouteOption> BuildControllerRouteOptions(
        ImmutableArray<ProgramAttributeApplication> typeAttributes,
        string controllerName,
        CompilationProfileId profileId,
        SymbolId typeId,
        MethodId methodId,
        List<AnalysisDiagnostic> diagnostics)
    {
        var controllerRouteAttributes = typeAttributes
            .Where(attribute => attribute.AttributeType == Identity.RouteAttribute)
            .OrderBy(attribute => attribute.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        var options = new List<ControllerRouteOption>();
        foreach (var attribute in controllerRouteAttributes)
        {
            var template = TryUnquoteStringLiteral(FirstArgument(attribute));
            if (template is null)
            {
                // A present but malformed/unquoted controller route is unsupported and is never
                // replaced with an empty prefix.
                diagnostics.Add(AspNetCoreControllerModelDiagnostics.MalformedRouteTemplate(
                    profileId,
                    $"{typeId.Value}\u001f{attribute.AttributeType}\u001f{FirstArgument(attribute) ?? string.Empty}"));
                continue;
            }

            var substituted = SubstituteControllerToken(template, controllerName);
            var existing = options.FirstOrDefault(option => option.Template == substituted);
            if (existing is not null)
            {
                existing.SourceAttributes.Add(attribute);
            }
            else
            {
                options.Add(new ControllerRouteOption(substituted, attribute));
            }
        }

        if (controllerRouteAttributes.IsEmpty)
        {
            // No controller RouteAttribute at all: an empty controller prefix is valid.
            return [new ControllerRouteOption()];
        }

        return options
            .OrderBy(option => option.Template, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<MethodRouteOption> BuildMethodRouteOptions(
        ImmutableArray<ProgramAttributeApplication> methodRouteAttributes,
        CompilationProfileId profileId,
        MethodId methodId,
        List<AnalysisDiagnostic> diagnostics)
    {
        var options = new List<MethodRouteOption>();
        foreach (var attribute in methodRouteAttributes)
        {
            var template = TryUnquoteStringLiteral(FirstArgument(attribute));
            if (template is null)
            {
                diagnostics.Add(AspNetCoreControllerModelDiagnostics.MalformedRouteTemplate(
                    profileId,
                    $"{methodId.Value}\u001f{attribute.AttributeType}\u001f{FirstArgument(attribute) ?? string.Empty}"));
                continue;
            }

            var existing = options.FirstOrDefault(option => option.Template == template);
            if (existing is not null)
            {
                existing.SourceAttributes.Add(attribute);
            }
            else
            {
                options.Add(new MethodRouteOption(template, attribute));
            }
        }

        return options
            .OrderBy(option => option.Template, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    /// Decodes a C# string literal as the Roslyn index stores it (quoted with escapes) back to its
    /// canonical route template value. Unknown escapes or unquoted values are rejected so no route is
    /// manufactured from ambiguous input.
    /// </summary>
    private static string? TryUnquoteStringLiteral(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            return null;
        }

        var inner = value.Substring(1, value.Length - 2);
        var builder = new StringBuilder(inner.Length);
        for (var index = 0; index < inner.Length; index++)
        {
            var current = inner[index];
            if (current != '\\')
            {
                builder.Append(current);
                continue;
            }

            if (index + 1 >= inner.Length)
            {
                return null;
            }

            var escaped = inner[++index];
            switch (escaped)
            {
                case '\\':
                case '"':
                    builder.Append(escaped);
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                default:
                    return null;
            }
        }

        return builder.ToString();
    }

    private static string SubstituteControllerToken(string template, string controllerName)
        => template.Replace("[controller]", controllerName, StringComparison.Ordinal);

    private static string GetControllerName(string metadataName)
    {
        var simpleName = metadataName;
        var separator = simpleName.LastIndexOfAny(['.', '+']);
        if (separator >= 0)
        {
            simpleName = simpleName[(separator + 1)..];
        }

        const string suffix = "Controller";
        return simpleName.EndsWith(suffix, StringComparison.Ordinal)
            ? simpleName[..^suffix.Length]
            : simpleName;
    }

    private static string CombineRoute(string controllerRoute, string actionTemplate)
    {
        var segments = new List<string>(2);
        var normalizedController = controllerRoute.Trim('/');
        var normalizedAction = actionTemplate.Trim('/');
        if (normalizedController.Length > 0)
        {
            segments.Add(normalizedController);
        }

        if (normalizedAction.Length > 0)
        {
            segments.Add(normalizedAction);
        }

        return string.Join("/", segments);
    }

    private static bool IsRootedTemplate(string template)
        => template.StartsWith('/') || template.StartsWith("~/", StringComparison.Ordinal);

    private static string CanonicalizeRootedTemplate(string template)
    {
        var value = template;
        if (value.StartsWith("~/", StringComparison.Ordinal))
        {
            value = value[2..];
        }
        else if (value.StartsWith('/'))
        {
            value = value[1..];
        }

        return value.Trim('/');
    }

    internal static ImmutableArray<string> ExtractPlaceholderNames(string route)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        for (var index = 0; index < route.Length; index++)
        {
            if (route[index] != '{')
            {
                continue;
            }

            var end = route.IndexOf('}', index + 1);
            if (end < 0)
            {
                continue;
            }

            var body = route.Substring(index + 1, end - index - 1);
            if (body.Length == 0)
            {
                continue;
            }

            var name = body.Split(':', 2)[0].Trim();
            if (name.Length > 0)
            {
                names.Add(name);
            }

            index = end;
        }

        return names.ToImmutable();
    }

    private static ImmutableArray<EvidenceRef> BuildCandidateUnderlyingEvidence(
        ProgramMethod method,
        ProgramType type,
        ImmutableArray<ProgramAttributeApplication> apiControllerAttributes,
        ImmutableArray<ProgramAttributeApplication> methodRouteAttributes,
        ImmutableArray<ProgramAttributeApplication> controllerRouteAttributes,
        ImmutableArray<ProgramAttributeApplication> httpMethodAttributes)
    {
        var builder = ImmutableArray.CreateBuilder<EvidenceRef>();
        builder.AddRange(method.Evidence);
        builder.AddRange(type.Evidence);
        foreach (var attribute in apiControllerAttributes)
        {
            builder.AddRange(attribute.Evidence);
        }

        foreach (var attribute in methodRouteAttributes)
        {
            builder.AddRange(attribute.Evidence);
        }

        foreach (var attribute in controllerRouteAttributes)
        {
            builder.AddRange(attribute.Evidence);
        }

        foreach (var attribute in httpMethodAttributes)
        {
            builder.AddRange(attribute.Evidence);
        }

        return builder
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    /// <summary>
    /// Builds the single framework-model evidence record for one fact. The evidence identity hashes
    /// the producing descriptor, a stable fact/route/outcome subject, the effective certainty, and
    /// the complete canonical underlying evidence-ID sequence, so records with different payloads
    /// never share one identity while semantically identical evidence remains deterministic.
    /// </summary>
    private ImmutableArray<EvidenceRef> CreateModelEvidence(
        string subject,
        ImmutableArray<EvidenceRef> underlying,
        CertaintyLevel certainty)
    {
        var canonical = underlying
            .DistinctBy(item => item.Id.Value)
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .ToImmutableArray();
        var artifact = $"{Descriptor.ModelId}:{Descriptor.Version}";
        var evidencePayload = $"{subject}\u001f{string.Join('\u001f', canonical.Select(item => item.Id.Value))}";
        var id = StableIdentity.CreateEvidenceIdV2(new EvidenceIdentityDescriptor(
            EvidenceKind.FrameworkModel,
            artifact,
            null,
            null,
            null,
            null,
            certainty,
            Descriptor.ModelId,
            Descriptor.Version,
            Detail: evidencePayload));
        return
        [
            new EvidenceRef(
                id,
                EvidenceKind.FrameworkModel,
                artifact,
                range: null,
                symbol: null,
                detail: evidencePayload,
                certainty,
                canonical,
                Descriptor.ModelId,
                Descriptor.Version),
        ];
    }

    private BehaviorFactId CreateBehaviorFactId(
        CompilationProfileId profileId,
        string factKind,
        BehaviorFactAnchor anchor,
        int siblingOrdinal)
        => StableIdentity.CreateBehaviorFactId(new BehaviorFactIdentityDescriptor(
            profileId,
            Descriptor.ModelId,
            Descriptor.Version,
            factKind,
            anchor,
            siblingOrdinal));

    private static string NormalizeParameterType(string fullyQualifiedType)
    {
        var type = fullyQualifiedType.TrimEnd('?');
        return type switch
        {
            "System.Object" => "object",
            "System.String" => "string",
            "System.Int32" => "int",
            "System.Boolean" => "bool",
            _ => type,
        };
    }

    private static ParameterIdentityDescriptor Param(string normalizedType)
        => new(ParameterRefKind.None, normalizedType);

    private static bool ParameterSignaturesEqual(
        ImmutableArray<ParameterIdentityDescriptor> admitted,
        ImmutableArray<ParameterIdentityDescriptor> actual)
    {
        if (admitted.Length != actual.Length)
        {
            return false;
        }

        for (var index = 0; index < admitted.Length; index++)
        {
            if (admitted[index].RefKind != actual[index].RefKind
                || !string.Equals(
                    NormalizeParameterType(admitted[index].FullyQualifiedType),
                    NormalizeParameterType(actual[index].FullyQualifiedType),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildOutcomeSubject(OperationDescriptor operation, FrameworkMethodIdentity identity)
    {
        var parameters = identity.Parameters
            .Select(parameter => $"{parameter.RefKind}:{NormalizeParameterType(parameter.FullyQualifiedType)}");
        return string.Join(
            '\u001f',
            operation.Id.Value,
            identity.AssemblyIdentity,
            identity.ContainingMetadataType,
            identity.MethodMetadataName,
            identity.GenericArity,
            string.Join(",", parameters));
    }

    /// <summary>
    /// Resolves an exact status from a StatusCode helper only when the compiler proved exactly one
    /// constant argument at ordinal 0 whose fully qualified type is an integer. Unrelated constants
    /// at other ordinals are permitted; missing, duplicate, wrong-ordinal, wrong-type, overflow, and
    /// non-integer ordinal-zero values are all rejected so no status is guessed.
    /// </summary>
    private static bool TryResolveStatusCode(ImmutableArray<CompilerProvenArgument> arguments, out int statusCode)
    {
        statusCode = 0;
        if (arguments.IsDefaultOrEmpty)
        {
            return false;
        }

        var ordinalZero = arguments.Where(argument => argument.Ordinal == 0).ToArray();
        if (ordinalZero.Length != 1)
        {
            return false;
        }

        var argument = ordinalZero[0];
        if (!string.Equals(NormalizeParameterType(argument.FullyQualifiedType), "int", StringComparison.Ordinal)
            || !int.TryParse(argument.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out statusCode))
        {
            return false;
        }

        return true;
    }

    private sealed class ControllerRouteOption
    {
        public ControllerRouteOption()
        {
        }

        public ControllerRouteOption(string template, ProgramAttributeApplication sourceAttribute)
        {
            Template = template;
            SourceAttributes.Add(sourceAttribute);
        }

        public string Template { get; } = string.Empty;

        public List<ProgramAttributeApplication> SourceAttributes { get; } = [];
    }

    private sealed class MethodRouteOption
    {
        public MethodRouteOption(string template, ProgramAttributeApplication sourceAttribute)
        {
            Template = template;
            SourceAttributes.Add(sourceAttribute);
        }

        public string Template { get; }

        public List<ProgramAttributeApplication> SourceAttributes { get; } = [];
    }

    private sealed record ActionTemplateSource(
        string Template,
        ImmutableArray<ProgramAttributeApplication> HttpAttributes,
        ImmutableArray<ProgramAttributeApplication> RouteAttributes);

    private sealed record EntryPointCandidate(
        HttpMethodKind HttpMethod,
        string CanonicalRoute,
        ImmutableArray<ProgramAttributeApplication> HttpMethodAttributes,
        ImmutableArray<ProgramAttributeApplication> MethodRouteAttributes,
        ImmutableArray<ProgramAttributeApplication> ControllerRouteAttributes);

    private sealed record ResolvedEntryPoint(
        HttpMethodKind HttpMethod,
        string CanonicalRoute,
        EntryPointId EntryPointId,
        ImmutableArray<ProgramAttributeApplication> HttpMethodAttributes,
        ImmutableArray<ProgramAttributeApplication> MethodRouteAttributes,
        ImmutableArray<ProgramAttributeApplication> ControllerRouteAttributes);

    private sealed record AdmittedOutcome(
        HttpOutcomeHelperKind Helper,
        ImmutableArray<ParameterIdentityDescriptor> ParameterTypes,
        string ReturnType,
        int? StatusCode);
}
