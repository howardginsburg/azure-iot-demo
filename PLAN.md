# IoT Learning Summit — Demo Plan

## Overview
Build a unified demo that shows the three architecture paths from slide 10, all landing in the same Fabric RTI eventhouse. Simulates an industrial sensor (Connected Factory scenario) with temperature, pressure, and vibration telemetry.

## Payload Structure
```json
{
  "deviceId": "factory-sensor-01",
  "timestamp": "2026-03-01T13:05:02.123Z",
  "source": "iot-hub",
  "location": "Line-3-Motor-A",
  "temperature": 72.4,
  "pressure": 14.7,
  "vibration": 0.032
}
```

## Telemetry Generation
Each sender gets its own `TelemetryGenerator` instance with independent random walk state.
Values drift realistically rather than jumping randomly:

| Metric      | Baseline | Drift/tick | Bounds      | Unit |
|-------------|----------|------------|-------------|------|
| Temperature | 72.0     | ±0.3       | 65.0–85.0   | °F   |
| Pressure    | 14.7     | ±0.1       | 13.5–16.0   | PSI  |
| Vibration   | 0.03     | ±0.005     | 0.01–0.10   | g    |

- **Anomaly injection**: ~5% chance per tick of a vibration spike (0.15–0.25g) to simulate bearing wear
- Each sender stamps its own `source` value and `deviceId` (e.g., `factory-sensor-01`, `-02`, `-03`)
- `timestamp` is UTC ISO 8601 at time of generation

## KQL Table (Fabric RTI)
```kql
.create table IndustrialTelemetry (
    deviceId: string,
    timestamp: datetime,
    source: string,
    temperature: real,
    pressure: real,
    vibration: real,
    location: string
)
```

## Demo 1: IoT Hub (Cloud Gateway Path)
**Architecture: Devices → IoT Hub → Event Hubs endpoint → Fabric RTI**

- Uses `MQTTnet` to connect to IoT Hub's MQTT endpoint (MQTT v3.1.1)
- Auth: SAS token in MQTT password field, username = `{iothub-hostname}/{device-id}/?api-version=2021-04-12`
- Publishes to `devices/{deviceId}/messages/events/` with `source: "iot-hub"`
- IoT Hub's built-in Event Hubs endpoint routes to Fabric RTI eventstream
- **What to show**: All three senders use MQTTnet — IoT Hub is just another MQTT broker

## Demo 2: Event Grid MQTT (MQTT Direct Path)
**Architecture: Devices → Event Grid MQTT Broker → routing → Fabric RTI**

- Same .NET CLI app uses `MQTTnet` library
- Connects to Event Grid MQTT broker namespace with X.509 certificate auth
- Publishes to topic like `factory/sensors/telemetry` with `source: "event-grid"`
- Event Grid routes to Event Hubs → Fabric RTI eventstream
- **What to show**: MQTT namespace, topic spaces, client auth, routing to Event Hubs

## Demo 3: IoT Operations (Adaptive Cloud Path)
**Architecture: Devices → IoT Operations MQTT Broker → Dataflow → Event Hubs → Fabric RTI**

- Custom asset definition in AIO portal with data points: temperature, pressure, vibration
- Same .NET CLI app uses `MQTTnet` to connect to AIO MQTT broker (`iotops` subcommand)
- Connects with username/password auth
- Publishes to the asset's MQTT topic with `source: "iot-operations"`
- AIO dataflow routes from broker topic → Event Hubs → Fabric RTI
- **What to show**: AIO portal, asset definition, dataflow configuration, CLI publishing to local broker, data flowing from edge

## .NET CLI App Design
Interactive console app with Spectre.Console live dashboard:

```
iot-demo --interval 2
```

All three senders shown in a live table. Press 1/2/3 to toggle each sender on/off during the demo. Senders run in parallel on background tasks.

```
╭──────────────────────────────────────────────────────╮
│          IoT Demo — Industrial Sensor                │
├──────┬───────────┬──────────┬────────────┬───────────┤
│  #   │ Sender    │ Status   │  Messages  │ Last Sent │
├──────┼───────────┼──────────┼────────────┼───────────┤
│ [1]  │ IoT Hub   │ ● Running│     42     │ 13:05:02  │
│ [2]  │ Event Grid│ ○ Stopped│      0     │    --     │
│ [3]  │ IoT Ops   │ ● Running│     38     │ 13:05:01  │
╰──────┴───────────┴──────────┴────────────┴───────────┘
  Press 1/2/3 to toggle  │  Q to quit  │  Interval: 2s
```

### Architecture
- Each sender is an `ISender` with `StartAsync()` / `StopAsync()` and state (Running/Stopped/Error)
- Background `Task` per sender with `CancellationTokenSource` for toggle control
- Shared `TelemetryGenerator` produces the payload, each sender stamps its own `source` value
- `Spectre.Console` `LiveDisplay` or `Layout` redraws the table on each tick
- Keypress listener on main thread toggles senders

### Dependencies
- `MQTTnet` — MQTT client for all three senders (IoT Hub, Event Grid, IoT Operations)
- `Spectre.Console` — Live dashboard UI
- `Microsoft.Extensions.Configuration` — appsettings.json loading

### Configuration (appsettings.json)
```json
{
  "DeviceId": "factory-sensor-01",
  "Location": "Line-3-Motor-A",
  "IntervalSeconds": 2,
  "IoTHub": {
    "Hostname": "<iothub-name>.azure-devices.net",
    "DeviceId": "factory-sensor-01",
    "SharedAccessKey": "<device SAS key>"
  },
  "EventGrid": {
    "Hostname": "<namespace>.westus2-1.ts.eventgrid.azure.net",
    "Port": 8883,
    "ClientCertPath": "certs/client.pem",
    "ClientKeyPath": "certs/client-key.pem",
    "Topic": "factory/sensors/telemetry"
  },
  "IoTOperations": {
    "Hostname": "aio-mq-dmqtt-frontend",
    "Port": 1883,
    "Username": "<username>",
    "Password": "<password>",
    "Topic": "azure-iot-operations/data/factory-sensor-01"
  }
}
```
Loaded via `Microsoft.Extensions.Configuration` with strongly-typed options classes.

## Fabric RTI Setup
- Single eventstream with 3 sources (or shared Event Hubs namespace)
- All sources land in `IndustrialTelemetry` table
- KQL dashboard with:
  - Real-time chart showing all three sources
  - Filter by source to isolate each path
  - Anomaly detection on vibration (stretch goal)

## Demo Flow (during presentation)
1. Show slide 10 (The Architecture That Matters) — "let me show you all three paths live"
2. **IoT Operations**: Show AIO portal → asset → publish data → show it arrive in RTI
3. **IoT Hub**: Run CLI → show device in portal → data in RTI alongside IoT Ops data
4. **Event Grid**: Run CLI → show MQTT namespace → data arrives → all three sources visible
5. Switch to Fabric RTI dashboard → KQL query showing all three streams together
6. "Same payload, same destination, three different paths — pick the one that fits your architecture"

## Todos
1. **create-dotnet-cli** — Scaffold .NET console app with System.CommandLine
2. **implement-iothub-sender** — IoT Hub device telemetry sender using device SDK
3. **implement-eventgrid-sender** — Event Grid MQTT publisher using MQTTnet
4. **payload-generator** — Shared telemetry payload generator with realistic industrial values
5. **configure-aio-asset** — Document/script custom asset definition for IoT Operations
6. **configure-fabric-rti** — Document eventstream + KQL table setup
7. **build-kql-dashboard** — KQL queries for the combined demo dashboard
8. **end-to-end-test** — Test all three paths landing in same table
