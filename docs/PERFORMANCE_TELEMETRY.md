# GordianXI Performance & Telemetry Reference Guide

This document establishes the runtime telemetry definitions, monitoring architecture, and performance thresholds (**Good**, **Warning**, **Bad**) for GordianXI.

---

## 📊 Telemetry Metrics & Operational Thresholds

| Metric Display | Category | Description & Units | 🟢 Good (Optimal) | 🟡 Warning (Investigate) | 🔴 Bad (Critical Issue) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **`IN` (Rate / Bandwidth)** | Network | Inbound chunk rate (`c/s`) and throughput (`KB/s`) | **4 – 30 c/s**<br>`< 25 KB/s` | **30 – 100 c/s**<br>`25 – 100 KB/s` | **> 150 c/s**<br>`> 250 KB/s` *(packet flood)* |
| **`OUT` (Rate / Bandwidth)** | Network | Outbound chunk rate (`c/s`) and throughput (`KB/s`) | **4 – 15 c/s**<br>`< 10 KB/s` | **15 – 50 c/s**<br>`10 – 50 KB/s` | **> 80 c/s**<br>`> 100 KB/s` *(runaway queue)* |
| **`DROPS`** | Network | Sequence number gaps & duplicate UDP chunks dropped | **0** | **1 – 5** / min *(sporadic UDP packet loss)* | **> 10** / min *(severe packet loss / desync)* |
| **`LATENCY`** | Network / Dispatch | Microsecond duration to decode & dispatch packet opcode | **< 5 µs** | **10 – 50 µs** | **> 100 µs** *(stalling packet processing)* |
| **`HEAP`** | Memory | Total live managed heap size in Megabytes (`MB`) | **25 – 150 MB** | **250 – 500 MB** | **> 1,000 MB (1 GB)** *(potential leak)* |
| **`ALLOC RATE` (`+MB/s`)** | Memory | Heap allocation velocity in Megabytes per second | **< 1.0 MB/s** *(near-zero in steady state)* | **5.0 – 20.0 MB/s** *(heap allocations in tick loop)* | **> 50.0 MB/s** *(causes aggressive GC pauses)* |
| **`GC(0/1/2)`** | Memory | Cumulative collections across Gen 0, Gen 1, and Gen 2 | **Gen 0: < 1 / s**<br>Gen 1: < 0.1 / s<br>**Gen 2: 0** | **Gen 0: 2 – 5 / s**<br>Gen 1: 0.5 – 1 / s<br>**Gen 2: > 0** *(rare)* | **Frequent Gen 1 / Gen 2 collections** *(causes micro-stutters)* |
| **`PAUSE %`** | Memory | Ratio of wall-clock time spent in GC pause | **< 0.5%** | **1.0% – 3.0%** | **> 5.0%** *(noticeable frame hitches)* |
| **`GRID`** | 3D Simulation | 3D uniform spatial grid query duration (Radius, Nearest, Cone) | **< 5 µs avg**<br>`< 20 µs peak` | **20 – 50 µs avg**<br>`50 – 100 µs peak` | **> 100 µs avg** *(degenerate grid partitioning)* |
| **`RECKONING`** | 3D Simulation | Dead-reckoning position extrapolation cycle time | **< 20 µs** for 100 ents<br>`(> 5M ents/s)` | **50 – 100 µs** for 100 ents<br>`(1M - 2M ents/s)` | **> 250 µs** for 100 ents<br>`(< 400K ents/s; impacts 60 FPS budget)` |

---

## 🔍 Metric Deep-Dive & Diagnostic Meaning

### 1. Network Throughput & Packet Rates (`IN` / `OUT`)
* **What it measures:** Inbound and outbound datagram packet chunks (`0x0A` framed structures) and total throughput in Kilobytes per second (`KB/s`), calculated over a rolling 250ms window alongside a 30-second moving average.
* **Interpretation:**
  * Steady state (idle / running around): ~4 c/s (4Hz position ping-pong `0x015`).
  * Busy state (combat action bursts, party status updates): 15 – 30 c/s.
  * Spikes above 150 c/s suggest an unhandled server packet loop or flooding bug.

### 2. Sequence Discrepancies (`DROPS`)
* **What it measures:** Unacknowledged UDP sequence gaps and duplicate datagrams safely dropped by sequence sliding window deduplication.
* **Interpretation:**
  * A non-zero count indicates real network packet loss between client and server.
  * Occasional drops (1–5) are normal over commercial Wi-Fi/Internet connections.
  * Sustained increases indicate router packet drop, firewall throttling, or server socket buffer overflow.

### 3. Opcode Dispatch Latency (`LATENCY`)
* **What it measures:** High-resolution time (using CPU timestamp counter `Stopwatch.GetTimestamp()`) spent in `PacketDispatcher` locating the opcode handler in the $O(1)$ 512-slot array and executing the zero-allocation `readonly ref struct` decoder.
* **Interpretation:**
  * Normal dispatch takes **< 1 µs** to **3 µs**.
  * Latency above 50 µs indicates that an opcode handler is executing blocking I/O, disk access, or complex locks inside the network receiver thread.

### 4. Managed Heap & Allocation Velocity (`HEAP` and `+MB/s`)
* **What it measures:**
  * **HEAP:** Live managed memory tracked by `GC.GetTotalMemory(false)`.
  * **ALLOC RATE (`+MB/s`):** Monotonic bytes allocated by the managed runtime (`GC.GetTotalAllocatedBytes()`) divided by elapsed time.
* **Interpretation:**
  * In steady-state operation, GordianXI's zero-allocation design maintains allocation rates **below 0.5 MB/s**.
  * Higher rates (> 10 MB/s) indicate accidental object allocations, string formatting in hot loops, or boxing in packet dispatchers.

### 5. Garbage Collection Pressures (`GC(0/1/2)` and `PAUSE %`)
* **What it measures:** Cumulative collections in .NET 10 generational GC and the GC pause time percentage via `GC.GetGCMemoryInfo().PauseTimePercentage`.
* **Interpretation:**
  * Gen 0 collections are fast (< 1ms).
  * Gen 2 collections freeze all threads and can take 10–50ms. A growing Gen 2 count during normal gameplay is a warning of long-lived object leaks or large array allocations (LOH).

### 6. 3D Spatial Partition Grid (`GRID`)
* **What it measures:** Execution duration for 3D uniform spatial bin queries in `SpatialPartitionGrid`:
  * Spherical radius searches (`GetEntitiesInRadius`)
  * Nearest target queries (`GetNearestEntity`)
  * Directional FOV vision cone searches (`GetEntitiesInCone`)
* **Interpretation:**
  * Standard hash-grid cell lookup and distance check should complete in **1 – 4 µs**.
  * Sustained latency > 50 µs indicates degenerate clustering (e.g. hundreds of entities in a single 10-yalm cell) or excessive search radii.

### 7. Entity Dead-Reckoning Latency (`RECKONING`)
* **What it measures:** High-resolution duration to extrapolate positions, headings, and velocities across all active world entities between 250ms network ticks.
* **Interpretation:**
  * A 60 FPS frame has a total budget of **16.6 ms (16,666 µs)**.
  * Dead reckoning should consume less than **0.1%** of that frame budget (< 20 µs).
  * You can benchmark dead-reckoning cycle throughput at any time in the State Inspector using the **Benchmark** button.

---

## 🛠️ Performance Troubleshooting Runbook

| Symptom | Root Cause | Remediation |
| :--- | :--- | :--- |
| **`DROPS` increasing continuously** | Network packet loss or socket buffer overrun | Verify UDP network connection; ensure `EnableSequenceDeduplication = true` on `SessionNetworkManager`. |
| **`ALLOC RATE` > 20 MB/s** | Allocation in packet or simulation hot path | Profile with `dotnet-trace` or Visual Studio Diagnostic Tools; inspect packet parsing loops for `new byte[]`, boxing, or LINQ queries. |
| **`LATENCY` > 50 µs** | Blocking operation on network thread | Ensure packet handlers do not perform synchronous file I/O or acquire contested locks. Hand off heavy tasks via Channels/Tasks. |
| **`GRID` > 100 µs** | Cell size mismatch or large search radius | Verify `cellSize` on `SpatialPartitionGrid` (default: 10.0 yalms); ensure radius search does not exceed 100 yalms. |
| **`PAUSE %` > 2%** | LOH allocations or frequent Gen 1/2 sweeps | Utilize `ArrayPool<byte>.Shared` and `Span<byte>` buffers instead of allocating arrays in datagram buffers. |
