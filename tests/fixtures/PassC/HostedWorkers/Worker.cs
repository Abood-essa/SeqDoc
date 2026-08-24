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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            stoppingToken.ThrowIfCancellationRequested();
            foreach (var item in Array.Empty<int>())
            {
                _ = item;
            }

            await Task.Delay(1, stoppingToken);
        }
    }
}

public sealed class BatchWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var item in Array.Empty<int>())
        {
            _ = item;
            await Task.Delay(1, stoppingToken);
        }
    }
}

public sealed class RetryWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                stoppingToken.ThrowIfCancellationRequested();
                await Task.Delay(1, stoppingToken);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }
    }
}

public sealed class LookalikeCancellationWorker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        FakeCancellation.ThrowIfCancellationRequested(stoppingToken);
        return Task.CompletedTask;
    }
}

public static class FakeCancellation
{
    public static void ThrowIfCancellationRequested(CancellationToken token)
    {
    }
}

// This type proves that framework capability extraction is not registration admission.
public sealed class UnregisteredWorker : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
