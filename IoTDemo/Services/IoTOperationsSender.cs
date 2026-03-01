using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using IoTDemo.Models;

namespace IoTDemo.Services;

/// <summary>
/// Sends telemetry to IoT Operations MQTT broker using MQTTnet with username/password auth.
/// </summary>
public class IoTOperationsSender : ISender
{
    private readonly IoTOperationsSettings _settings;
    private readonly TelemetryGenerator _telemetry;
    private readonly int _intervalMs;
    private IMqttClient? _client;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public string Name => "IoT Ops";
    public SenderStatus Status { get; private set; } = SenderStatus.Stopped;
    public int MessageCount { get; private set; }
    public DateTime? LastSentUtc { get; private set; }
    public string? LastError { get; private set; }

    public IoTOperationsSender(IoTOperationsSettings settings, string deviceId, string location, int intervalMs)
    {
        _settings = settings;
        _intervalMs = intervalMs;
        _telemetry = new TelemetryGenerator(deviceId, "iot-operations", location);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var factory = new MqttFactory();
            _client = factory.CreateMqttClient();

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(_settings.Hostname, _settings.Port)
                .WithClientId(Guid.NewGuid().ToString())
                .WithCredentials(_settings.Username, _settings.Password)
                .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500);

            // Use TLS if port is 8883
            if (_settings.Port == 8883)
                optionsBuilder.WithTlsOptions(o => o.UseTls());

            var options = optionsBuilder.Build();

            await _client.ConnectAsync(options, cancellationToken);
            Status = SenderStatus.Running;
            LastError = null;

            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loopTask = SendLoopAsync(_loopCts.Token);
        }
        catch (Exception ex)
        {
            Status = SenderStatus.Error;
            LastError = ex.Message;
        }
    }

    public async Task StopAsync()
    {
        _loopCts?.Cancel();
        if (_loopTask != null)
            await _loopTask;

        if (_client?.IsConnected == true)
            await _client.DisconnectAsync();

        Status = SenderStatus.Stopped;
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var json = _telemetry.GenerateJson();
                var msg = new MqttApplicationMessageBuilder()
                    .WithTopic(_settings.Topic)
                    .WithPayload(json)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await _client!.PublishAsync(msg, ct);
                MessageCount++;
                LastSentUtc = DateTime.UtcNow;
                LastError = null;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Status = SenderStatus.Error;
            }

            try { await Task.Delay(_intervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _client?.Dispose();
    }
}
