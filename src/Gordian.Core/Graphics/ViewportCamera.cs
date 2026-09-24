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
        public Vector3 Target => _target;

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
        /// Updates the camera from the target player position and spherical angles.
        /// </summary>
        public void Update(Vector3 targetPosition, float pitch, float yaw, float distance, float aspectRatio)
        {
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
            // (0=+X, 90=-Z; increasing yaw turns the view right), whose world forward is (cos, -sin) on (X, Z).
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

                    _target = targetPosition + _eyeOffset;
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
                    break;

                case CameraMode.FirstPerson:
                    _position = targetPosition + _eyeOffset;
                    _target = _position + forwardDir;
                    break;

                case CameraMode.FreeCam:
                    // In FreeCam, position is maintained independently; target is derived from forwardDir
                    _target = _position + forwardDir;
                    break;
            }

            UpdateMatrices();
        }

        /// <summary>
        /// Moves the camera in FreeCam mode using 6-DOF translation deltas.
        /// </summary>
        public void MoveFreeCam(Vector3 translationDelta, float pitchDelta, float yawDelta)
        {
            if (_mode != CameraMode.FreeCam) return;

            _pitch = Math.Clamp(_pitch + pitchDelta, -80.0f, 80.0f);
            _yaw = NormalizeDegrees(_yaw + yawDelta);

            float pitchRad = _pitch * (MathF.PI / 180.0f);
            float yawRad = _yaw * (MathF.PI / 180.0f);

            float cosP = MathF.Cos(pitchRad);
            float sinP = MathF.Sin(pitchRad);
            float cosY = MathF.Cos(yawRad);
            float sinY = MathF.Sin(yawRad);

            var forward = new Vector3(-cosY * cosP, sinP, -sinY * cosP);
            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            var up = Vector3.Normalize(Vector3.Cross(right, forward));

            _position += (right * translationDelta.X) + (up * translationDelta.Y) + (forward * translationDelta.Z);
            _target = _position + forward;

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
