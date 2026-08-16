using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AdvancedAnalysis.ConditionalDependencyInjection.Services;

// The explicit Microsoft.AspNetCore.Builder, Microsoft.Extensions.Configuration, and
// Microsoft.Extensions.DependencyInjection usings keep this file self-contained: the relocation test
// copies the fixture without the repository Directory.Build.props, so WebApplication,
// ConfigurationBinder.GetValue<bool>, AddScoped/AddKeyedScoped/TryAddScoped, and MapControllers must
// resolve from the file itself rather than from inherited implicit usings.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

// Positive: the exact ConfigurationBinder.GetValue<bool> local flows directly into one if/else whose
// true arm registers exactly one IStorageService implementation and whose false arm registers exactly
// one opposite implementation. accepted contract must project one complete alternative group for this pair.
bool useMemoryStorage = builder.Configuration.GetValue<bool>("Storage:UseMemoryStorage");
if (useMemoryStorage)
{
    builder.Services.AddScoped<IStorageService, MemoryStorageService>();
}
else
{
    builder.Services.AddScoped<IStorageService, FileStorageService>();
}

// Negative: two independent if statements with different condition operations never form one group.
bool useAudit = builder.Configuration.GetValue<bool>("FeatureToggles:UseAudit");
if (useAudit)
{
    builder.Services.AddScoped<IAuditService, ConsoleAuditService>();
}

bool useAuditFile = builder.Configuration.GetValue<bool>("FeatureToggles:UseAuditFile");
if (useAuditFile)
{
    builder.Services.AddScoped<IAuditService, FileAuditService>();
}

// Negative: a single registration with no else arm never forms a group.
bool useCache = builder.Configuration.GetValue<bool>("FeatureToggles:UseCache");
if (useCache)
{
    builder.Services.AddScoped<ICacheService, MemoryCacheService>();
}

// Negative: two registrations in the SAME (true) arm with no opposite-arm registration never form a
// group because no complete mutually exclusive alternative exists.
bool useSms = builder.Configuration.GetValue<bool>("FeatureToggles:UseSms");
if (useSms)
{
    builder.Services.AddScoped<ISmsService, TwilioSmsService>();
    builder.Services.AddScoped<ISmsService, VonageSmsService>();
}

// Negative: the same implementation appears in BOTH arms and the true arm adds a second registration;
// the service type has no exact one-per-arm exclusive pair.
bool useBackup = builder.Configuration.GetValue<bool>("FeatureToggles:UseBackup");
if (useBackup)
{
    builder.Services.AddScoped<IBackupService, LocalBackupService>();
    builder.Services.AddScoped<IBackupService, CloudBackupService>();
}
else
{
    builder.Services.AddScoped<IBackupService, CloudBackupService>();
}

// Negative: an UNGUARDED registration of the same service type sits outside the if/else, so the
// alternative group can never account for the complete registration set.
builder.Services.AddScoped<INotificationService, SmsNotificationService>();
bool usePriority = builder.Configuration.GetValue<bool>("FeatureToggles:UsePriority");
if (usePriority)
{
    builder.Services.AddScoped<INotificationService, PriorityNotificationService>();
}
else
{
    builder.Services.AddScoped<INotificationService, BatchNotificationService>();
}

// Negative: unsupported DI shapes (keyed, TryAdd, factory) never produce registration or arm facts
// and therefore never group.
bool useKeyed = builder.Configuration.GetValue<bool>("FeatureToggles:UseKeyed");
if (useKeyed)
{
    builder.Services.AddKeyedScoped<IKeyedService, AlphaKeyedService>("alpha");
}
else
{
    builder.Services.AddKeyedScoped<IKeyedService, BetaKeyedService>("beta");
}

builder.Services.TryAddScoped<ITryService, TryServiceImplementation>();

bool useFactory = builder.Configuration.GetValue<bool>("FeatureToggles:UseFactory");
if (useFactory)
{
    builder.Services.AddScoped<IWidgetService>(_ => new WidgetService());
}
else
{
    builder.Services.AddScoped<IWidgetService>(_ => new OtherWidgetService());
}

// Negative: the condition is a helper call, not an admitted direct-local boolean, so even though the
// registrations are exact the arm facts never project and no group forms.
if (DiShapes.ComputeChoice())
{
    builder.Services.AddScoped<IPolicyService, StrictPolicyService>();
}
else
{
    builder.Services.AddScoped<IPolicyService, RelaxedPolicyService>();
}

// Negative (review regression): a registration nested inside an inner if with a non-admitted condition
// is never directly enclosed by the outer arm; the collector must stop at the nested control
// boundary instead of attributing it to the outer true arm and forming a false group.
bool usePrimary = builder.Configuration.GetValue<bool>("Storage:UsePrimary");
if (usePrimary)
{
    if (DiShapes.ComputeChoice())
    {
        builder.Services.AddScoped<IFallbackService, SecondaryAwareService>();
    }
}
else
{
    builder.Services.AddScoped<IFallbackService, PrimaryBlindService>();
}

// Negative (review regression): a registration nested inside a loop is not guaranteed by the outer
// arm; no group may form.
bool useLoop = builder.Configuration.GetValue<bool>("Storage:UseLoop");
if (useLoop)
{
    for (int i = 0; i < 3; i++)
    {
        builder.Services.AddScoped<ILoopService, LoopScopedService>();
    }
}
else
{
    builder.Services.AddScoped<ILoopService, NonLoopService>();
}

// Negative (review regression): a local reassigned after the admitted read is not a single-write
// direct local, so no exact alternatives may form even though the read itself stays admitted.
bool reassignedToggle = builder.Configuration.GetValue<bool>("Storage:UseReassigned");
reassignedToggle = DiShapes.ComputeChoice();
if (reassignedToggle)
{
    builder.Services.AddScoped<IReassignedService, ReassignedTrueService>();
}
else
{
    builder.Services.AddScoped<IReassignedService, ReassignedFalseService>();
}

// Negative (review regression): ref/out escapes before the condition disqualify the direct-local shape.
bool refEscapedToggle = builder.Configuration.GetValue<bool>("Storage:UseRefEscaped");
DiShapes.Mutate(ref refEscapedToggle);
if (refEscapedToggle)
{
    builder.Services.AddScoped<IRefEscapedService, RefEscapedTrueService>();
}
else
{
    builder.Services.AddScoped<IRefEscapedService, RefEscapedFalseService>();
}

bool outEscapedToggle = builder.Configuration.GetValue<bool>("Storage:UseOutEscaped");
DiShapes.Replace(out outEscapedToggle);
if (outEscapedToggle)
{
    builder.Services.AddScoped<IOutEscapedService, OutEscapedTrueService>();
}
else
{
    builder.Services.AddScoped<IOutEscapedService, OutEscapedFalseService>();
}

// Negative: top-level-only authority. The helper method contains an admitted read and an exact
// if/else with exact registrations, but because Method Flow is the sole control authority inside
// extracted methods, accepted contract must never project companion arm facts there.
DiShapes.RegisterAlternativeSinks(builder.Services, builder.Configuration);

var app = builder.Build();
app.MapControllers();
await app.RunAsync();
