// src/Gordian.Engine/Ui/Lobby/LobbyStage.cs
using System;
using System.Numerics;

namespace Gordian.Engine.Ui.Lobby
{
    /// <summary>
    /// The stage behind character select, deletion and creation (#552 round 2): the grassland of <c>ROM/1/5.DAT</c>
    /// (file 83935, the model file of zone id 300 in the client's zone numbering: 0x147B3 + 300 - 256), with the preview
    /// character standing on it. Retail's capture (<c>Lobby_Title_Select_Create_Delete_Retail.mp4</c>, 2026-11-13) shows the
    /// same place for all three: the character in front of a rock ridge that rises to pink peaks on the right, purple dusk on
    /// the character list, red sunset on the delete list, blue daylight in creation.
    /// <para>
    /// The file is a zone model file (a 0x1C map, 0x2E meshes, the <c>weat/suny</c> 0x2F environments 00:00-21:00, stars,
    /// moon), so the zone renderer draws it as any zone. xi-tools names it the creation stage
    /// (<c>docs/model/character_creation_dats.md</c>, "Zone / stage backdrop"); it holds no camera routes or routines.
    /// </para>
    /// <para>
    /// <b>Unverified against retail:</b> the spot and direction (matched by eye to the capture's skyline), the hours (matched
    /// to its sky colours) and whether retail's clock runs; retail's creation plays per-race cameras and motions from other
    /// files (xi-tools), not drawn here: the character stands at the same spot in its idle stance.
    /// </para>
    /// </summary>
    public static class LobbyStage
    {
        /// <summary>The zone id whose model file is the stage.</summary>
        public const int ZoneId = 300;

        /// <summary>The stage's weather (its only environment set is <c>suny</c>).</summary>
        public const string Weather = "suny";

        /// <summary>Where the character stands, in display axes (the renderer's: internal X and Y negated); the ground there is at 0.</summary>
        public static readonly Vector3 Spot = Vector3.Zero;

        /// <summary>The way the camera looks across the stage, degrees from display +X toward +Z.</summary>
        public const float HeadingDegrees = 190f;

        /// <summary>The character list's hour (purple dusk in the capture).</summary>
        public const float ListHour = 16f;

        /// <summary>The delete list's hour (red sunset in the capture).</summary>
        public const float DeleteHour = 18f;

        /// <summary>Character creation's hour (blue daylight in the capture).</summary>
        public const float CreationHour = 12f;

        /// <summary>Whether the lobby screen stands the character on the stage: the list (select and delete), creation and the fade after choosing.</summary>
        public static bool Shows(LobbyController lobby) =>
            !lobby.IsLicencePending && lobby.Screen is LobbyScreen.CharacterList or LobbyScreen.Creation or LobbyScreen.Entering;

        /// <summary>The stage's hour on the lobby's screen.</summary>
        public static float HourOf(LobbyController lobby) => lobby.Screen switch
        {
            LobbyScreen.Creation => CreationHour,
            _ => lobby.IsDeleteList ? DeleteHour : ListHour,
        };

        /// <summary>
        /// The stage camera for a preview camera: the preview looks along -Z at a model standing at the origin, so the same
        /// camera turned to <see cref="HeadingDegrees"/> and moved to <see cref="Spot"/> shows the stage behind the model
        /// as it stands there.
        /// </summary>
        public static (Vector3 Eye, Vector3 Target) Place(Vector3 previewEye, Vector3 previewTarget)
        {
            float heading = HeadingDegrees * MathF.PI / 180f;
            var forward = new Vector3(MathF.Cos(heading), 0f, MathF.Sin(heading));
            // CreateRotationY(a) takes -Z to (-sin a, 0, -cos a).
            var turn = Matrix4x4.CreateRotationY(MathF.Atan2(-forward.X, -forward.Z));
            return (Spot + Vector3.Transform(previewEye, turn), Spot + Vector3.Transform(previewTarget, turn));
        }
    }
}
