using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BehaviorDocumentation.DependencyInjection.Services;

/// <summary>
/// Real method body that carries the exact Microsoft DI registrations so the behavior extractor can
/// walk them (top-level statements in <c>&lt;Main&gt;$</c> have no method declaration and are not
/// body-extracted). The admitted registrations are the receiver-only two-type-argument generic
/// extension forms; every other line exercises a fail-closed unsupported form.
/// </summary>
public static class ServiceRegistration
{
    public static void Register(IServiceCollection services)
    {
        // Admitted exact registrations on Microsoft IServiceCollection.
        services.AddScoped<IGadgetStore, GadgetStore>();
        services.AddSingleton<IClock, SystemClock>();
        // A second distinct registration for the same service; neither is selected.
        services.AddScoped<IGadgetStore, MemoryGadgetStore>();

        // Unsupported forms must fail closed: factory, instance, non-generic open-generic,
        // collection, keyed, TryAdd, and a lookalike helper.
        services.AddScoped<IGadgetStore>(sp => new GadgetStore());
        services.AddScoped<IGadgetStore, GadgetStore>(sp => new GadgetStore());
        services.AddScoped(typeof(IGadgetStore), typeof(GadgetStore));
        services.AddScoped<IEnumerable<IGadgetStore>, GadgetStoreCollection>();
        services.AddKeyedScoped<IGadgetStore, GadgetStore>("primary");
        services.TryAddScoped<IGadgetStore, GadgetStore>();
        BehaviorDocumentation.DependencyInjection.Lookalikes.ServiceCollectionServiceExtensions
            .AddScoped<IGadgetStore, GadgetStore>(services);
    }
}
