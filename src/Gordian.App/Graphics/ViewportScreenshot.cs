// src/Gordian.App/Graphics/ViewportScreenshot.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Config;
using Gordian.Core.Diagnostics;
using Gordian.Engine.Actions;
using NeoVeldrid;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// Saves one rendered frame of a viewport to a PNG (<c>/screenshot</c>). The frame is copied to a staging texture
    /// before it is presented and read back on the next frame, when the GPU has finished with it, so rendering does not
    /// wait. Encoding and the file write run off the render thread.
    /// </summary>
    internal sealed class ViewportScreenshot : IDisposable
    {
        private ScreenshotRequest? _pending;
        private ScreenshotRequest? _copied;
        private Texture? _staging;
        private CommandList? _commands;
        private int _saving;

        /// <summary>Called with the line for the chat log when a screenshot is saved or fails, on any thread.</summary>
        public Action<string>? Report { get; set; }

        /// <summary>Takes a request for the next frame. False while another screenshot is still in progress.</summary>
        public bool Request(ScreenshotRequest request)
        {
            if (Volatile.Read(ref _saving) != 0 || Volatile.Read(ref _copied) != null) return false;
            return Interlocked.CompareExchange(ref _pending, request, null) == null;
        }

        /// <summary>
        /// Copies the frame if a request is waiting for this point of it: after the scene for one hiding the UI,
        /// otherwise after the last overlay. Call under the GPU lock, before presenting.
        /// </summary>
        public void CaptureIfDue(GraphicsDevice device, Framebuffer framebuffer, bool uiDrawn)
        {
            var request = Volatile.Read(ref _pending);
            if (request == null || request.HideUi == uiDrawn) return;

            try
            {
                var source = framebuffer.ColorTargets[0].Target;
                _staging = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                    source.Width, source.Height, 1, 1, source.Format, TextureUsage.Staging));
                _commands ??= device.ResourceFactory.CreateCommandList();
                _commands.Begin();
                _commands.CopyTexture(source, _staging);
                _commands.End();
                device.SubmitCommands(_commands);
                Volatile.Write(ref _copied, request);
            }
            catch (Exception ex)
            {
                Fail(ex);
                DisposeStaging();
            }
            Volatile.Write(ref _pending, null);
        }

        /// <summary>Reads back the frame copied last time and starts saving it. Call under the GPU lock, once a frame.</summary>
        public void CollectIfReady(GraphicsDevice device)
        {
            if (Volatile.Read(ref _copied) is not { } request || _staging is not { } staging) return;

            try
            {
                int width = (int)staging.Width, height = (int)staging.Height;
                bool bgra = IsBgra(staging.Format);
                byte[] pixels = new byte[width * height * 4];
                var mapped = device.Map(staging, MapMode.Read, 0);
                try
                {
                    unsafe
                    {
                        var rows = new ReadOnlySpan<byte>((void*)mapped.Data, (int)mapped.SizeInBytes);
                        for (int y = 0; y < height; y++)
                        {
                            rows.Slice((int)(y * mapped.RowPitch), width * 4).CopyTo(pixels.AsSpan(y * width * 4));
                        }
                    }
                }
                finally
                {
                    device.Unmap(staging, 0);
                }

                Volatile.Write(ref _saving, 1);
                _ = Task.Run(() => Save(request, pixels, width, height, bgra));
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            finally
            {
                DisposeStaging();
                Volatile.Write(ref _copied, null);
            }
        }

        private void Save(ScreenshotRequest request, byte[] pixels, int width, int height, bool bgra)
        {
            try
            {
                string path = ScreenshotFiles.ResolvePath(GordianStorage.ScreenshotsDirectory, request.Name, DateTime.Now);
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                    PngEncoder.Write(file, pixels, width, height, bgra);
                }
                Report?.Invoke($"Screenshot saved: {path}");
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            finally
            {
                Volatile.Write(ref _saving, 0);
            }
        }

        private void Fail(Exception ex)
        {
            GordianLog.Warning("Graphics", $"Screenshot failed: {ex.Message}");
            Report?.Invoke($"Screenshot failed: {ex.Message}");
        }

        private static bool IsBgra(PixelFormat format) => format switch
        {
            PixelFormat.B8_G8_R8_A8_UNorm or PixelFormat.B8_G8_R8_A8_UNorm_SRgb => true,
            PixelFormat.R8_G8_B8_A8_UNorm or PixelFormat.R8_G8_B8_A8_UNorm_SRgb => false,
            _ => throw new NotSupportedException($"Unsupported frame format {format}.")
        };

        private void DisposeStaging()
        {
            _staging?.Dispose();
            _staging = null;
        }

        /// <summary>Call under the GPU lock.</summary>
        public void Dispose()
        {
            DisposeStaging();
            _commands?.Dispose();
            _commands = null;
        }
    }
}
