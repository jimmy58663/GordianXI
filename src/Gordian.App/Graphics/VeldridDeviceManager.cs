// src/Gordian.App/Graphics/VeldridDeviceManager.cs
using System;
using Gordian.Core.Diagnostics;
using NeoVeldrid;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Manages the lifecycle, backend auto-detection, swapchain resizing, and resource factory for Veldrid graphics devices.
    /// A viewport window uses <see cref="InitializeShared"/>: its own swapchain on the process's
    /// <see cref="SharedGraphicsDevice"/> (#300). <see cref="Initialize"/> creates a device of its own with a main swapchain
    /// (offscreen tests).
    /// </summary>
    public sealed class VeldridDeviceManager : IDisposable
    {
        private bool _disposed;
        private SharedGraphicsDevice? _shared;
        private Swapchain? _ownSwapchain;
        private readonly object _privateLock = new();

        public GraphicsDevice? Device { get; private set; }

        /// <summary>This window's swapchain: its own on the shared device, or the owned device's main swapchain.</summary>
        public Swapchain? Swapchain => _ownSwapchain ?? Device?.MainSwapchain;

        /// <summary>The framebuffer of <see cref="Swapchain"/>, drawn into each frame.</summary>
        public Framebuffer? Framebuffer => Swapchain?.Framebuffer;

        /// <summary>The shared device this window draws on, or null for an owned device.</summary>
        public SharedGraphicsDevice? SharedDevice => _shared;

        /// <summary>The lock frames and immediate-context uses are made under: the shared device's, else a private one.</summary>
        public object GpuLock => _shared?.Lock ?? _privateLock;

        /// <summary>Whether <see cref="Swapchain"/> waits for the vertical blank when presenting.</summary>
        public bool SyncToVerticalBlank
        {
            get => Swapchain?.SyncToVerticalBlank ?? false;
            set
            {
                if (Swapchain is { } swapchain && swapchain.SyncToVerticalBlank != value) swapchain.SyncToVerticalBlank = value;
            }
        }

        public ResourceFactory? Factory => Device?.ResourceFactory;
        public GraphicsBackend ActiveBackend => Device?.BackendType ?? GraphicsBackend.Direct3D11;
        public string DeviceName => Device?.DeviceName ?? "None";
        public bool IsInitialized => Device != null;

        public uint CurrentWidth { get; private set; }
        public uint CurrentHeight { get; private set; }

        /// <summary>
        /// Creates a GraphicsDevice of its own and attached main Swapchain targeting the provided surface source.
        /// </summary>
        public void Initialize(
            SwapchainSource swapchainSource,
            uint width,
            uint height,
            GraphicsBackendPreference preference = GraphicsBackendPreference.Auto,
            bool vsync = true,
            bool debug = false)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (Device != null)
            {
                Dispose();
                _disposed = false;
            }

            CurrentWidth = Math.Max(1, width);
            CurrentHeight = Math.Max(1, height);

            var options = SharedGraphicsDevice.CreateOptions(debug, vsync);

            var scDesc = new SwapchainDescription(
                swapchainSource,
                CurrentWidth,
                CurrentHeight,
                PixelFormat.R32_Float,
                vsync,
                false
            );

            Device = CreateDeviceWithFallback(scDesc, options, preference);
            GordianLog.Info("Graphics", $"Initialized Veldrid {Device.BackendType} device: '{Device.DeviceName}' ({CurrentWidth}x{CurrentHeight}, VSync={vsync})");
        }

        /// <summary>
        /// Creates this window's swapchain on the process's shared device, acquiring the device (the first window creates
        /// it). Call under no lock; it takes the device's.
        /// </summary>
        public void InitializeShared(
            SwapchainSource swapchainSource,
            uint width,
            uint height,
            GraphicsBackendPreference preference = GraphicsBackendPreference.Auto,
            bool vsync = true)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Device != null) throw new InvalidOperationException("The device manager is already initialized.");

            CurrentWidth = Math.Max(1, width);
            CurrentHeight = Math.Max(1, height);
            var shared = SharedGraphicsDevice.Acquire(preference);
            try
            {
                lock (shared.Lock)
                {
                    _ownSwapchain = shared.CreateSwapchain(swapchainSource, CurrentWidth, CurrentHeight, vsync);
                }
            }
            catch
            {
                shared.Release();
                throw;
            }
            _shared = shared;
            Device = shared.Device;
            GordianLog.Info("Graphics", $"Created a viewport swapchain on the shared {Device.BackendType} device ({CurrentWidth}x{CurrentHeight}, VSync={vsync}).");
        }

        /// <summary>Presents this window's swapchain (call under <see cref="GpuLock"/>).</summary>
        public void Present()
        {
            if (Device != null && Swapchain is { } swapchain) Device.SwapBuffers(swapchain);
        }

        private static GraphicsDevice CreateDeviceWithFallback(
            SwapchainDescription scDesc,
            GraphicsDeviceOptions options,
            GraphicsBackendPreference preference)
        {
            var candidates = GetBackendCandidates(preference);

            Exception? lastEx = null;
            foreach (var backend in candidates)
            {
                try
                {
                    if (!GraphicsDevice.IsBackendSupported(backend))
                    {
                        continue;
                    }

                    return backend switch
                    {
                        GraphicsBackend.Direct3D11 => GraphicsDevice.CreateD3D11(options, scDesc),
                        GraphicsBackend.Vulkan => GraphicsDevice.CreateVulkan(options, scDesc),
                        _ => throw new PlatformNotSupportedException($"Direct swapchain creation not supported for backend: {backend}")
                    };
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    GordianLog.Warning("Graphics", $"Failed to initialize backend {backend}: {ex.Message}. Attempting fallback...");
                }
            }

            throw new InvalidOperationException(
                $"Failed to initialize any 3D graphics backend for preference '{preference}'.",
                lastEx);
        }

        /// <summary>
        /// Returns the prioritized list of graphics backends to attempt based on user preference and OS.
        /// </summary>
        public static GraphicsBackend[] GetBackendCandidates(GraphicsBackendPreference preference)
        {
            return preference switch
            {
                GraphicsBackendPreference.Direct3D11 => [GraphicsBackend.Direct3D11, GraphicsBackend.Vulkan],
                GraphicsBackendPreference.Vulkan => [GraphicsBackend.Vulkan, GraphicsBackend.Direct3D11],
                // NeoVeldrid has no Metal backend: macOS runs Vulkan through its bundled MoltenVK.
                GraphicsBackendPreference.Metal => [GraphicsBackend.Vulkan],
                _ => GetAutoCandidates()
            };
        }

        private static GraphicsBackend[] GetAutoCandidates()
        {
            if (OperatingSystem.IsWindows())
            {
                // On Windows: Direct3D 11 is optimal, Vulkan is solid backup
                return [GraphicsBackend.Direct3D11, GraphicsBackend.Vulkan];
            }

            if (OperatingSystem.IsLinux())
            {
                // On Linux: Vulkan is native
                return [GraphicsBackend.Vulkan];
            }

            if (OperatingSystem.IsMacOS())
            {
                // On macOS: Vulkan through NeoVeldrid's bundled MoltenVK (no native Metal backend)
                return [GraphicsBackend.Vulkan];
            }

            return [GraphicsBackend.Direct3D11, GraphicsBackend.Vulkan];
        }

        /// <summary>
        /// Resizes this window's swapchain when the hosting window or viewport container changes size (call under
        /// <see cref="GpuLock"/>). Other windows' swapchains are not touched.
        /// </summary>
        public void Resize(uint width, uint height)
        {
            if (Device == null || Swapchain == null || _disposed)
            {
                return;
            }

            uint clampedWidth = Math.Max(1, width);
            uint clampedHeight = Math.Max(1, height);

            if (clampedWidth == CurrentWidth && clampedHeight == CurrentHeight)
            {
                return;
            }

            CurrentWidth = clampedWidth;
            CurrentHeight = clampedHeight;

            try
            {
                Swapchain.Resize(CurrentWidth, CurrentHeight);
            }
            catch (Exception ex)
            {
                GordianLog.Warning("Graphics", $"Swapchain resize error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_shared is { } shared)
            {
                // Only this window's swapchain: the device and its caches belong to every window.
                lock (shared.Lock)
                {
                    try { Device?.WaitForIdle(); } catch { /* shutting down */ }
                    _ownSwapchain?.Dispose();
                    _ownSwapchain = null;
                }
                _shared = null;
                Device = null;
                shared.Release();
                return;
            }

            try
            {
                Device?.WaitForIdle();
            }
            catch
            {
                // Ignore during shutdown
            }

            Device?.Dispose();
            Device = null;
        }
    }
}
