// src/Gordian.Core/Graphics/CameraMode.cs
namespace Gordian.Core.Graphics
{
    /// <summary>
    /// Represents the operating mode of the 3D viewport camera.
    /// </summary>
    public enum CameraMode
    {
        /// <summary>
        /// Third-person orbital follow camera rotating around the target entity at an adjustable distance.
        /// </summary>
        ThirdPersonOrbital = 0,

        /// <summary>
        /// First-person perspective placed directly at the target entity's eye level.
        /// </summary>
        FirstPerson = 1,

        /// <summary>
        /// Detached 6-DOF fly camera capable of free translation and rotation throughout the 3D world.
        /// </summary>
        FreeCam = 2,
    }
}
