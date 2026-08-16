using AdvancedAnalysis.DecisionTopology.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<WorkItemService>();
builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
await app.RunAsync();
