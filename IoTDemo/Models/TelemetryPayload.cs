namespace IoTDemo.Models;

public class TelemetryPayload
{
    public string DeviceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public double Temperature { get; set; }
    public double Pressure { get; set; }
    public double Vibration { get; set; }
}
