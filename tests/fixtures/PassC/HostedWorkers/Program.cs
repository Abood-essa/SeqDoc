using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<HostedWorkers.ExactWorker>();
builder.Services.AddHostedService<HostedWorkers.BackgroundWorker>();
