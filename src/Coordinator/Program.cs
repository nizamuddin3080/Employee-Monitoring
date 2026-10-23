using LocalActivityTracker.Coordinator;

Console.Title = "Local Activity Tracker - Coordinator";

var enableTray = !args.Contains("--headless") && !args.Contains("--no-tray");
var runDurationSeconds = 0;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--duration" && i + 1 < args.Length && int.TryParse(args[i + 1], out var d))
    {
        runDurationSeconds = d;
    }
}

using var cts = new CancellationTokenSource();

void OnCancelKey(object? s, ConsoleCancelEventArgs e)
{
    e.Cancel = true;
    Console.WriteLine("\nShutdown signal received (Ctrl+C). Terminating gracefully...");
    try { cts.Cancel(); } catch { }
}

void OnProcessExit(object? s, EventArgs e)
{
    try { cts.Cancel(); } catch { }
}

Console.CancelKeyPress += OnCancelKey;
AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

using var app = new CoordinatorApp(enableTray: enableTray);

try
{
    await app.StartAsync(cts.Token);

    if (runDurationSeconds > 0)
    {
        Console.WriteLine($"Coordinator running in timed mode for {runDurationSeconds} second(s)...");
        await Task.Delay(TimeSpan.FromSeconds(runDurationSeconds), cts.Token);
    }
    else
    {
        Console.WriteLine("Coordinator running. Press Ctrl+C to terminate.");
        await Task.Delay(Timeout.Infinite, cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Normal cancellation
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"FATAL: Coordinator failed with error: {ex.Message}");
    Console.ResetColor();
    return 1;
}
finally
{
    Console.CancelKeyPress -= OnCancelKey;
    AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    await app.StopAsync();
}

return 0;
