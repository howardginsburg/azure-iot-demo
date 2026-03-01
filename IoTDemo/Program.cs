using Microsoft.Extensions.Configuration;
using Spectre.Console;
using IoTDemo.Models;
using IoTDemo.Services;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var settings = new AppSettings();
config.Bind(settings);

var intervalMs = settings.IntervalSeconds * 1000;

ISender[] senders =
[
    new IoTHubSender(settings.IoTHub, settings.Location, intervalMs),
    new EventGridSender(settings.EventGrid, settings.DeviceId, settings.Location, intervalMs),
    new IoTOperationsSender(settings.IoTOperations, settings.DeviceId, settings.Location, intervalMs)
];

using var appCts = new CancellationTokenSource();

AnsiConsole.Clear();
AnsiConsole.Write(new Rule("[bold blue]IoT Demo — Industrial Sensor Telemetry[/]").RuleStyle("blue"));
AnsiConsole.MarkupLine("[dim]Press [bold]1[/]/[bold]2[/]/[bold]3[/] to toggle senders  |  [bold]Q[/] to quit[/]\n");

// Dashboard refresh loop
var dashboardTask = Task.Run(async () =>
{
    while (!appCts.Token.IsCancellationRequested)
    {
        RenderDashboard(senders, settings.IntervalSeconds);
        try { await Task.Delay(500, appCts.Token); }
        catch (OperationCanceledException) { break; }
    }
});

// Keypress listener on main thread
while (!appCts.Token.IsCancellationRequested)
{
    if (Console.KeyAvailable)
    {
        var key = Console.ReadKey(intercept: true);
        switch (key.KeyChar)
        {
            case '1': await ToggleSender(senders[0], appCts.Token); break;
            case '2': await ToggleSender(senders[1], appCts.Token); break;
            case '3': await ToggleSender(senders[2], appCts.Token); break;
            case 'q' or 'Q':
                appCts.Cancel();
                break;
        }
    }
    else
    {
        await Task.Delay(50);
    }
}

// Cleanup
await dashboardTask;
foreach (var sender in senders)
    await sender.DisposeAsync();

AnsiConsole.MarkupLine("\n[green]All senders stopped. Goodbye![/]");

static async Task ToggleSender(ISender sender, CancellationToken ct)
{
    if (sender.Status == SenderStatus.Running)
        await sender.StopAsync();
    else
        await sender.StartAsync(ct);
}

static void RenderDashboard(ISender[] senders, int intervalSec)
{
    Console.SetCursorPosition(0, 3);

    var table = new Table()
        .Border(TableBorder.Rounded)
        .BorderColor(Color.Blue)
        .AddColumn(new TableColumn("[bold]#[/]").Centered())
        .AddColumn(new TableColumn("[bold]Sender[/]").Width(12))
        .AddColumn(new TableColumn("[bold]Status[/]").Width(12).Centered())
        .AddColumn(new TableColumn("[bold]Messages[/]").Width(10).RightAligned())
        .AddColumn(new TableColumn("[bold]Last Sent[/]").Width(12).Centered())
        .AddColumn(new TableColumn("[bold]Last Error[/]").Width(30));

    for (int i = 0; i < senders.Length; i++)
    {
        var s = senders[i];
        var statusMarkup = s.Status switch
        {
            SenderStatus.Running => "[green]● Running[/]",
            SenderStatus.Error => "[red]✖ Error[/]",
            _ => "[dim]○ Stopped[/]"
        };
        var lastSent = s.LastSentUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "--";
        var errorText = s.LastError ?? "";
        var error = string.IsNullOrEmpty(errorText)
            ? "[dim]--[/]"
            : $"[red]{Markup.Escape(errorText.Length > 28 ? errorText[..28] + "…" : errorText)}[/]";

        table.AddRow(
            $"[bold blue]\\[{i + 1}][/]",
            $"[bold]{s.Name}[/]",
            statusMarkup,
            s.MessageCount.ToString(),
            lastSent,
            error
        );
    }

    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine($"[dim]  Press [bold]1[/]/[bold]2[/]/[bold]3[/] to toggle  │  [bold]Q[/] to quit  │  Interval: {intervalSec}s[/]");
}
