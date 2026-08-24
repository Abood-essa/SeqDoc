using CoreWCF;
using CoreWCF.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CoreWcfServices;

public sealed class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddServiceModelServices();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseServiceModel(builder =>
        {
            // Only CalculatorService is registered and given a dispatchable endpoint.
            // ExplicitCalculatorService (Services/CalculatorService.cs) has the exact same compiler-proven
            // contract/operation/body capability but is deliberately never registered here, so it proves
            // the capability-without-registration boundary: no executable root, no execution wording.
            builder.AddService<CalculatorService>()
                .AddServiceEndpoint<CalculatorService, ICalculatorService>(new BasicHttpBinding(), "/CalculatorService/basicHttp");
        });
    }
}
