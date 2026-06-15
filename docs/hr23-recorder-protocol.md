# HR2.3 Recorder Server

The desktop application starts a loopback TCP JSON Lines server with the application lifecycle. The default endpoint is `127.0.0.1:7070`; change `Hr23RecorderHost` or `Hr23RecorderPort` in `App.config` when another local endpoint is required.

Each TCP connection sends exactly one JSON object followed by a newline. The server replies with one JSON object followed by a newline and then closes the connection.

## Commands

```json
{"cmd":"status"}
```

```json
{
  "cmd": "prepare",
  "sessionId": "session_20260615_153000",
  "outputDir": "D:/recordings/session_20260615_153000/raw/hr23_radar",
  "timeBase": {
    "master": "debug_monitor",
    "recordingStartEpochS": 1781188200.1
  },
  "metadata": {
    "source": "debug_monitor"
  }
}
```

```json
{"cmd":"start"}
```

```json
{"cmd":"stop"}
```

Successful responses contain `ok: true` and the current `state`. Failed responses contain `ok: false`, `state`, `error`, and `message`. A successful `stop` response always reports `state: "stopped"`.

## Output

`prepare.outputDir` is created when necessary and contains:

- `raw.dat`: UDP payload bytes concatenated in receive order without framing or transformation.
- `packets.csv`: packet timestamp, session alignment time, source endpoint, length, raw offset, cumulative bytes, and frame-header marker.
- `events.csv`: `prepared`, `started`, `first_packet`, `warning`, `error`, `stopped`, and `closed` events.
- `metadata.json`: session metadata, time base, alignment mode, counters, timestamps, source endpoint, warnings, and errors.

When `timeBase.recordingStartEpochS` is present, `session_elapsed_s` uses that debug_monitor epoch and metadata reports `debug_monitor_recording_start_epoch`. Otherwise it uses the local recorder start command epoch and reports `local_recorder_start_epoch`.

## Local Verification

Build and run the headless self-test from the repository root:

```powershell
dotnet build Tests\Hr23Recorder.SelfTest\Hr23Recorder.SelfTest.csproj -c Debug
Tests\Hr23Recorder.SelfTest\bin\Debug\Hr23Recorder.SelfTest.exe
```

The self-test covers state transitions, exact raw bytes, CSV and metadata output, both alignment modes, TCP commands, TCP port conflicts, and the original `UdpService.DataReceived` path.

For an application-level test:

1. Start the desktop application and connect its existing UDP receiver to local port `20202` or `23480`.
2. Use debug_monitor's HR2.3 client with host `127.0.0.1` and port `7070` to send `status`, `prepare`, and `start`.
3. Use `Tools/UdpDataSender` to send UDP data to the configured radar UDP port.
4. Send `stop` from debug_monitor and inspect the four files under the supplied `raw/hr23_radar` directory.

The TCP server never binds the radar UDP ports, so the existing UDP sender and display/processing path remain independent from debug_monitor control traffic.
