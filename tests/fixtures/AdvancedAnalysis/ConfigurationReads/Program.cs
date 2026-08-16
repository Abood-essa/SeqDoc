using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using AdvancedAnalysis.ConfigurationReads;

// The explicit Microsoft.AspNetCore.Builder and Microsoft.Extensions.DependencyInjection usings keep
// this file self-contained: the relocation test copies the fixture without the repository
// Directory.Build.props, so WebApplication, AddControllers, and MapControllers must resolve from the
// file itself rather than from inherited implicit usings.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
await app.RunAsync();
