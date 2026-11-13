// src/Gordian.Core/Profiles/CameraZoom.cs
namespace Gordian.Core.Profiles
{
    /// <summary>
    /// The camera zoom a profile last left its character's camera at (#550): the base third-person distance in yalms (never
    /// the lock-on zoom, which only scales what is drawn) and whether the view was first person. Saved on the
    /// <see cref="AccountProfile"/> so each multi-boxed profile keeps its own, and put back on the next login before the
    /// first frame.
    /// </summary>
    public sealed record CameraZoom
    {
        /// <summary>The base camera distance, in yalms.</summary>
        public float Distance { get; init; }

        /// <summary>Whether the camera was in first person.</summary>
        public bool FirstPerson { get; init; }
    }
}
