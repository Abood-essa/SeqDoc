using Microsoft.Extensions.Hosting;

namespace HostedWorkers
{
public sealed class ExactWorker : IHostedService
{
    private Timer? timer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        timer = new Timer(RunJob, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        return Task.CompletedTask;
    }

    public Task ExecuteAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private void RunJob(object? state)
    {
    }
}

public sealed class LookalikeWorker : FakeHosting.IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class BackgroundWorker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.CompletedTask;
}

public static class UnsupportedTimerShapes
{
    public static void RegisterLambda()
    {
        _ = new Timer(_ => { }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }
}
}

namespace FakeHosting
{
    public interface IHostedService
    {
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }
}
