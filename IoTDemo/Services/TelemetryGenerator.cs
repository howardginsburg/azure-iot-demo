using System.Text.Json;
using IoTDemo.Models;

namespace IoTDemo.Services;

public class TelemetryGenerator
{
    private readonly Random _random = new();
    private readonly string _deviceId;
    private readonly string _source;

    private double _temperature;
    private double _pressure;
    private double _vibration;

    private const double TempBaseline = 72.0, TempDrift = 0.3, TempMin = 65.0, TempMax = 85.0;
    private const double PressBaseline = 14.7, PressDrift = 0.1, PressMin = 13.5, PressMax = 16.0;
    private const double VibBaseline = 0.03, VibDrift = 0.005, VibMin = 0.01, VibMax = 0.10;
    private const double AnomalyChance = 0.05, AnomalyVibMin = 0.15, AnomalyVibMax = 0.25;

    public TelemetryGenerator(string deviceId, string source)
    {
        _deviceId = deviceId;
        _source = source;
        _temperature = TempBaseline;
        _pressure = PressBaseline;
        _vibration = VibBaseline;
    }

    public TelemetryPayload Generate()
    {
        _temperature = Drift(_temperature, TempDrift, TempMin, TempMax);
        _pressure = Drift(_pressure, PressDrift, PressMin, PressMax);

        if (_random.NextDouble() < AnomalyChance)
            _vibration = AnomalyVibMin + _random.NextDouble() * (AnomalyVibMax - AnomalyVibMin);
        else
            _vibration = Drift(_vibration, VibDrift, VibMin, VibMax);

        return new TelemetryPayload
        {
            DeviceId = _deviceId,
            Timestamp = DateTime.UtcNow,
            Source = _source,
            Temperature = Math.Round(_temperature, 2),
            Pressure = Math.Round(_pressure, 2),
            Vibration = Math.Round(_vibration, 4)
        };
    }

    public string GenerateJson()
    {
        return JsonSerializer.Serialize(Generate(), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private double Drift(double current, double maxDelta, double min, double max)
    {
        var delta = (_random.NextDouble() * 2 - 1) * maxDelta;
        return Math.Clamp(current + delta, min, max);
    }
}
