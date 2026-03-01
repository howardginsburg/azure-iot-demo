using System.Security.Cryptography.X509Certificates;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using IoTDemo.Models;

namespace IoTDemo.Services;

/// <summary>
/// Sends telemetry to Event Grid MQTT broker using MQTTnet with X.509 cert auth.
/// </summary>
public class EventGridSender : ISender
{
    private readonly EventGridSettings _settings;
    private readonly TelemetryGenerator _telemetry;
    private readonly int _intervalMs;
    private IMqttClient? _client;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public string Name => "Event Grid";
    public SenderStatus Status { get; private set; } = SenderStatus.Stopped;
    public int MessageCount { get; private set; }
    public DateTime? LastSentUtc { get; private set; }
    public string? LastError { get; private set; }

    public EventGridSender(EventGridSettings settings, string deviceId, string location, int intervalMs)
    {
        _settings = settings;
        _intervalMs = intervalMs;
        _telemetry = new TelemetryGenerator(deviceId, "event-grid", location);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var factory = new MqttFactory();
            _client = factory.CreateMqttClient();

            var cert = X509Certificate2.CreateFromPemFile(_settings.ClientCertPath, _settings.ClientKeyPath);

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_settings.Hostname, _settings.Port)
                .WithTlsOptions(o =>
                {
                    o.UseTls();
                    o.WithClientCertificates(new[] { cert });
                })
                .WithClientId(Guid.NewGuid().ToString())
                .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
                .Build();

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
