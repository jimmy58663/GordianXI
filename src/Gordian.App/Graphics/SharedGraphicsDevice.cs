// src/Gordian.App/Graphics/SharedGraphicsDevice.cs
using System;
using Gordian.Core.Diagnostics;
using NeoVeldrid;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The one <see cref="GraphicsDevice"/> of the process (#300), shared by every viewport window: each window creates
    /// its own <see cref="Swapchain"/> on it (<see cref="CreateSwapchain"/>) and draws with the shared GPU caches in
    /// <see cref="Resources"/>, so two windows in one zone upload it once. The device is created without a swapchain when
    /// the first window acquires it and disposed when the last one releases it.
    /// <para>
    /// The render loops of the windows run on their own threads; each draws a whole frame (record, submit, present) under
    /// <see cref="Lock"/>, as does every other use of the device's immediate context (zone uploads, swapchain resizes).
    /// Resizes and swapchain failures stay per window.
    /// </para>
    /// </summary>
    public sealed class SharedGraphicsDevice
    {
        private static readonly object s_gate = new();
        private static SharedGraphicsDevice? s_instance;

        private int _references;

        private SharedGraphicsDevice(GraphicsDevice device)
        {
            Device = device;
            Resources = new GpuSharedResources(device, Lock);
        }

        public GraphicsDevice Device { get; }

        /// <summary>The lock every frame and every immediate-context use on <see cref="Device"/> is made under.</summary>
        public object Lock { get; } = new();

        /// <summary>The GPU caches and layouts every window shares.</summary>
        public GpuSharedResources Resources { get; }

        /// <summary>The device while one exists (a window holds it), else null.</summary>
        public static SharedGraphicsDevice? Current
        {
            get { lock (s_gate) return s_instance; }
        }

        /// <summary>
        /// The process's device, created on the first call (with <paramref name="preference"/>'s backend and its
        /// fallbacks); later callers share it whatever backend they would prefer. Pair with <see cref="Release"/>.
        /// </summary>
        public static SharedGraphicsDevice Acquire(GraphicsBackendPreference preference, bool debug = false)
        {
            lock (s_gate)
            {
                if (s_instance == null)
                {
                    var options = CreateOptions(debug, vsync: true);
                    var device = CreateHeadlessDevice(options, preference);
                    s_instance = new SharedGraphicsDevice(device);
                    GordianLog.Info("Graphics", $"Created the shared Veldrid {device.BackendType} device: '{device.DeviceName}' (one per process, #300).");
                }
                else if (preference != GraphicsBackendPreference.Auto && !Matches(preference, s_instance.Device.BackendType))
                {
                    GordianLog.Info("Graphics", $"A viewport preferred {preference}; it shares the open {s_instance.Device.BackendType} device.");
                }
                s_instance._references++;
                return s_instance;
            }
        }

        /// <summary>Gives back a reference from <see cref="Acquire"/>; the last one disposes the device and its caches.</summary>
        public void Release()
        {
            lock (s_gate)
            {
                if (_references <= 0) return;
                if (--_references > 0) return;
                if (ReferenceEquals(s_instance, this)) s_instance = null;
            }

            lock (Lock)
            {
                try { Device.WaitForIdle(); } catch { /* shutting down */ }
                Resources.Dispose();
                Device.Dispose();
            }
            GordianLog.Info("Graphics", "Disposed the shared graphics device (no viewport window left).");
        }

        /// <summary>A swapchain for one window's surface on the shared device (call under <see cref="Lock"/>).</summary>
        public Swapchain CreateSwapchain(SwapchainSource source, uint width, uint height, bool vsync)
        {
            var description = new SwapchainDescription(source, Math.Max(1, width), Math.Max(1, height), PixelFormat.R32_Float, vsync, false);
            return Device.ResourceFactory.CreateSwapchain(description);
        }

        internal static GraphicsDeviceOptions CreateOptions(bool debug, bool vsync) => new(
            debug: debug,
            swapchainDepthFormat: PixelFormat.R32_Float,
            syncToVerticalBlank: vsync,
            resourceBindingModel: ResourceBindingModel.Improved,
            preferDepthRangeZeroToOne: true,
            preferStandardClipSpaceYDirection: true);

        private static bool Matches(GraphicsBackendPreference preference, GraphicsBackend backend) => preference switch
        {
            GraphicsBackendPreference.Direct3D11 => backend == GraphicsBackend.Direct3D11,
            GraphicsBackendPreference.Vulkan or GraphicsBackendPreference.Metal => backend == GraphicsBackend.Vulkan,
            _ => true
        };

        private static GraphicsDevice CreateHeadlessDevice(GraphicsDeviceOptions options, GraphicsBackendPreference preference)
        {
            Exception? lastEx = null;
            foreach (var backend in VeldridDeviceManager.GetBackendCandidates(preference))
            {
                try
                {
                    if (!GraphicsDevice.IsBackendSupported(backend)) continue;
                    return backend switch
                    {
                        GraphicsBackend.Direct3D11 => GraphicsDevice.CreateD3D11(options),
                        GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(options),
                        _ => throw new PlatformNotSupportedException($"Backend {backend} is not supported.")
                    };
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    GordianLog.Warning("Graphics", $"Failed to initialize backend {backend}: {ex.Message}. Attempting fallback...");
                }
            }
            throw new InvalidOperationException($"Failed to initialize any 3D graphics backend for preference '{preference}'.", lastEx);
        }
    }
}
