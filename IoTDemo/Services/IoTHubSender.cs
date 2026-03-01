using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using IoTDemo.Models;

namespace IoTDemo.Services;

/// <summary>
/// Sends telemetry to IoT Hub using raw MQTT (MQTTnet) with SAS token auth.
/// </summary>
public class IoTHubSender : ISender
{
    private readonly IoTHubSettings _settings;
    private readonly TelemetryGenerator _telemetry;
    private readonly int _intervalMs;
    private IMqttClient? _client;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public string Name => "IoT Hub";
    public SenderStatus Status { get; private set; } = SenderStatus.Stopped;
    public int MessageCount { get; private set; }
    public DateTime? LastSentUtc { get; private set; }
    public string? LastError { get; private set; }

    public IoTHubSender(IoTHubSettings settings, string location, int intervalMs)
    {
        _settings = settings;
        _intervalMs = intervalMs;
        _telemetry = new TelemetryGenerator(settings.DeviceId, "iot-hub", location);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var factory = new MqttFactory();
            _client = factory.CreateMqttClient();

            var username = $"{_settings.Hostname}/{_settings.DeviceId}/?api-version=2021-04-12";
            var sasToken = GenerateSasToken(
                $"{_settings.Hostname}/devices/{_settings.DeviceId}",
                _settings.SharedAccessKey,
                TimeSpan.FromHours(1));

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_settings.Hostname, 8883)
                .WithTlsOptions(o => o.UseTls())
                .WithClientId(_settings.DeviceId)
                .WithCredentials(username, sasToken)
                .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
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
        var topic = $"devices/{_settings.DeviceId}/messages/events/";
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var json = _telemetry.GenerateJson();
                var msg = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
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

    private static string GenerateSasToken(string resourceUri, string key, TimeSpan ttl)
    {
        var expiry = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var encodedUri = HttpUtility.UrlEncode(resourceUri);
        var toSign = $"{encodedUri}\n{expiry}";

        using var hmac = new HMACSHA256(Convert.FromBase64String(key));
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign)));

        return $"SharedAccessSignature sr={encodedUri}&sig={HttpUtility.UrlEncode(sig)}&se={expiry}";
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _client?.Dispose();
    }
}
