using CoreWCF;
using CoreWCF.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace CoreWcfServices;

// Dead-helper negative: this method contains an exact AddServiceEndpoint<TService,TContract> call with
// a real compiler-proven shape, but the method is never invoked from the admitted Configure/UseServiceModel
// chain (indeed, never invoked from anywhere). CoreWcfHostChainScanner only walks the operation tree of
// the Configure method actually selected by an admitted UseStartup<TStartup>() call, so this call is
// structurally never visited — it must never contribute registration evidence.
public static class UnusedRegistrationHelper
{
    public static void NeverCalled(IApplicationBuilder app)
    {
        app.UseServiceModel(builder =>
        {
            builder.AddService<CalculatorService>()
                .AddServiceEndpoint<CalculatorService, ICalculatorService>(new BasicHttpBinding(), "/CalculatorService/deadHelper");
        });
    }
}

// Unexecuted full-host negative: this is a complete configured host chain, but the returned builder is
// never Build/Run/RunAsync-consumed. Configuration alone must not prove that the service is executable.
public static class UnbuiltHostChain
{
    public static IHostBuilder Create(string[] args)
        => Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<UnbuiltStartup>());
}

public sealed class UnbuiltStartup
{
    public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.AddServiceModelServices();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseServiceModel(builder =>
        {
            builder.AddService<CalculatorService>()
                .AddServiceEndpoint<CalculatorService, ICalculatorService>(new BasicHttpBinding(), "/CalculatorService/unbuilt");
        });
    }
}

// Disconnected-callback negative: a second, real Startup-shaped type with its own exact Configure and
// UseServiceModel callback, but no UseStartup<UnusedStartup>() call ever selects it. The scanner only
// admits TStartup types reached through a real Host.CreateDefaultBuilder(...).ConfigureWebHostDefaults(w
// => w.UseStartup<TStartup>()) chain, so this type's Configure method is never scanned at all.
public sealed class UnusedStartup
{
    public void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.AddServiceModelServices();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseServiceModel(builder =>
        {
            builder.AddService<CalculatorService>()
                .AddServiceEndpoint<CalculatorService, ICalculatorService>(new BasicHttpBinding(), "/CalculatorService/disconnected");
        });
    }
}

// Mismatched client/contract negative: derives System.ServiceModel.ClientBase<ICalculatorService> and
// implements ICalculatorService (so the type is noticed at all — capability/client-boundary admission is
// triggered per implementing method) but also separately implements the unrelated, independently admitted
// classic-family IClassicEchoService directly. A client boundary must be emitted only for the exact
// contract ClientBase was constructed with (ICalculatorService); IClassicEchoService's own operation must
// admit ordinary capability instead, never a client boundary, merely because ClientBase appears somewhere
// in the same type's base-type chain.
public sealed class MismatchedContractClient : System.ServiceModel.ClientBase<ICalculatorService>, ICalculatorService, IClassicEchoService
{
    public MismatchedContractClient(System.ServiceModel.Channels.Binding binding, System.ServiceModel.EndpointAddress address)
        : base(binding, address)
    {
    }

    public double Add(double n1, double n2) => Channel.Add(n1, n2);

    public double Subtract(double n1, double n2) => Channel.Subtract(n1, n2);

    public double Multiply(double n1, double n2) => Channel.Multiply(n1, n2);

    public double Divide(double n1, double n2) => Channel.Divide(n1, n2);

    public double SquareRoot(double n1) => Channel.SquareRoot(n1);

    public double Modulo(double n1, double n2) => Channel.Modulo(n1, n2);

    public string Echo(string value) => value;
}
