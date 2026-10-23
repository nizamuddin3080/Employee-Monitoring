using LocalActivityTracker.NativeHost;

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

AppDomain.CurrentDomain.ProcessExit += (s, e) =>
{
    try
    {
        cts.Cancel();
    }
    catch
    {
        // Suppress cancellation exceptions
    }
};

using var stdioIn = Console.OpenStandardInput();
using var stdioOut = Console.OpenStandardOutput();

using var bridge = new NativeMessagingBridge(stdioIn, stdioOut);
bridge.Start(cts.Token);

// Wait until bridge stops (e.g. Chrome closes stdin or process receives termination signal)
try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Clean shutdown
}
finally
{
    await bridge.StopAsync();
}

return 0;
