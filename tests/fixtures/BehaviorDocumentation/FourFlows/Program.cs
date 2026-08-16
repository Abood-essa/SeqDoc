using BehaviorDocumentation.FourFlows.Services;

var builder = WebApplication.CreateBuilder(args);
// Admitted top-level DI registration projected through the companion-only traversal.
builder.Services.AddScoped<IWidgetService, WidgetService>();
builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
await app.RunAsync();
