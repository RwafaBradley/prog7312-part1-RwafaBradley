# SmartX — Data Ingestion and Validation Gateway

**PROG7312 — Part 1**
A WPF desktop console on .NET 10, driving an ASP.NET Core Minimal API gateway.

The SmartX mesh is a hybrid IoT ecosystem in which thousands of ESP32 microcontrollers publish telemetry of several types — floats for soil moisture, integers for power wattage, booleans for valve states — into a central gateway, and what follows documents Part 1 of that system, namely the ingestion and validation engine, the HTTP API which fronts it, and the Windows operator console which drives both.

The architecture is the Traditional Desktop option from the brief, which is to say a WPF user interface communicating with an external processing engine as opposed to hosting that engine inside its own process, and the separation is enforced rather than merely intended, given that the console holds no copy of the engine and is able to reach it only over HTTP.

***

## Contents

1. What runs, and where
2. Prerequisites
3. Quick start
4. Running it step by step
5. The console, screen by screen
6. The engagement strategy: Signal Radar
7. Where each required C# concept lives
8. WPF architecture notes
9. Project layout
10. API reference
11. Running the gateway in Docker
12. How to demonstrate it
13. Design decisions worth defending
14. Troubleshooting
15. Repository

***

## 1. What runs, and where

| Piece | Project | Target | Default address |
|---|---|---|---|
| Ingestion engine | `src/SmartX.Core` | `net10.0` | in process to the API |
| HTTP gateway | `src/SmartX.Api` | `net10.0` | `http://localhost:5240` |
| Client logic and view models | `src/SmartX.Desktop.Client` | `net10.0` | referenced by the UI |
| WPF console | `src/SmartX.Wpf` | `net10.0-windows` | the desktop |

Two processes are involved, the gateway and the console, and since the console is a pure HTTP client which never hosts the API in process, the gateway is obliged to be answering before the window opens; `run-desktop.ps1` handles both in a single step.

**Figure 1 — Process and deployment architecture.**

```
Operator machine (Windows)                       Container or host process
┌───────────────────────────────┐                ┌──────────────────────────────┐
│  SmartX.Wpf                   │                │  SmartX.Api                  │
│  markup, custom radar control │                │  Minimal API surface         │
│            │                  │                │            │                 │
│  SmartX.Desktop.Client        │   HTTP :5240   │  SmartX.Core                 │
│  view models, commands,       │       ↔        │  ingestion, validation,      │
│  GatewayClient (HttpClient)   │   JSON and SSE │  topology, statistics,       │
│                               │                │  attachment store            │
└───────────────────────────────┘                └──────────────────────────────┘
        one process                                      one process
     no engine in process                          no UI, no Windows dependency
```

> **There is not a single NuGet `PackageReference` anywhere in this solution.** The consequence is that `dotnet restore` succeeds upon a clean machine, behind a corporate proxy, or entirely offline, everything required being present already in the .NET 10 SDK and the Windows Desktop runtime.

***

## 2. Prerequisites

| Requirement | Version | Check with |
|---|---|---|
| **Windows** | 10 (1809 or later) or 11 | |
| .NET SDK | **10.0 or later**, with the Windows Desktop workload | `dotnet --list-sdks` |
| Docker Desktop *(optional)* | 24 or later | `docker --version` |

The Windows Desktop workload ships inside the ordinary .NET SDK installer for Windows, so no additional step is required, and Visual Studio 2022 (17.14 or later) or Visual Studio 2026 opens `SmartX.Desktop.sln` directly, as does VS Code with the C# Dev Kit.

WPF requires Windows. `src/SmartX.Wpf` targets `net10.0-windows` and cannot be built upon Linux or macOS, whereas the other three projects are platform neutral and build anywhere, which is the reason the solution divides precisely where it does.

***

## 3. Quick start

From the repository root, in PowerShell:

```powershell
.\run-desktop.ps1
```

That command builds the solution, starts the gateway, waits for `/api/health` to answer, launches the console, and shuts the gateway down again once the window closes; double clicking `run-desktop.cmd` does the same thing by the same route.

The gateway seeds an 18 node simulated ESP32 mesh as it starts, with the consequence that the radar is alive the moment the window opens as opposed to filling gradually while the operator waits for something to look at.

***

## 4. Running it step by step

The sequence below is the one to follow in order to watch both sides at once, or where the execution policy blocks the script.

```powershell
dotnet restore
dotnet build
```

**Terminal 1, the gateway:**

```powershell
dotnet run --project src\SmartX.Api
```

Confirm it at <http://localhost:5240/api/health>, which ought to return `{"status":"healthy", ...}`.

**Terminal 2, the console:**

```powershell
dotnet run --project src\SmartX.Wpf
```

### Pointing the console at a different gateway

The first command line argument overrides the address:

```powershell
dotnet run --project src\SmartX.Wpf -- http://10.0.4.7:5240/
```

The foot of the sidebar always names the port it is talking to, and the status bar turns red and carries a retry message where the connection drops, the console reconnecting on its own once the gateway returns.

***

## 5. The console, screen by screen

The console opens upon a gateway screen carrying the three architectural pillars the brief requires, of which only *Sensor Data Ingestion and Telemetry* is open, the remaining two being visibly locked and labelled with the part in which they arrive. Opening the first pillar reveals a sidebar of six screens, grouped as set out below.

**MONITOR**

| Screen | What it shows |
|---|---|
| **Overview** | Registered nodes, packets ingested, anomalies flagged and average API latency, with the most recent registrations underneath. Every figure is read from the running gateway. |
| **Signal Radar** | The engagement feature. Section 6. |

**INGEST**

| Screen | What it shows |
|---|---|
| **Register Sensor** | Sensor payload management: MAC address, deployment location, and the category cards which fix the payload type. |
| **Telemetry Stream** | The three live typed readings, the generic wrapper, the overloaded `+` running upon two real meters, and the jagged array being promoted into a `List<T>`. |
| **Deployment Tree** | The nested tree with the recursive validator and its trace. |
| **File Attachments** | Multipart upload by drag and drop or `OpenFileDialog`, with the attached files listed per profile. |

### 5.1 The registration flow

Registration is validated twice, and the duplication is deliberate rather than accidental: the form validates so that the operator is told immediately, and the gateway validates because the form is a convenience which any HTTP client is able to bypass.

**Figure 2 — Registering a sensor.**

```
Operator              │  Console (Desktop.Client)        │  Gateway (Api and Core)
──────────────────────┼──────────────────────────────────┼───────────────────────
types into the form   →  RegistrationFormViewModel       │
                      │   INotifyDataErrorInfo            │
                      │            │                      │
                      │   ‹fields valid?›                 │
field colours red,    ←  no        │ yes                  │
button stays disabled │            │                      │
                      │   AsyncRelayCommand               │
                      │   CanExecute false while in flight│
                      │            │                      │
                      │   POST /api/sensors           →   RegistrationValidator
                      │                                   │        │
                      │   400, errors at field level  ←   ‹valid?›  no
                      │            │                      │  yes
                      │            │                      SensorRegistry.Add
                      │   201 or 200                  ←   │
 new point appears    ←  ApplySnapshot mutates in place   │
 on the radar         │   no collection is cleared        │
```

The refusal path matters as much as the success path. A malformed MAC never leaves the machine, an registration already in flight cannot be submitted a second time because `CanExecute` has by then returned false, and a request which does reach the gateway malformed comes back as `400` carrying errors at field level, as opposed to a bare failure the operator is left to guess at.

### 5.2 The attachment flow

**Figure 3 — Uploading an attachment.**

```
Operator              │  Console                         │  Gateway
──────────────────────┼──────────────────────────────────┼───────────────────────
drag and drop, or     →  WpfFileDialogService            │
click to browse       │   OpenFileDialog                  │
                      │            │                      │
                      │   ‹extension permitted?›          │
refused, listed as    ←  no        │ yes                  │
rejected              │            │                      │
                      │   StreamContent over FileStream   │
                      │   multipart/form-data         →   AttachmentStore
                      │                                   │   80 KB chunks,
                      │                                   │   SHA256 on the
                      │                                   │   same pass
 file listed against  ←  attachment id and hash       ←   │
 the profile          │                                   │
```

Nothing is buffered whole. The console streams from a `FileStream` and the gateway streams to disk in 80 KB chunks, computing a SHA256 digest upon the same pass, with the consequence that the memory cost of an upload is bounded by the size of a chunk as opposed to the size of the file.

***

## 6. The engagement strategy: Signal Radar

Every registered node is a point upon a live radar sweep, and five visual channels carry information at once:

| Channel | What it encodes |
|---|---|
| **Angle** | The node's place within its deployment zone. |
| **Ring** | Which zone it belongs to, so a whole zone going quiet is a visible gap as opposed to a line in a list. |
| **Colour** | The rhythm state, below. |
| **Brightness** | Freshness. A point flares as the sweep passes and fades until the next pass. |
| **Halo** | Anything needing attention pulses, so it is noticeable in peripheral vision. |

The colour channel encodes a rhythm state, of which there are five:

| State | Colour | Meaning |
|---|---|---|
| `Healthy` | teal | On cadence, inside its own expected envelope. |
| `Drifting` | amber | *Sustained* deviation (smoothed sigma at or above 2), or an unstable link. |
| `Flaring` | rose | A genuine spike: sigma at or above 3. Held for 8 seconds after it recovers. |
| `Stalled` | violet | Publishing on cadence, but the last 10 or more readings are identical bit for bit: a wedged sensor. |
| `Flatlined` | grey | Silent for 3.5 times its expected interval. |

### 6.1 Why this, and not a progress bar or a threshold alarm

The two failures which matter in a high throughput mesh are an anomalous spike and a sensor disconnect, and conventional consoles communicate both badly: a spike is one row in a scrolling log, and a disconnect is the *absence* of rows, which no threshold alarm is able to fire upon. Placing the nodes spatially and scoring each against its own rolling baseline, as opposed to against one global threshold, means that a 700 W excursion is an alarm upon a 20 W sensor and unremarkable upon a 2 kW one. Every node in the attention queue states its own diagnosis in plain language as well, with the consequence that the console explains rather than merely alerting.

**Figure 4 — Ingestion and anomaly scoring, one packet.**

```
ESP32 node            │  Gateway pipeline                │  Per node state
──────────────────────┼──────────────────────────────────┼───────────────────────
POST /api/telemetry   →  PacketValidator                 │
or /telemetry/batch   │   type, range, ordering           │
                      │            │                      │
 rejected, out of     ←  no        │ yes                  │
 order or out of range│            │                      │
                      │   TelemetryChannel<T>         →   CircularTelemetryBuffer<T>
                      │   one closed channel per node     │   fixed capacity,
                      │            │                      │   struct enumerator
                      │   ‹actuator?›                     │
                      │     │            │                │
                      │    yes           no               │
                      │     │            │                │
                      │  chatter      weighted        →   running mean and
                      │  count        Welford             sigma, which forget
                      │     │            │                │
                      │     └─────┬──────┘                │
                      │           │                       │
                      │   rhythm state: Healthy, Drifting,│
                      │   Flaring, Stalled, Flatlined     │
                      │           │                       │
 radar point recolours ←  SSE frame on /api/telemetry/stream
```

### 6.2 Proving it works

Select a node, then use the buttons beneath the attention queue:

| Button | What the simulator does | What you should see |
|---|---|---|
| **Spike** | Adds 2.5 to 4 times the node's swing for 6 ticks | The point turns rose, its halo pulses, the queue names the sigma figure |
| **Disconnect** | Stops publishing for 25 ticks | Within roughly 3 seconds the point greys and climbs to the top of the queue |
| **Wedge** | Repeats the same value for 40 ticks | After roughly 10 readings the point turns violet and reports the identical run length |
| **Clear** | Cancels any active fault | It recovers to teal over the next few seconds |

***

## 7. Where each required C# concept lives

The table below maps each requirement in the brief to the construct which satisfies it and to the file in which that construct lives, given that a claim of this kind is worth precisely as much as the reader's ability to go and check it.

| Requirement | Implementation | File |
|---|---|---|
| **Generics** | `TelemetryPacket<T>`, a `readonly struct` constrained to `struct, IEquatable<T>`, so the reading stays in its own unboxed primitive and equality is a strongly typed call. One closed channel per node keeps it there for the packet's whole life. | `Core/Telemetry/TelemetryPacket.cs`, `Core/Ingestion/TelemetryChannel.cs` |
| **No boxing** | `TelemetryOperator<T>` compiles an expression tree into a typed delegate once per closed generic type, as opposed to casting through `object` upon every packet. `bool`, which has no `+`, is reinterpreted in place rather than boxed. | `Core/Telemetry/TelemetryOperator.cs` |
| **Operator overloading** | `+` aggregates (logical OR for actuators), `-` takes a delta, and the relational operators order packets chronologically. Runs live from the Telemetry Stream screen. | `Core/Telemetry/TelemetryPacket.cs`, `Core/Ingestion/IngestionGateway.cs` |
| **Jagged arrays** | `double[][]`, each row one ingest window at its own exact length, given that a 1 Hz probe and a 10 Hz meter fill genuinely ragged rows. | `Core/Arrays/TelemetryBatchMatrix.cs` |
| **Multi dimensional arrays** | `double[,]`, the rectangular render frame, one contiguous block with a single bounds check per access. | `Core/Arrays/TelemetryBatchMatrix.cs` |
| **Collections** | Staged rows promoted into a `List<TelemetryPacket<double>>` whose capacity is set in advance. Try **Promote staged window** upon the Telemetry Stream screen. | `Core/Arrays/TelemetryBatchMatrix.cs` |
| **Custom collections** | `CircularTelemetryBuffer<T>`, a ring of fixed capacity with a struct enumerator, so steady ingestion allocates nothing. `SensorRegistry`, a hybrid dictionary, ordered list and index per category. | `Core/Collections/` |
| **Recursion** | `TopologyValidator.Descend` walks Facility to Zone to Sub Zone to Node, checking nesting, duplicate MACs, fan out and power budget at every level, with a base case, a depth ceiling and cycle detection. | `Core/Topology/TopologyValidator.cs` |
| **File upload** | *Drop files or click to browse* opens a real `OpenFileDialog`, then uploads over multipart with `StreamContent` over a `FileStream`. The gateway streams to disk in 80 KB chunks with a SHA256 digest upon the same pass. | `Wpf/Services/WpfServices.cs`, `Desktop.Client/Services/GatewayClient.cs`, `Core/Storage/AttachmentStore.cs` |
| **Async API integration** | The console consumes the gateway's event stream as an `IAsyncEnumerable` and reconnects on its own. Nothing upon the path blocks the UI thread. | `Desktop.Client/Services/GatewayClient.cs` |

***

## 8. WPF architecture notes

**Nothing blocks the UI thread.** WPF renders upon a single thread, with the consequence that one `.Result` anywhere upon the HTTP path would freeze the window, the radar included, for the duration of the call, and would deadlock outright where the continuation required that same thread. Every call is therefore `async` the whole way down and is awaited from an `AsyncRelayCommand`.

**`AsyncRelayCommand` guards re entrancy.** While an invocation is in flight `CanExecute` returns false and the bound button disables itself, which is what stops an impatient double click from firing two overlapping POSTs, that being the classic desktop defect in which a node ends up registered twice. Exceptions route to the status bar as opposed to escaping into an `async void`, which in WPF terminates the process.

**One `HttpClient` for the life of the application.** Constructing one per call is the best known misuse of the type, given that each instance holds its own connection pool and a disposed instance leaves its sockets in `TIME_WAIT`, so a console refreshing every second exhausts the ephemeral port range within hours.

**Tiles and radar points are updated in place.** A frame arrives every second, and clearing and repopulating the bound collections would destroy and recreate every container sixty times a minute and restart every animation along with them. `ApplySnapshot` reconciles instead: existing entries are mutated, new nodes inserted, departed nodes removed, and the ordering corrected by `Move`.

**The radar is custom drawn.** `SignalRadarControl` is a `FrameworkElement` with a hand written `OnRender`, as opposed to a `UserControl` full of shapes or a XAML `Storyboard`, the reason being that a storyboard's `Duration` cannot be data bound while every node carries its own rhythm, so animating in code is the only manner in which the picture is able to reflect the data. The control detaches from `CompositionTarget.Rendering` upon unload, given that the event is static and a control which stays subscribed roots its whole view model for ever.

**Validation is declarative.** `RegistrationFormViewModel` implements `INotifyDataErrorInfo` and the `TextBox` style carries a `Validation.HasError` trigger, so an invalid field colours itself with no code behind whatever, and the identical rules run a second time in `PacketValidator` upon the gateway.

**Code behind is minimal.** `MainWindow.xaml.cs` is a constructor and nothing besides. The only remaining code behind is three drag and drop handlers upon the attachments screen, which have no view model equivalent.

**Figure 5 — The event stream, and what happens when it drops.**

```
Console                                    Gateway
─────────────────────────────────────────  ──────────────────────────
GET /api/telemetry/stream              →   SSE, one frame per tick
        │
        │  await foreach (frame in ...)  ←  frame, every second
        │  IAsyncEnumerable, never blocks
        │
        ├──→ ApplySnapshot on the UI thread via IUiDispatcher
        │      mutate existing, insert new, remove departed, Move to reorder
        │
        ╳  connection drops
        │
        ├──→ status bar turns red, names the retry interval
        │
        └──→ retry every 2 s              →  gateway returns
                                             stream resumes, radar repopulates
```

***

## 9. Project layout

```
smartx-wpf/
├─ SmartX.Desktop.sln
├─ run-desktop.ps1            builds, starts the gateway, waits, launches the console
├─ run-desktop.cmd            wrapper for Explorer
├─ Dockerfile                 containerises the gateway only
├─ docker-compose.yml
└─ src/
   ├─ SmartX.Core/            the engine, no ASP.NET, no UI, no Windows
   │  ├─ Domain/              categories, registrations, deployment location
   │  ├─ Telemetry/           TelemetryPacket<T>, TelemetryOperator<T>, envelope
   │  ├─ Collections/         CircularTelemetryBuffer<T>, SensorRegistry
   │  ├─ Arrays/              jagged and rectangular staging
   │  ├─ Topology/            deployment tree and the recursive validator
   │  ├─ Validation/          packet and registration checks
   │  ├─ Engagement/          running statistics and the anomaly engine
   │  ├─ Ingestion/           typed channels and the gateway
   │  ├─ Storage/             streamed attachment store
   │  └─ Simulation/          the mock ESP32 mesh
   │
   ├─ SmartX.Api/             minimal API surface
   │
   ├─ SmartX.Desktop.Client/  all client logic, and NOT a WPF project
   │  ├─ Mvvm/                ObservableObject, RelayCommand, AsyncRelayCommand
   │  ├─ Services/            GatewayClient, IUiDispatcher, IFileDialogService
   │  └─ ViewModels/          shell, radar points, tiles, registration form
   │
   └─ SmartX.Wpf/             the markup layer
      ├─ Theme.xaml           palette and control styles
      ├─ MainWindow.xaml      sidebar shell and screen switching
      ├─ Views/               the gateway screen and the six console screens
      ├─ Controls/            SignalRadarControl, custom drawn
      ├─ Converters/          state to brush, bool to visibility and friends
      └─ Services/            WpfDispatcher, WpfFileDialogService
```

**Why `SmartX.Desktop.Client` exists.** Every view model, command and HTTP call lives in a plain `net10.0` library holding no reference to WPF at all, with the consequence that the presentation logic is unit testable without a `Dispatcher`, and retargeting at MAUI or Avalonia would mean rewriting the markup and nothing besides.

***

## 10. API reference

All responses are camelCase JSON, and enums travel as names as opposed to as integers.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/health` | Liveness, and the container health probe's target. |
| `GET` | `/api/status` | Uptime, node count, packet counters, the three pillars. |
| `GET` | `/api/sensors?category=&search=` | List, optionally filtered. |
| `GET` | `/api/sensors/{mac}` | One node. |
| `POST` | `/api/sensors` | Register or update. `400` returns errors at field level. |
| `DELETE` | `/api/sensors/{mac}` | Deregister. |
| `POST` | `/api/sensors/{mac}/attachments` | `multipart/form-data`, one or many files. |
| `GET` | `/api/sensors/{mac}/attachments/{id}` | Download, supports range requests. |
| `DELETE` | `/api/sensors/{mac}/attachments/{id}` | Remove. |
| `POST` | `/api/telemetry` | Ingest one packet. |
| `POST` | `/api/telemetry/batch` | Ingest many. |
| `GET` | `/api/telemetry/{mac}?take=` | Recent accepted packets. |
| `GET` | `/api/telemetry/stream` | **SSE**, one frame per tick. |
| `GET` | `/api/heartbeat` | One frame on demand. |
| `GET` | `/api/aggregate?a={mac}&b={mac}` | Operator overloading, two live meters. |
| `GET` | `/api/frame` | The rectangular render frame. |
| `POST` | `/api/archive/{mac}` | Promote staged jagged rows into a `List<T>`. |
| `GET` | `/api/topology` | The deployment tree. |
| `GET` | `/api/topology/validate` | Run the recursive validator. |
| `GET` | `/api/topology/resolve/{mac}` | Recursively resolve a node's full path. |
| `POST` | `/api/simulator/toggle` | Pause or resume the mock mesh. |
| `POST` | `/api/simulator/seed?...` | Add more nodes. |
| `POST` | `/api/simulator/fault?mac=&kind=` | `spike`, `disconnect`, `stall` or `clear`. |

### Try it from PowerShell

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5240/api/sensors `
  -ContentType 'application/json' -Body (@{
    macAddress  = '00:1B:44:11:3A:B7'
    alias       = 'Bed 04 Probe'
    category    = 'Environmental'
    facility    = 'Facility A'; zone = 'Zone 1'; subZone = 'SubZone B'
    nodeId      = 'Bed-04-Probe'
    unit        = '%VWC'; minExpected = 10; maxExpected = 70
  } | ConvertTo-Json)
```

Pushing the same reading a second time causes it to be rejected as out of order, which is the quickest available demonstration that the form is a convenience and the gateway is the boundary.

***

## 11. Running the gateway in Docker

Only the API is containerised. A WPF application is a Windows desktop process carrying a window and file dialogs and is therefore not able to run meaningfully inside a Linux container, so it stays upon the operator's machine and the container supplies it with a gateway to talk to.

```powershell
docker compose up --build
```

The compose file maps the container to **port 5240**, precisely where the console looks by default, so no client configuration is required at all, and attachments persist in the `smartx-attachments` volume.

The container declares a `HEALTHCHECK`. Given that the ASP.NET runtime image ships without `curl`, the application answers the probe from its own runtime, as opposed to the image carrying an extra package purely for the purposes of polling itself.

***

## 12. How to demonstrate it

1. **Gateway screen.** Three pillars, two of them visibly locked and labelled with the part in which they arrive.
2. **Overview.** Live figures, already moving, read from the gateway as opposed to counted locally.
3. **Signal Radar.** Eighteen simulated nodes, placed by zone, sweeping.
4. **Register Sensor.** Type a malformed MAC and the hint stays grey while the button stays disabled; fix it, submit, and a new point joins the radar.
5. **Validation happens on the server side as well.** Run the PowerShell replay above and show the second push rejected.
6. **File Attachments.** Drop a `.ini` or a `.png`, then try a `.exe` and show it refused by extension.
7. **Prove the detector.** Spike, disconnect and wedge a node in turn, and watch what the radar does with each.
8. **Telemetry Stream.** Three live readings in three different types, then aggregate two meters and read the result, which names the overloaded `+`.
9. **Deployment Tree.** Run the validator and read what it reports: nodes walked, devices found, depth reached, the depth being the recursion's actual descent as opposed to a configured figure.
10. **Resilience.** Stop the gateway with the console open, so that the status bar turns red and reports the retry; restart it, and the radar resumes on its own.

***

## 13. Design decisions worth defending

**The value never boxes.** A packet is a `readonly struct` living inline within a ring buffer allocated in advance, the decision from category to type is taken once per node at registration, and the arithmetic goes through a delegate compiled once per closed generic type. At thousands of nodes publishing at 1 Hz, the naive alternative to each of these adds tens of thousands of heap allocations per second.

**Push, as opposed to poll.** The console subscribes to the event stream rather than polling it, given that polling beats to the client's rhythm instead of the mesh's, with the consequence that a node which had stopped publishing would appear to carry on until the next request happened to land.

**Drift is smoothed; spikes are not.** Across eighteen nodes several sit beyond two sigma at any given instant purely by chance, so warning upon a single sample would paint the radar amber permanently and train the operator to ignore it, which is why drift waits for a smoothed average. A spike, by contrast, is by definition visible in one sample and is therefore judged instantly and then held for eight seconds, an anomaly which vanishes before the eye reaches it having been displayed but not communicated.

**Baselines forget.** The statistics are an exponentially weighted Welford estimator, Welford because recomputing over a window would be O(n) per packet, and weighted because sensors drift and are periodically recalibrated.

**Actuators are judged differently.** A relay's zero or one value has no meaningful mean, so scoring it against one would flag every legitimate valve movement; for an actuator the anomaly is *chatter*, namely an abnormal number of state transitions, which is the genuine signature of a failing coil.

**The recursion cannot crash the gateway.** `StackOverflowException` cannot be caught in .NET, so the validator stops at a depth ceiling and reports the subtree which is too deep as a finding, and it detects cycles by reference identity as opposed to trusting the input to be a tree.

***

## 14. Troubleshooting

**The status bar says the gateway is unreachable.** The API is not running. Start it with `dotnet run --project src\SmartX.Api` and check <http://localhost:5240/api/health>, the console retrying every two seconds on its own in the meantime.

**`run-desktop.ps1` will not run: running scripts is disabled on this system.** Use `run-desktop.cmd`, or run `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass` first, or follow section 4 manually.

**`error NETSDK1100: To build a project targeting Windows on this operating system`.** The build is running upon Linux or macOS. `src/SmartX.Wpf` needs Windows, whereas the other three build anywhere: `dotnet build src/SmartX.Core src/SmartX.Api src/SmartX.Desktop.Client`.

**`dotnet build` cannot find the .NET 10 SDK.** `dotnet --list-sdks` must show a `10.0.*` entry, given that .NET 9 will not build this solution.

**Port 5240 is already in use.**

```powershell
dotnet run --project src\SmartX.Api --urls http://localhost:5555
dotnet run --project src\SmartX.Wpf -- http://localhost:5555/
```

**Attachments disappear on restart.** They are written under `src/SmartX.Api/App_Data/attachments`, which `.gitignore` excludes, and in Docker they live in the `smartx-attachments` volume instead. The registry itself is deliberately held in memory for Part 1, persistence being outside the scope of this part.

**Several amber points with no fault injected.** This is correct behaviour. The simulated environmental nodes follow a slow sinusoidal profile, so they genuinely wander from their own rolling baseline at the peaks, and a drifting node quotes its smoothed sigma figure in the attention queue.

***

## 15. Repository

| Purpose | Address |
|---|---|
| Public repository | <https://github.com/RwafaBradley/prog7312-part1-RwafaBradley> |
| GitHub Classroom submission | <https://github.com/EMGPRS/prog7312-2026-prog7312-part-1-rwafabradley> |

***

## Version

Part 1, SmartX Data Ingestion and Validation Gateway, WPF desktop architecture. Parts 2 and 3 build upon this code base, and the Real Time Command Stream and Network Topology pillars are present upon the gateway screen and deliberately locked.
