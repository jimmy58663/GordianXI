// src/Gordian.App/Graphics/GpuPassTimer.cs
using System;
using NeoVeldrid;
using Silk.NET.Direct3D11;

namespace Gordian.App.Graphics
{
    /// <summary>The viewport passes the performance overlay times (#30), in drawing order.</summary>
    public enum ViewportPass
    {
        /// <summary>Tier 1: the 3D scene (terrain, sky, models, effects, post-process), or the lobby's screens.</summary>
        Scene,

        /// <summary>Tier 2: the stock 2D UI.</summary>
        StockUi,

        /// <summary>Tier 3: the ImGui overlays (the performance overlay itself).</summary>
        Overlay,
    }

    /// <summary>
    /// Measures how long the GPU spends on each viewport pass (#30). NeoVeldrid exposes no query API, so each backend needs
    /// its own native implementation; <see cref="Create"/> returns the one for the device's backend, or a timer that reports
    /// <see cref="IsSupported"/> false. Created only while the detailed overlay is shown, so a hidden overlay issues no
    /// queries. Every call is made under the device's GPU lock (the immediate context is not thread-safe).
    /// </summary>
    public abstract class GpuPassTimer : IDisposable
    {
        public const int PassCount = 3;

        /// <summary>Whether the backend can time passes; false shows "n/a" in the overlay.</summary>
        public abstract bool IsSupported { get; }

        /// <summary>A short description for the overlay ("D3D11 timestamp queries", or why there are none).</summary>
        public abstract string Description { get; }

        /// <summary>Starts a frame's measurement (before the scene pass is submitted).</summary>
        public abstract void BeginFrame();

        /// <summary>Marks the end of a pass (after its commands are submitted).</summary>
        public abstract void EndPass(ViewportPass pass);

        /// <summary>Ends the frame's measurement (after the last pass).</summary>
        public abstract void EndFrame();

        /// <summary>
        /// Copies the most recent finished frame's pass times (milliseconds) into <paramref name="passMs"/> (one per
        /// <see cref="ViewportPass"/>); false while none has finished. Results lag a few frames behind, never stall.
        /// </summary>
        public abstract bool TryReadLatest(Span<float> passMs);

        public abstract void Dispose();

        /// <summary>The timer for a device's backend (D3D11 timestamp queries on Windows; unsupported elsewhere for now).</summary>
        public static GpuPassTimer Create(GraphicsDevice device)
        {
            ArgumentNullException.ThrowIfNull(device);
            if (OperatingSystem.IsWindows() && device.BackendType == GraphicsBackend.Direct3D11)
            {
                try
                {
                    return new D3D11GpuPassTimer(device);
                }
                catch (Exception ex)
                {
                    return new UnsupportedGpuPassTimer($"D3D11 timestamp queries failed: {ex.Message}");
                }
            }
            return new UnsupportedGpuPassTimer($"not measured on {device.BackendType} yet");
        }
    }

    /// <summary>A backend without GPU pass timing: the overlay shows the CPU side only.</summary>
    public sealed class UnsupportedGpuPassTimer : GpuPassTimer
    {
        public UnsupportedGpuPassTimer(string description) => Description = description;

        public override bool IsSupported => false;
        public override string Description { get; }
        public override void BeginFrame() { }
        public override void EndPass(ViewportPass pass) { }
        public override void EndFrame() { }
        public override bool TryReadLatest(Span<float> passMs) => false;
        public override void Dispose() { }
    }

    /// <summary>
    /// D3D11 pass timing with timestamp queries on the immediate context: a <c>D3D11_QUERY_TIMESTAMP_DISJOINT</c> around the
    /// frame and a <c>D3D11_QUERY_TIMESTAMP</c> before the first pass and after each pass, read back without flushing
    /// (<c>D3D11_ASYNC_GETDATA_DONOTFLUSH</c>) a few frames later from a small ring, so measuring never waits for the GPU.
    /// A frame whose disjoint query reports a clock change is dropped. Written from the public Direct3D 11 query
    /// documentation (Microsoft Learn: ID3D11DeviceContext::GetData, D3D11_QUERY).
    /// </summary>
    internal sealed unsafe class D3D11GpuPassTimer : GpuPassTimer
    {
        /// <summary>Frames in flight the ring covers; a frame finds no free slot only if the GPU is that far behind.</summary>
        private const int RingSize = 5;

        /// <summary>D3D11_ASYNC_GETDATA_DONOTFLUSH.</summary>
        private const uint DoNotFlush = 1;

        private sealed class Slot
        {
            public ID3D11Query* Disjoint;
            public readonly ID3D11Query*[] Stamps = new ID3D11Query*[PassCount + 1];
            public bool Pending;
        }

        private readonly ID3D11DeviceContext* _context;
        private readonly Slot[] _slots = new Slot[RingSize];
        private Slot? _current;
        private int _next;
        private readonly float[] _latest = new float[PassCount];
        private bool _hasLatest;
        private bool _disposed;

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public D3D11GpuPassTimer(GraphicsDevice device)
        {
            var info = device.GetD3D11Info();
            var d3dDevice = (ID3D11Device*)info.Device;
            if (d3dDevice == null) throw new InvalidOperationException("no D3D11 device");
            ID3D11DeviceContext* context = null;
            d3dDevice->GetImmediateContext(&context);
            if (context == null) throw new InvalidOperationException("no immediate context");
            _context = context;
            try
            {
                for (int i = 0; i < RingSize; i++)
                {
                    var slot = new Slot { Disjoint = CreateQuery(d3dDevice, Query.TimestampDisjoint) };
                    for (int s = 0; s < slot.Stamps.Length; s++) slot.Stamps[s] = CreateQuery(d3dDevice, Query.Timestamp);
                    _slots[i] = slot;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static ID3D11Query* CreateQuery(ID3D11Device* device, Query type)
        {
            var desc = new QueryDesc(type, 0);
            ID3D11Query* query = null;
            int hr = device->CreateQuery(&desc, &query);
            if (hr < 0 || query == null) throw new InvalidOperationException($"CreateQuery({type}) failed: 0x{hr:X8}");
            return query;
        }

        public override bool IsSupported => true;
        public override string Description => "D3D11 timestamp queries";

        public override void BeginFrame()
        {
            if (_disposed) return;
            Collect();
            _current = null;
            // Take the next slot whose results have been read; none free means the GPU is far behind, so skip this frame.
            for (int i = 0; i < RingSize; i++)
            {
                var slot = _slots[(_next + i) % RingSize];
                if (slot.Pending) continue;
                _current = slot;
                _next = (_next + i + 1) % RingSize;
                break;
            }
            if (_current == null) return;
            _context->Begin((ID3D11Asynchronous*)_current.Disjoint);
            _context->End((ID3D11Asynchronous*)_current.Stamps[0]);
        }

        public override void EndPass(ViewportPass pass)
        {
            if (_current == null) return;
            _context->End((ID3D11Asynchronous*)_current.Stamps[(int)pass + 1]);
        }

        public override void EndFrame()
        {
            if (_current == null) return;
            _context->End((ID3D11Asynchronous*)_current.Disjoint);
            _current.Pending = true;
            _current = null;
        }

        /// <summary>Reads every pending slot whose results are ready, keeping the newest frame's times.</summary>
        private void Collect()
        {
            // Oldest first, so the newest ready frame is the one kept.
            for (int i = 0; i < RingSize; i++)
            {
                var slot = _slots[(_next + i) % RingSize];
                if (!slot.Pending) continue;
                QueryDataTimestampDisjoint disjoint;
                if (_context->GetData((ID3D11Asynchronous*)slot.Disjoint, &disjoint, (uint)sizeof(QueryDataTimestampDisjoint), DoNotFlush) != 0) continue;
                Span<ulong> stamps = stackalloc ulong[PassCount + 1];
                bool ready = true;
                for (int s = 0; s < stamps.Length && ready; s++)
                {
                    ulong value;
                    ready = _context->GetData((ID3D11Asynchronous*)slot.Stamps[s], &value, sizeof(ulong), DoNotFlush) == 0;
                    stamps[s] = value;
                }
                if (!ready) continue;
                slot.Pending = false;
                if ((bool)disjoint.Disjoint || disjoint.Frequency == 0) continue;
                double toMs = 1000.0 / disjoint.Frequency;
                for (int p = 0; p < PassCount; p++)
                {
                    _latest[p] = stamps[p + 1] >= stamps[p] ? (float)((stamps[p + 1] - stamps[p]) * toMs) : 0f;
                }
                _hasLatest = true;
            }
        }

        public override bool TryReadLatest(Span<float> passMs)
        {
            if (!_hasLatest) return false;
            _latest.AsSpan(0, Math.Min(PassCount, passMs.Length)).CopyTo(passMs);
            return true;
        }

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var slot in _slots)
            {
                if (slot == null) continue;
                if (slot.Disjoint != null) slot.Disjoint->Release();
                for (int s = 0; s < slot.Stamps.Length; s++)
                {
                    if (slot.Stamps[s] != null) slot.Stamps[s]->Release();
                }
            }
            if (_context != null) _context->Release();
        }
    }
}
