namespace BehaviorDocumentation.DependencyInjection.Lookalikes;

/// <summary>
/// Lookalike helper with the same simple type name and the same method names as the Microsoft DI
/// extension class but in a different namespace and assembly. Exact symbol identity must reject it;
/// raw simple-name matching must never admit it.
/// </summary>
public static class ServiceCollectionServiceExtensions
{
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddScoped<TService, TImplementation>(
        this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
        => services;
}
