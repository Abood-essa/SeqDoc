using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace CoreWcfServices;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // This complete-looking chain is deliberately never invoked. Its compiler operations are nested
        // in the real entry point but must not admit UnbuiltStartup or any registration from that startup.
        static async Task UninvokedNestedHostAsync(string[] nestedArgs)
        {
            await Host.CreateDefaultBuilder(nestedArgs)
                .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<UnbuiltStartup>())
                .Build()
                .RunAsync();
        }

        _ = (Func<string[], Task>)UninvokedNestedHostAsync;

        await Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>())
            .Build()
            .RunAsync();
    }
}
