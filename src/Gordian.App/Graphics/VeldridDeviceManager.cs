// src/Gordian.App/Graphics/VeldridDeviceManager.cs
using System;
using Gordian.Core.Diagnostics;
using Veldrid;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Manages the lifecycle, backend auto-detection, swapchain resizing, and resource factory for Veldrid graphics devices.
    /// </summary>
    public sealed class VeldridDeviceManager : IDisposable
    {
        private bool _disposed;

        public GraphicsDevice? Device { get; private set; }
        public Swapchain? MainSwapchain => Device?.MainSwapchain;
        public ResourceFactory? Factory => Device?.ResourceFactory;
        public GraphicsBackend ActiveBackend => Device?.BackendType ?? GraphicsBackend.Direct3D11;
        public string DeviceName => Device?.DeviceName ?? "None";
        public bool IsInitialized => Device != null;

        public uint CurrentWidth { get; private set; }
        public uint CurrentHeight { get; private set; }

        /// <summary>
        /// Creates a GraphicsDevice and attached main Swapchain targeting the provided surface source.
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

            var options = new GraphicsDeviceOptions(
                debug: debug,
                swapchainDepthFormat: PixelFormat.R32_Float,
                syncToVerticalBlank: vsync,
                resourceBindingModel: ResourceBindingModel.Improved,
                preferDepthRangeZeroToOne: true,
                preferStandardClipSpaceYDirection: true
            );

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
                        GraphicsBackend.Metal => GraphicsDevice.CreateMetal(options, scDesc),
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
                GraphicsBackendPreference.Metal => [GraphicsBackend.Metal],
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
                // On macOS: Metal is native
                return [GraphicsBackend.Metal];
            }

            return [GraphicsBackend.Direct3D11, GraphicsBackend.Vulkan];
        }

        /// <summary>
        /// Resizes the main swapchain when the hosting window or viewport container changes size.
        /// </summary>
        public void Resize(uint width, uint height)
        {
            if (Device == null || MainSwapchain == null || _disposed)
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
                MainSwapchain.Resize(CurrentWidth, CurrentHeight);
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
