// src/Gordian.Core/Graphics/ViewportCamera.cs
using System;
using System.Numerics;

namespace Gordian.Core.Graphics
{
    /// <summary>
    /// High-performance 3D viewport camera supporting Third-Person Orbital, First-Person,
    /// and 6-DOF Freecam perspectives with real-time frustum plane extraction.
    /// Clean-room implementation referencing FFXI spherical coordinate systems.
    /// </summary>
    public sealed class ViewportCamera
    {
        private CameraMode _mode = CameraMode.ThirdPersonOrbital;
        private Vector3 _position = new(0, 5, -10);
        private Vector3 _target = Vector3.Zero;
        private Vector3 _eyeOffset = new(0, 1.3f, 0); // Average character eye level
        private float _pitch = 15.0f;                 // degrees (-80 to +80)
        private float _yaw = 0.0f;                     // degrees (0 to 360)
        private float _distance = 6.0f;                // yalms (1.5 to 25.0)
        private float _fov = MathF.PI / 3.0f;          // 60 degrees in radians
        private float _nearClip = 0.1f;
        private float _farClip = 5000.0f;
        private float _aspectRatio = 16.0f / 9.0f;

        private Matrix4x4 _viewMatrix = Matrix4x4.Identity;
        private Matrix4x4 _projectionMatrix = Matrix4x4.Identity;
        private Matrix4x4 _viewProjectionMatrix = Matrix4x4.Identity;
        private readonly BoundingFrustum _frustum = new();

        public CameraMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        public Vector3 Position => _position;

        /// <summary>
        /// Zone collision the orbital camera keeps in front of (null = the camera passes through walls). The camera
        /// pulls in to the first wall between the character and its orbit position, as the legacy client's does,
        /// ignoring triangles marked camera-transparent.
        /// </summary>
        public World.Collision.ZoneCollisionMesh? Collision { get; set; }

        /// <summary>Gap kept between the camera and a blocking wall, in yalms.</summary>
        public const float CollisionMargin = 0.3f;

        /// <summary>How fast a pulled-in camera eases back out once the wall is gone, in yalms per second.</summary>
        public const float CollisionReleaseSpeed = 10.0f;

        private float _collisionDistance = float.MaxValue;
        public Vector3 Target => _target;

        /// <summary>
        /// A point the third-person view turns toward, in the same space as the position passed to
        /// <see cref="Update(Vector3, float, float, float, float, float)"/> (the lock-on target, #137). Only its bearing
        /// from the camera is used: the view yaw swings to face it while the pitch stays the orbit pitch, and the camera
        /// keeps orbiting the character. <see cref="AimBlend"/> says how far.
        /// </summary>
        public Vector3? AimPoint { get; set; }

        /// <summary>0 = look at the character as always, 1 = view yaw fully toward <see cref="AimPoint"/>.</summary>
        public float AimBlend { get; set; }

        public Vector3 EyeOffset
        {
            get => _eyeOffset;
            set => _eyeOffset = value;
        }

        public float Pitch
        {
            get => _pitch;
            set
            {
                float minPitch = _mode == CameraMode.ThirdPersonOrbital ? -15.0f : -80.0f;
                _pitch = Math.Clamp(value, minPitch, 80.0f);
            }
        }

        public float Yaw
        {
            get => _yaw;
            set => _yaw = NormalizeDegrees(value);
        }

        public float Distance
        {
            get => _distance;
            set => _distance = Math.Clamp(value, 0.5f, 50.0f);
        }

        public float FieldOfView
        {
            get => _fov;
            set => _fov = Math.Clamp(value, 0.1f, MathF.PI - 0.1f);
        }

        public float NearClip
        {
            get => _nearClip;
            set => _nearClip = Math.Max(0.01f, value);
        }

        public float FarClip
        {
            get => _farClip;
            set => _farClip = Math.Max(_nearClip + 1.0f, value);
        }

        public float AspectRatio
        {
            get => _aspectRatio;
            set => _aspectRatio = Math.Max(0.1f, value);
        }

        public Matrix4x4 ViewMatrix => _viewMatrix;
        public Matrix4x4 ProjectionMatrix => _projectionMatrix;
        public Matrix4x4 ViewProjectionMatrix => _viewProjectionMatrix;
        public BoundingFrustum Frustum => _frustum;

        public Vector3 Forward
        {
            get
            {
                var dir = _target - _position;
                return dir.LengthSquared() > 0.0001f ? Vector3.Normalize(dir) : -Vector3.UnitZ;
            }
        }

        public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));
        public Vector3 Up => Vector3.Normalize(Vector3.Cross(Right, Forward));

        public ViewportCamera()
        {
            UpdateMatrices();
        }

        /// <summary>
        /// Rate (per second) at which the orbital camera's follow height closes the gap to the character's height, so
        /// steps and slopes raise and lower the view smoothly instead of jolting it. About 95% of a step is absorbed
        /// in half a second.
        /// </summary>
        public const float FollowHeightRate = 6.0f;

        /// <summary>
        /// Height changes larger than this (yalms) are teleports or zone-ins, which the camera follows immediately.
        /// </summary>
        public const float FollowHeightSnapDistance = 8.0f;

        private float _followHeight;
        private bool _hasFollowHeight;

        /// <summary>
        /// Updates the camera from the target player position and spherical angles.
        /// </summary>
        public void Update(Vector3 targetPosition, float pitch, float yaw, float distance, float aspectRatio)
            => Update(targetPosition, pitch, yaw, distance, aspectRatio, deltaSeconds: 0.0f);

        /// <summary>
        /// Updates the camera from the target player position and spherical angles, easing the orbital camera's
        /// follow height toward the character's height over <paramref name="deltaSeconds"/> (0 snaps to it).
        /// </summary>
        public void Update(Vector3 targetPosition, float pitch, float yaw, float distance, float aspectRatio, float deltaSeconds)
        {
            float followHeight = EaseFollowHeight(targetPosition.Y, deltaSeconds);

            _pitch = Math.Clamp(pitch, -80.0f, 80.0f);
            _yaw = NormalizeDegrees(yaw);
            _distance = Math.Clamp(distance, 0.5f, 50.0f);
            _aspectRatio = Math.Max(0.1f, aspectRatio);

            float pitchRad = _pitch * (MathF.PI / 180.0f);
            float yawRad = _yaw * (MathF.PI / 180.0f);

            // Forward vector in standard coordinate space
            float cosP = MathF.Cos(pitchRad);
            float sinP = MathF.Sin(pitchRad);
            float cosY = MathF.Cos(yawRad);
            float sinY = MathF.Sin(yawRad);

            // Yaw is expressed in the wire heading convention shared with WorldEntity.Direction
            // (0=East/+X, 90=South/-Z; increasing yaw turns the view right), whose world forward is (cos, -sin) on (X, Z).
            // The renderer displays entities at a mirrored X coordinate (see EntityRenderer/VeldridViewportControl:
            // pos = (-x, -y, z)), so the camera's forward vector is mirrored the same way (negate X) to stay
            // aimed at wherever the mirrored player mesh actually is.
            var forwardDir = new Vector3(-cosY * cosP, sinP, -sinY * cosP);

            switch (_mode)
            {
                case CameraMode.ThirdPersonOrbital:
                    // Orbital pitch floor: retail FFXI limits downward pitch to prevent swinging below feet
                    _pitch = Math.Clamp(pitch, -15.0f, 80.0f);
                    pitchRad = _pitch * (MathF.PI / 180.0f);
                    cosP = MathF.Cos(pitchRad);
                    sinP = MathF.Sin(pitchRad);

                    _target = new Vector3(targetPosition.X, followHeight, targetPosition.Z) + _eyeOffset;
                    float camY = _target.Y + (sinP * _distance);

                    // Ground floor safeguard: when orbiting an avatar (EyeOffset.Y > 0),
                    // ensure camera elevation never drops below the character's ground plane (+ small margin)
                    if (_eyeOffset.Y > 0.0f)
                    {
                        float groundFloor = targetPosition.Y + 0.25f;
                        if (camY < groundFloor)
                        {
                            camY = groundFloor;
                        }
                    }

                    _position = new Vector3(
                        _target.X - (-cosY * cosP * _distance),
                        camY,
                        _target.Z - (-sinY * cosP * _distance)
                    );
                    _position = KeepInFrontOfWalls(_target, _position, deltaSeconds);
                    if (AimPoint is { } aim && AimBlend > 0.0f)
                    {
                        // Turn the view toward the aim point's bearing, keeping the orbit pitch: retail's lock-on view
                        // faces the target while the character moves about the screen (#137).
                        float ax = aim.X - _position.X;
                        float az = aim.Z - _position.Z;
                        float len = MathF.Sqrt((ax * ax) + (az * az));
                        if (len > 1e-3f)
                        {
                            var aimed = new Vector3(ax / len * cosP, -sinP, az / len * cosP);
                            var orbit = _target - _position;
                            float orbitLen = orbit.Length();
                            if (orbitLen > 1e-4f)
                            {
                                var current = orbit / orbitLen;
                                var blended = Vector3.Normalize(Vector3.Lerp(current, aimed, Math.Clamp(AimBlend, 0.0f, 1.0f)));
                                _target = _position + (blended * orbitLen);
                            }
                        }
                    }
                    break;

                case CameraMode.FirstPerson:
                    _position = targetPosition + _eyeOffset;
                    _target = _position + forwardDir;
                    break;

                case CameraMode.FreeCam:
                    // In FreeCam, position is maintained independently; the view looks along the free camera's
                    // direction (pitch tilts it the way the orbit camera's does, see FreeCamForward)
                    _target = _position + FreeCamForward(_pitch, _yaw);
                    break;
            }

            UpdateMatrices();
        }

        /// <summary>
        /// Pulls an orbital camera position in along the line from the look-at point until no collision triangle lies
        /// between them. Hits snap the camera in at once; a cleared line lets it ease back out at
        /// <see cref="CollisionReleaseSpeed"/> so it does not jump when the wall leaves view.
        /// </summary>
        private Vector3 KeepInFrontOfWalls(Vector3 target, Vector3 desired, float deltaSeconds)
        {
            var offset = desired - target;
            float full = offset.Length();
            var collision = Collision;
            if (collision == null || full < 1e-4f)
            {
                _collisionDistance = float.MaxValue;
                return desired;
            }

            // Collision is in internal space; the camera works in display space (-x, -y, z).
            static Vector3 ToInternal(Vector3 v) => new(-v.X, -v.Y, v.Z);
            var direction = offset / full;
            float allowed = full;
            if (collision.TryRaycast(ToInternal(target), ToInternal(target + direction * (full + CollisionMargin)), skipCameraTransparent: true, out float fraction))
            {
                allowed = MathF.Max(0.0f, fraction * (full + CollisionMargin) - CollisionMargin);
            }

            if (allowed < _collisionDistance || deltaSeconds <= 0.0f) _collisionDistance = allowed;
            else _collisionDistance = MathF.Min(allowed, _collisionDistance + CollisionReleaseSpeed * deltaSeconds);

            return target + direction * MathF.Min(full, _collisionDistance);
        }

        /// <summary>
        /// Moves the camera in FreeCam mode using 6-DOF translation deltas.
        /// </summary>
        public void MoveFreeCam(Vector3 translationDelta, float pitchDelta, float yawDelta)
        {
            if (_mode != CameraMode.FreeCam) return;

            _pitch = Math.Clamp(_pitch + pitchDelta, -80.0f, 80.0f);
            _yaw = NormalizeDegrees(_yaw + yawDelta);

            var forward = FreeCamForward(_pitch, _yaw);
            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            var up = Vector3.Normalize(Vector3.Cross(right, forward));

            _position += (right * translationDelta.X) + (up * translationDelta.Y) + (forward * translationDelta.Z);
            _target = _position + forward;

            UpdateMatrices();
        }

        /// <summary>
        /// The free camera's view direction (display space) for a pitch and yaw in degrees. A positive pitch looks down,
        /// as the orbit camera does from above its target, so switching to the free camera keeps the view where it was
        /// and the pitch controls tilt it the same way. (It had looked up, the mirror of the orbit view.)
        /// </summary>
        public static Vector3 FreeCamForward(float pitch, float yaw)
        {
            float pitchRad = pitch * (MathF.PI / 180.0f);
            float yawRad = yaw * (MathF.PI / 180.0f);
            float cosP = MathF.Cos(pitchRad);
            return new Vector3(-MathF.Cos(yawRad) * cosP, -MathF.Sin(pitchRad), -MathF.Sin(yawRad) * cosP);
        }

        /// <summary>
        /// Places the free camera (display space) at <paramref name="position"/>, looking along
        /// <see cref="FreeCamForward"/> of <paramref name="pitch"/> and <paramref name="yaw"/>. The viewport draws the
        /// free camera with this, moving its own camera by the locomotion controller's free camera movement.
        /// </summary>
        public void SetFreeCamPose(Vector3 position, float pitch, float yaw, float aspectRatio)
        {
            _position = position;
            _pitch = Math.Clamp(pitch, -80.0f, 80.0f);
            _yaw = NormalizeDegrees(yaw);
            _aspectRatio = Math.Max(0.1f, aspectRatio);
            _target = _position + FreeCamForward(_pitch, _yaw);
            UpdateMatrices();
        }

        /// <summary>
        /// Directly positions the camera and look-at target (useful for scripted events, cutscenes, and tests).
        /// </summary>
        public void SetLookAt(Vector3 eye, Vector3 target, Vector3? up = null)
        {
            _position = eye;
            _target = target;
            var upVec = up ?? Vector3.UnitY;

            _viewMatrix = Matrix4x4.CreateLookAt(_position, _target, upVec);
            _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(_fov, _aspectRatio, _nearClip, _farClip);
            _viewProjectionMatrix = Matrix4x4.Multiply(_viewMatrix, _projectionMatrix);
            _frustum.Update(_viewProjectionMatrix);
        }

        /// <summary>
        /// Shows an event's cutscene camera (display space): eye, look-at point, vertical field of view (radians) and roll
        /// around the view direction (radians). The camera's own field of view and orbit are left as they were, so the
        /// player's view comes back unchanged when the event lets go.
        /// </summary>
        public void SetEventView(Vector3 eye, Vector3 target, float fieldOfView, float roll, float aspectRatio)
        {
            var forward = target - eye;
            if (forward.LengthSquared() < 1e-8f) return;
            _aspectRatio = Math.Max(0.1f, aspectRatio);
            _position = eye;
            _target = target;
            var up = Vector3.UnitY;
            if (roll != 0f && float.IsFinite(roll)) up = Vector3.Transform(up, Quaternion.CreateFromAxisAngle(Vector3.Normalize(forward), roll));
            float fov = Math.Clamp(float.IsFinite(fieldOfView) ? fieldOfView : _fov, 0.1f, MathF.PI - 0.1f);
            _viewMatrix = Matrix4x4.CreateLookAt(_position, _target, up);
            _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(fov, _aspectRatio, _nearClip, _farClip);
            _viewProjectionMatrix = Matrix4x4.Multiply(_viewMatrix, _projectionMatrix);
            _frustum.Update(_viewProjectionMatrix);
        }

        private float EaseFollowHeight(float height, float deltaSeconds)
        {
            if (!_hasFollowHeight || deltaSeconds <= 0.0f || MathF.Abs(height - _followHeight) > FollowHeightSnapDistance)
            {
                _followHeight = height;
                _hasFollowHeight = true;
                return height;
            }
            _followHeight += (height - _followHeight) * (1.0f - MathF.Exp(-FollowHeightRate * deltaSeconds));
            return _followHeight;
        }

        private void UpdateMatrices()
        {
            _viewMatrix = Matrix4x4.CreateLookAt(_position, _target, Vector3.UnitY);
            _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(_fov, _aspectRatio, _nearClip, _farClip);
            _viewProjectionMatrix = Matrix4x4.Multiply(_viewMatrix, _projectionMatrix);
            _frustum.Update(_viewProjectionMatrix);
        }

        private static float NormalizeDegrees(float deg)
        {
            deg %= 360.0f;
            if (deg < 0.0f) deg += 360.0f;
            return deg;
        }
    }
}
