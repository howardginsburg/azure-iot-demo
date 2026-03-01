namespace IoTDemo.Models;

public class AppSettings
{
    public int IntervalSeconds { get; set; } = 2;
    public IoTHubSettings IoTHub { get; set; } = new();
    public EventGridSettings EventGrid { get; set; } = new();
    public IoTOperationsSettings IoTOperations { get; set; } = new();
}

public class IoTHubSettings
{
    public string Hostname { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string SharedAccessKey { get; set; } = string.Empty;
}

public class EventGridSettings
{
    public string Hostname { get; set; } = string.Empty;
    public int Port { get; set; } = 8883;
    public string ClientCertPath { get; set; } = string.Empty;
    public string ClientKeyPath { get; set; } = string.Empty;
    public string CaCertPath { get; set; } = string.Empty;
    public string Topic { get; set; } = "factory/sensors/telemetry";
}

public class IoTOperationsSettings
{
    public string Hostname { get; set; } = string.Empty;
    public int Port { get; set; } = 1883;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Topic { get; set; } = "azure-iot-operations/data/factory-sensor-01";
}
