namespace SeqDoc.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += OnCancelKeyPress;
        try
        {
            return await CliHost.RunAsync(args, Console.Out, Console.Error, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        }
    }
}
