namespace IoTDemo.Services;

public enum SenderStatus { Stopped, Running, Error }

public interface ISender : IAsyncDisposable
{
    string Name { get; }
    SenderStatus Status { get; }
    int MessageCount { get; }
    DateTime? LastSentUtc { get; }
    string? LastError { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
}
