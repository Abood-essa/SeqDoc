using BehaviorDocumentation.DependencyInjection.Services;

var builder = WebApplication.CreateBuilder(args);
ServiceRegistration.Register(builder.Services);
// Admitted top-level registration projected through the companion-only traversal.
builder.Services.AddTransient<IGadgetRepository, GadgetRepository>();
builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
await app.RunAsync();
