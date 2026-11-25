using Gordian.App.Graphics;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Ui;
using Gordian.Engine.Ui;
using NeoVeldrid;
using System.Runtime.InteropServices;

/// <summary>Optional read-only installed-art validation of the production stock completion renderer.</summary>
internal static class CompletionRenderProbe
{
    public static void Run(StockUiChatInput input)
    {
        string? game = Environment.GetEnvironmentVariable("GORDIAN_COMPLETION_GAME_ROOT");
        if (string.IsNullOrEmpty(game)) { Console.WriteLine("NOT RUN: installed-art completion pixels (set GORDIAN_COMPLETION_GAME_ROOT)"); return; }
        var resources = new ResourceManager(game);
        if (!resources.InitializeFileTable()) throw new InvalidOperationException("Missing file table.");
        var library = UiResourceLibrary.Load(resources) ?? throw new InvalidOperationException("Missing UI resources.");
        var font = StockUiLogFont.FromLibrary(library) ?? throw new InvalidOperationException("Missing log font.");
        if (!library.TryGetMenu("inline", out var inline)) throw new InvalidOperationException("Missing input frame.");
        using var gd = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(debug: true));
        using var color = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(640, 300, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        using var framebuffer = gd.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, color));
        using var staging = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(640, 300, 1, 1, color.Format, TextureUsage.Staging));
        using var commands = gd.ResourceFactory.CreateCommandList();
        using var renderer = new StockUiRenderer(gd, framebuffer.OutputDescription);
        byte[] Draw(StockUiChatInput field, int windows)
        {
            commands.Begin(); commands.SetFramebuffer(framebuffer); commands.ClearColorTarget(0, RgbaFloat.Black); commands.End(); gd.SubmitCommands(commands);
            renderer.Begin(library);
            StockUiChatWindow.DrawInput(renderer, library, font, inline, new(5, 250, 1, false), 620, field, 0);
            bool expectedWindows = renderer.DrawnWindows.Count == windows;
            renderer.End(framebuffer, 640, 300);
            commands.Begin(); commands.CopyTexture(color, staging); commands.End(); gd.SubmitCommands(commands); gd.WaitForIdle();
            if (!expectedWindows) throw new InvalidOperationException("Completion frame missing.");
            var mapped = gd.Map(staging, MapMode.Read);
            var bytes = new byte[640 * 300 * 4];
            try { for (int y = 0; y < 300; y++) Marshal.Copy(mapped.Data + (int)(y * mapped.RowPitch), bytes, y * 640 * 4, 640 * 4); }
            finally { gd.Unmap(staging); }
            return bytes;
        }
        var plain = new StockUiChatInput(); plain.Open(); plain.InsertText(input.Text);
        byte[] closed = Draw(plain, 1);
        byte[] open = Draw(input, 2);
        int changed = 0, bright = 0;
        for (int y = 202; y < 250; y++) for (int x = 5; x < 625; x++)
        {
            int offset = (y * 640 + x) * 4;
            if (!closed.AsSpan(offset, 3).SequenceEqual(open.AsSpan(offset, 3))) changed++;
            if (open[offset] > 180 && open[offset + 1] > 180 && open[offset + 2] > 180) bright++;
        }
        if (changed < 500 || bright < 100) throw new InvalidOperationException($"No rendered candidate text: changed={changed}, bright={bright}");
        Console.WriteLine($"PASS: production stock completion pixels, changed={changed}, bright={bright}, candidates={input.Completion.Rows.Count}; read-only installed art");
    }
}
