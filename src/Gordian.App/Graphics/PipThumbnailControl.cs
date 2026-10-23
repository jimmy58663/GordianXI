// src/Gordian.App/Graphics/PipThumbnailControl.cs
using Avalonia;
using Avalonia.Controls;
using Gordian.Core.Network;

namespace Gordian.App.Graphics
{
    /// <summary>
    /// The live picture on a PiP deck card (#302): a <see cref="VeldridViewportControl"/> in thumbnail mode showing one
    /// background character's scene. It is a native child with a swapchain of its own on the process's shared device
    /// (#300), created when the card appears and gone when the deck closes or the character is swapped in, so a hidden
    /// deck costs nothing. It draws a few frames a second (<see cref="ViewportRenderSettings.PipThumbnailFrameRate"/>),
    /// with no stock UI, overlays or sound; the main viewport keeps ticking background characters' game logic.
    /// </summary>
    public sealed class PipThumbnailControl : VeldridViewportControl
    {
        public static readonly StyledProperty<CharacterSession?> SessionProperty =
            AvaloniaProperty.Register<PipThumbnailControl, CharacterSession?>(nameof(Session));

        public PipThumbnailControl()
        {
            IsThumbnail = true;
            RenderMode = ViewportRenderMode.Paused;
        }

        /// <summary>The character this thumbnail shows.</summary>
        public CharacterSession? Session
        {
            get => GetValue(SessionProperty);
            set => SetValue(SessionProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == SessionProperty)
            {
                ActiveSession = Session;
                // Idle (no frames) while it shows no character, e.g. when live thumbnails are off.
                RenderMode = Session == null ? ViewportRenderMode.Paused : ViewportRenderMode.Background;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            // Lets go of the session's world events; the card's tab keeps the session.
            ActiveSession = null;
            base.OnDetachedFromVisualTree(e);
        }
    }
}
