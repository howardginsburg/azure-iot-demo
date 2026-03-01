# Azure IoT Demo

A .NET CLI tool that sends simulated industrial sensor telemetry through three Azure IoT messaging paths simultaneously, demonstrating the architecture options available in Microsoft Azure IoT.

## Architecture

All three paths deliver the same JSON payload to a single Fabric Real-Time Intelligence (RTI) table:

```
Path 1: CLI ──MQTT──▶ IoT Hub ──────────────────▶ Event Hubs ──▶ Fabric RTI
Path 2: CLI ──MQTT──▶ Event Grid MQTT Broker ───▶ Event Hubs ──▶ Fabric RTI
Path 3: CLI ──MQTT──▶ IoT Operations MQTT Broker▶ Dataflow ───▶ Fabric RTI
```

All three senders use **MQTTnet** — the only difference is the broker endpoint and authentication method.

## Payload

```json
{
  "deviceId": "factory-sensor-01",
  "timestamp": "2026-03-01T13:05:02.123Z",
  "source": "iot-hub",
  "temperature": 72.4,
  "pressure": 14.7,
  "vibration": 0.032
}
```

Values use a random walk algorithm with realistic drift. Vibration has a ~5% chance of anomaly spikes (0.15–0.25g) to simulate bearing wear.

Each sender uses a unique device identity:
| Sender | DeviceId Source |
|--------|----------------|
| IoT Hub | `IoTHub.DeviceId` from appsettings.json |
| Event Grid | CN (Common Name) extracted from X.509 client certificate |
| IoT Operations | `IoTOperations.Username` from appsettings.json |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- An Azure subscription with the following resources provisioned:
  - Azure IoT Hub
  - Azure Event Grid namespace with MQTT broker enabled
  - Azure IoT Operations on an Arc-enabled Kubernetes cluster
  - Microsoft Fabric workspace with Real-Time Intelligence eventhouse

## Quick Start

```bash
cd IoTDemo
dotnet build
# Edit appsettings.json with your connection details (see below)
dotnet run
```

The Spectre.Console dashboard launches. Press **1**, **2**, or **3** to toggle each sender on/off. Press **Q** to quit.

```
──────────────── IoT Demo — Industrial Sensor Telemetry ────────────────
 Press 1/2/3 to toggle senders  |  Q to quit

╭──────┬───────────┬──────────┬────────────┬───────────┬────────────────╮
│  #   │ Sender    │ Status   │  Messages  │ Last Sent │ Last Error     │
├──────┼───────────┼──────────┼────────────┼───────────┼────────────────┤
│ [1]  │ IoT Hub   │ ● Running│     42     │ 13:05:02  │ --             │
│ [2]  │ Event Grid│ ○ Stopped│      0     │    --     │ --             │
│ [3]  │ IoT Ops   │ ● Running│     38     │ 13:05:01  │ --             │
╰──────┴───────────┴──────────┴────────────┴───────────┴────────────────╯
  Press 1/2/3 to toggle  │  Q to quit  │  Interval: 2s
```

---

## Configuration

All connection details are stored in `appsettings.json`:

```json
{
  "IntervalSeconds": 2,
  "IoTHub": {
    "Hostname": "<iothub-name>.azure-devices.net",
    "DeviceId": "factory-sensor-01",
    "SharedAccessKey": "<device-sas-key>"
  },
  "EventGrid": {
    "Hostname": "<namespace>.westus2-1.ts.eventgrid.azure.net",
    "Port": 8883,
    "ClientCertPath": "certs/client.pem",
    "ClientKeyPath": "certs/client-key.pem",
    "CaCertPath": "certs/ca.pem",
    "Topic": "factory/sensors/telemetry"
  },
  "IoTOperations": {
    "Hostname": "aio-mq-dmqtt-frontend",
    "Port": 1883,
    "Username": "factory-sensor-03",
    "Password": "<password>",
    "Topic": "azure-iot-operations/data/factory-sensor-03"
  }
}
```

> **⚠️ Do not commit real credentials.** The `certs/` directory is in `.gitignore`.

---

## Setup Guide

### 1. IoT Hub

IoT Hub uses MQTT v3.1.1 with SAS token authentication. The CLI generates the SAS token automatically from the shared access key.

1. **Create an IoT Hub** (S1 tier or free tier for demo):
   ```bash
   az iot hub create --name <iothub-name> --resource-group <rg> --sku S1
   ```

2. **Register a device**:
   ```bash
   az iot hub device-identity create --hub-name <iothub-name> --device-id factory-sensor-01
   ```

3. **Get the device SAS key**:
   ```bash
   az iot hub device-identity show --hub-name <iothub-name> --device-id factory-sensor-01 --query "authentication.symmetricKey.primaryKey" -o tsv
   ```

4. **Update `appsettings.json`**:
   - `Hostname`: `<iothub-name>.azure-devices.net`
   - `DeviceId`: `factory-sensor-01`
   - `SharedAccessKey`: the key from step 3

5. **Route to Fabric RTI**: Use IoT Hub's built-in Event Hubs-compatible endpoint as a source in your Fabric eventstream.

---

### 2. Event Grid MQTT Broker

Event Grid uses MQTT v5 with X.509 certificate authentication.

1. **Create an Event Grid namespace with MQTT broker**:
   ```bash
   az eventgrid namespace create \
     --name <namespace> \
     --resource-group <rg> \
     --topic-spaces-configuration "{state:'Enabled'}"
   ```

2. **Generate certificates** using the [CertificateGenerator](https://github.com/howardginsburg/CertificateGenerator) tool:
   ```bash
   # Clone and run the certificate generator
   git clone https://github.com/howardginsburg/CertificateGenerator.git
   cd CertificateGenerator
   # Follow the README to generate a CA and client certificate
   # Use "factory-sensor-02" as the CN for the client cert (this becomes the deviceId)
   ```

3. **Register the CA certificate** in Event Grid:
   ```bash
   az eventgrid namespace ca-certificate create \
     --resource-group <rg> \
     --namespace-name <namespace> \
     --ca-certificate-name demo-ca \
     --certificate "$(cat ca.pem | base64)"
   ```

4. **Create a client** (using the cert subject CN as the authentication name):
   ```bash
   az eventgrid namespace client create \
     --resource-group <rg> \
     --namespace-name <namespace> \
     --client-name factory-sensor-02 \
     --authentication-name factory-sensor-02 \
     --client-certificate-authentication "{validationScheme:'SubjectMatchesAuthenticationName'}"
   ```

5. **Create a topic space** that allows publishing:
   ```bash
   az eventgrid namespace topic-space create \
     --resource-group <rg> \
     --namespace-name <namespace> \
     --topic-space-name factory-telemetry \
     --topic-templates "factory/sensors/#"
   ```

6. **Create a permission binding** for the client to publish:
   ```bash
   az eventgrid namespace permission-binding create \
     --resource-group <rg> \
     --namespace-name <namespace> \
     --permission-binding-name factory-publish \
     --client-group-name '$all' \
     --topic-space-name factory-telemetry \
     --permission publisher
   ```

7. **Route to Event Hubs**: Configure Event Grid routing to forward MQTT messages to an Event Hub, then connect that Event Hub as a source in your Fabric eventstream.

8. **Update `appsettings.json`**:
   - `Hostname`: from the Event Grid namespace MQTT hostname
   - Copy `client.pem`, `client-key.pem`, and `ca.pem` into the `certs/` directory

---

### 3. IoT Operations (Asset in the Portal)

IoT Operations uses MQTT v5 with username/password authentication on the local broker. This setup creates an **asset visible in the Operations Experience portal**.

#### 3a. Set up MQTT broker authentication

The default AIO broker only allows Kubernetes SAT authentication. To enable username/password for the demo, deploy a custom authentication server.

1. **Deploy the username/password auth server** from the [Azure IoT Operations samples](https://github.com/Azure-Samples/explore-iot-operations/tree/main/samples/auth-server-user-pass-mqtt):
   ```bash
   # Clone the samples repo
   git clone https://github.com/Azure-Samples/explore-iot-operations.git
   cd explore-iot-operations/samples/auth-server-user-pass-mqtt

   # Build and deploy to your cluster
   kubectl apply -f deploy/
   ```

2. **Create a BrokerListener** with a non-TLS port for the demo (or use TLS on port 8883):
   ```yaml
   apiVersion: mqttbroker.iotoperations.azure.com/v1
   kind: BrokerListener
   metadata:
     name: demo-listener
     namespace: azure-iot-operations
   spec:
     brokerRef: default
     serviceName: aio-broker-demo
     serviceType: ClusterIP
     ports:
       - port: 1883
         authenticationRef: demo-authn
   ```

3. **Create a BrokerAuthentication** resource pointing to the custom auth server:
   ```yaml
   apiVersion: mqttbroker.iotoperations.azure.com/v1
   kind: BrokerAuthentication
   metadata:
     name: demo-authn
     namespace: azure-iot-operations
   spec:
     authenticationMethods:
       - method: Custom
         customSettings:
           endpoint: https://authn-server.azure-iot-operations.svc.cluster.local:443
           caCertConfigMap: custom-auth-ca
           headers:
             Content-Type: application/json
   ```

4. **Create a user** in the auth server for the demo:
   ```bash
   # This depends on your auth server implementation
   # The username becomes the deviceId in the telemetry payload
   ```

#### 3b. Create the asset in Operations Experience

This is the key step that makes the asset appear in the AIO portal UI.

1. **Open the Operations Experience portal** at https://iotoperations.azure.com

2. **Select your IoT Operations instance**

3. **Navigate to Assets** → **Create asset**:

   | Field | Value |
   |-------|-------|
   | Asset name | `factory-sensor-03` |
   | Description | `Industrial sensor — temperature, pressure, vibration` |
   | Inbound endpoint | Select your custom MQTT endpoint |

4. **Create a dataset**:

   | Field | Value |
   |-------|-------|
   | Dataset name | `telemetry` |
   | Destination | `MQTT` |
   | Topic | `azure-iot-operations/data/factory-sensor-03` |

5. **Add data points** to the dataset:

   | Data point name | Data source |
   |-----------------|-------------|
   | temperature | `temperature` |
   | pressure | `pressure` |
   | vibration | `vibration` |

6. **Save** the asset. It now appears in the Operations Experience portal with status indicators.

#### 3c. Create a dataflow to route data to Fabric RTI

1. In Operations Experience, go to **Dataflows** → **Create dataflow**

2. **Source**: Select **Asset** → choose `factory-sensor-03`

3. **Destination**: Select **Microsoft Fabric OneLake** or **Event Hubs** endpoint
   - If using Event Hubs: create a dataflow endpoint pointing to your Event Hub namespace
   - The Event Hub feeds into your Fabric eventstream

4. **Save** the dataflow. Data published to `azure-iot-operations/data/factory-sensor-03` will flow through to Fabric RTI.

#### 3d. Connect the CLI

If running the CLI **on the cluster** (e.g., via `kubectl port-forward`):
```bash
kubectl port-forward svc/aio-broker-demo 1883:1883 -n azure-iot-operations
```

Update `appsettings.json`:
- `Hostname`: `localhost` (or the service DNS name if running in-cluster)
- `Port`: `1883`
- `Username`: the username you created in the auth server
- `Password`: the corresponding password
- `Topic`: `azure-iot-operations/data/factory-sensor-03` (must match the asset dataset topic)

When the sender publishes, the asset in the Operations Experience portal shows data flowing, and the dataflow routes it to Fabric RTI.

---

## Fabric Real-Time Intelligence Setup

### Create the KQL table

In your Fabric RTI eventhouse, run:

```kql
.create table IndustrialTelemetry (
    deviceId: string,
    timestamp: datetime,
    source: string,
    temperature: real,
    pressure: real,
    vibration: real
)
```

### Create eventstreams

Create an eventstream for each ingestion path (or a shared one if using a single Event Hub namespace):

1. **IoT Hub** → Add IoT Hub as a source → route to `IndustrialTelemetry` table
2. **Event Grid** → Add the Event Hub (where Event Grid routes MQTT messages) as a source
3. **IoT Operations** → Add the Event Hub (where the AIO dataflow sends data) as a source

### Sample KQL queries

```kql
// All telemetry from all sources
IndustrialTelemetry
| where timestamp > ago(5m)
| order by timestamp desc

// Compare sources side by side
IndustrialTelemetry
| where timestamp > ago(10m)
| summarize avg(temperature), avg(pressure), avg(vibration) by source, bin(timestamp, 30s)
| render timechart

// Detect vibration anomalies
IndustrialTelemetry
| where timestamp > ago(1h)
| where vibration > 0.10
| project timestamp, deviceId, source, vibration
| order by vibration desc
```

---

## Project Structure

```
IoTDemo/
├── Program.cs                      # Spectre.Console dashboard + keypress handler
├── appsettings.json                # Connection configuration
├── Models/
│   ├── AppSettings.cs              # Strongly-typed config classes
│   └── TelemetryPayload.cs         # JSON payload model
├── Services/
│   ├── ISender.cs                  # Sender interface
│   ├── TelemetryGenerator.cs       # Random walk payload generator
│   ├── IoTHubSender.cs             # MQTT v3.1.1 + SAS token
│   ├── EventGridSender.cs          # MQTT v5 + X.509 cert
│   └── IoTOperationsSender.cs      # MQTT v5 + username/password
└── certs/                          # Client certs (gitignored)
```

## License

MIT
