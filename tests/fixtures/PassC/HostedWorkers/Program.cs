using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<HostedWorkers.ExactWorker>();
builder.Services.AddHostedService<HostedWorkers.BackgroundWorker>();
builder.Services.AddHostedService<HostedWorkers.BatchWorker>();
builder.Services.AddHostedService<HostedWorkers.RetryWorker>();
builder.Services.AddHostedService<HostedWorkers.LookalikeCancellationWorker>();
builder.Services.AddHostedService<HostedWorkers.ThrottledWorker>();
builder.Services.AddHostedService<HostedWorkers.LocalTokenWorker>();
builder.Services.AddHostedService<HostedWorkers.FieldTokenWorker>();
builder.Services.AddHostedService<HostedWorkers.SubstitutedTokenWorker>();
builder.Services.AddHostedService<HostedWorkers.UnrelatedCatchWorker>();
builder.Services.AddHostedService<HostedWorkers.LambdaAwaitWorker>();
builder.Services.AddHostedService<HostedWorkers.UnsupportedLoopWorker>();
builder.Services.AddHostedService<HostedWorkers.TwoSemaphoreWorker>();
builder.Services.AddHostedService<HostedWorkers.GuardedWorker>();
