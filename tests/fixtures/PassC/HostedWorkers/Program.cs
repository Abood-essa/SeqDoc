using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<HostedWorkers.ExactWorker>();
builder.Services.AddHostedService<HostedWorkers.BackgroundWorker>();
builder.Services.AddHostedService<HostedWorkers.BatchWorker>();
builder.Services.AddHostedService<HostedWorkers.RetryWorker>();
builder.Services.AddHostedService<HostedWorkers.LookalikeCancellationWorker>();
