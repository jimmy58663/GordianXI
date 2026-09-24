// src/Gordian.Core/Resources/Graphics/ParticleGeneratorDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Billboarding alignment mode for particles.
    /// </summary>
    public enum ParticleBillBoardType
    {
        None,
        XYZ,
        XZ,
        Camera,
        Movement,
        MovementHorizontal
    }

    /// <summary>
    /// Type of resource linked to by a particle generator.
    /// Derived from xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xim LinkedDataType).
    /// </summary>
    public enum ParticleLinkedDataType : byte
    {
        Unknown = 0x00,
        Actor = 0x01,
        StaticMesh = 0x0B,
        SpriteSheet = 0x0E,
        WeightedMesh = 0x1D,
        Distortion = 0x22,
        RingMesh = 0x24,
        LensFlare = 0x39,
        Audio = 0x3D,
        PointLight = 0x47,
        Null = 0x57
    }

    /// <summary>
    /// Particle blend function selected by Section 2 Opcode 0x1E.
    /// Mapping referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
    /// ui/js/particle/ops/initializers.js BlendFuncInitializer, after xim).
    /// </summary>
    public enum ParticleBlendFunc
    {
        /// <summary>(SRC_ALPHA, ONE) additive; the default when a generator declares no blend opcode.</summary>
        SrcOneAdd,
        /// <summary>(SRC_ALPHA, ONE_MINUS_SRC_ALPHA) alpha blending.</summary>
        SrcInvSrcAdd,
        /// <summary>(SRC_ALPHA, ONE) reverse subtract: darkens the destination.</summary>
        SrcOneRevSub,
        /// <summary>(ZERO, ONE_MINUS_SRC_ALPHA): darkens the destination by source alpha.</summary>
        ZeroInvSrcAdd,
        /// <summary>(ONE, ZERO) opaque.</summary>
        OneZero
    }

    /// <summary>
    /// One raw Section 2 (initializer) or Section 3 (updater) opcode: its code, the particle allocation slot it reads or
    /// writes, and its argument dwords after the opcode header. Interpreted by the particle runtime.
    /// </summary>
    public sealed record ParticleOpcode(byte OpCode, ushort Allocation, uint[] Args)
    {
        public float Float(int index) => index < Args.Length ? BitConverter.UInt32BitsToSingle(Args[index]) : 0f;

        public Vector3 Vector(int index) => new(Float(index), Float(index + 1), Float(index + 2));
    }

    /// <summary>
    /// A Section 2 keyframe-link initializer: the Section 0x19 curve bound to an allocation slot, sampled
    /// <see cref="Cycles"/> times over a particle's life by progress-value updaters.
    /// </summary>
    public sealed record ParticleKeyFrameLink(string CurveId, int Cycles);

    /// <summary>
    /// Standard particle initialization configuration (Section 2 Opcode 0x01).
    /// </summary>
    public sealed class StandardParticleSetup
    {
        public ParticleBillBoardType BillBoardType { get; set; } = ParticleBillBoardType.None;
        public bool ScaleBeforeRotate { get; set; }
        public bool FollowCamera { get; set; }
        public bool LocalPositionInCameraSpace { get; set; }
        public bool DepthMask { get; set; }
        public bool LightingEnabled { get; set; }
        public bool CameraSpaceBillboard { get; set; }
        public bool FollowGenerator { get; set; } = true;
        public bool FogEnabled { get; set; } = true;
        public bool CameraAttachedBasePosition { get; set; }
        public bool LowPriorityDraw { get; set; }

        public string LinkedDataId { get; set; } = string.Empty;
        public ParticleLinkedDataType LinkedDataType { get; set; } = ParticleLinkedDataType.Unknown;
        public Vector3 BasePosition { get; set; } = Vector3.Zero;
        public ushort MaxLifeSpan { get; set; }
        public ushort LifeSpanVariance { get; set; }
    }

    /// <summary>
    /// Decoded definition of an FFXI Section 0x05 Particle Generator.
    /// Protocol specification referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xim ParticleGeneratorParser).
    /// </summary>
    public sealed class ParticleGeneratorDefinition
    {
        public string DatId { get; set; } = string.Empty;

        // Attachment configuration (+0x00)
        public ParticleAttachType AttachType { get; set; } = ParticleAttachType.None;
        public int AttachedJoint0 { get; set; }
        public int AttachedJoint1 { get; set; }
        public bool AttachSourceOriented { get; set; }

        // Scale parameters (+0x10)
        public float ScalePositionAmount { get; set; }
        public float ScaleSizeAmount { get; set; }

        // Environment & unknown identifiers (+0x50)
        public uint UnkId { get; set; }
        public string EnvironmentId { get; set; } = string.Empty;

        // Emission timing & flags (+0x64)
        public ushort EmissionVariance { get; set; }
        public ushort FramesPerEmission { get; set; } = 1;
        public byte ParticlesPerEmission { get; set; }
        public bool ContinuousSingleton { get; set; }
        public bool AutoRun { get; set; }
        public bool Batched { get; set; }

        // Initializer config (Opcode 0x01)
        public StandardParticleSetup? Setup { get; set; }

        /// <summary>
        /// Initial scale vector for the particle or mesh (Section 2 Opcode 0x0F).
        /// </summary>
        public Vector3 Scale { get; set; } = Vector3.One;

        // Color & Material parameters
        public Vector4 BaseColor { get; set; } = Vector4.One;

        // UV drift velocity (from Section 3 Opcodes 0x27 / 0x28)
        public Vector2 UVScrollVelocity { get; set; } = Vector2.Zero;

        // Draw distance (from Section 3 Opcode 0x2E or 0x0A)
        public float MaxDrawDistance { get; set; }

        /// <summary>
        /// Distance fade from Section 3 Opcode 0x2E (DrawDistanceUpdater): alpha is 1 within <see cref="FadeNear"/>
        /// of the camera, 0 beyond <see cref="FadeFar"/>, linear between. Both zero when the generator has no fade.
        /// Opcode layout referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particle/ops/updaters.js DrawDistanceUpdater, after xim).
        /// </summary>
        public float FadeNear { get; set; }

        /// <inheritdoc cref="FadeNear"/>
        public float FadeFar { get; set; }

        // Blend state from Section 2 Opcode 0x1E (default Src_One_Add / additive)
        public ParticleBlendFunc BlendFunc { get; set; } = ParticleBlendFunc.SrcOneAdd;
        public byte? AlphaOverride { get; set; }
        public bool IgnoreTextureAlpha { get; set; }

        /// <summary>
        /// Initial particle rotation in radians, raw DAT axes (Section 2 Opcode 0x09).
        /// </summary>
        public Vector3 Rotation { get; set; } = Vector3.Zero;

        /// <summary>
        /// Section 0x19 keyframe DatId whose value, sampled over the 24-hour Vana'diel clock, multiplies
        /// particle alpha (Section 3 Opcode 0x3F, fed by a Section 2 keyframe-link initializer).
        /// </summary>
        public string? ClockAlphaKeyFrameId { get; set; }

        /// <summary>
        /// Eight RGBA tints indexed by Vana'diel weekday, applied modulate-2x (Section 3 Opcode 0x4E).
        /// </summary>
        public Vector4[]? DayOfWeekColors { get; set; }

        /// <summary>
        /// Twelve RGBA tints indexed by moon phase, applied modulate-2x (Section 3 Opcode 0x4F).
        /// </summary>
        public Vector4[]? MoonPhaseColors { get; set; }

        /// <summary>
        /// True if the drawn sprite-sheet card is selected by the current moon phase (Section 3 Opcode 0x45).
        /// </summary>
        public bool SpriteIndexFromMoonPhase { get; set; }

        /// <summary>
        /// Every Section 2 initializer opcode in authored order, for the particle runtime.
        /// </summary>
        public List<ParticleOpcode> Initializers { get; } = new();

        /// <summary>
        /// Every Section 3 per-particle updater opcode in authored order, for the particle runtime.
        /// </summary>
        public List<ParticleOpcode> Updaters { get; } = new();

        /// <summary>
        /// Section 4 expiration-handler opcodes (e.g. 0x05 repeat: the particle loops instead of dying).
        /// </summary>
        public List<byte> ExpirationHandlers { get; } = new();

        /// <summary>
        /// Section 2 keyframe links by allocation slot.
        /// </summary>
        public Dictionary<ushort, ParticleKeyFrameLink> KeyFrameLinks { get; } = new();

        /// <summary>
        /// Section 1 GeneratorCullUpdater (0x0A): the generator stops emitting while the camera is farther than this
        /// from its base position. Zero when absent.
        /// </summary>
        public float MaxEmitDistance { get; set; }

        /// <summary>
        /// Painter's-order weight (Section 2 Opcode 0x30 param0): larger values draw earlier (farther).
        /// </summary>
        public float ProjectionBias { get; set; }

        /// <summary>
        /// Rotation velocity in radians per 60 Hz frame, raw DAT axes
        /// (Section 2 Opcode 0x0B consumed by Section 3 RotationUpdater Opcode 0x05).
        /// </summary>
        public Vector3 RotationVelocity { get; set; } = Vector3.Zero;

        /// <summary>
        /// Section 0x19 keyframe DatIds that set the particle color's R, G and B over the 24-hour clock
        /// (Section 3 Opcodes 0x3C / 0x3D / 0x3E); null entries leave that channel at the base color.
        /// </summary>
        public string?[] ClockColorKeyFrameIds { get; } = new string?[3];

        /// <summary>
        /// Section 0x19 keyframe DatIds that set the particle's X, Y and Z position offset (raw DAT axes)
        /// over the 24-hour clock (Section 3 Opcodes 0x6B / 0x6C / 0x6D).
        /// </summary>
        public string?[] ClockPositionKeyFrameIds { get; } = new string?[3];

        /// <summary>
        /// Section 0x19 keyframe DatIds that set the particle's X, Y and Z scale over the 24-hour clock
        /// (Section 3 Opcodes 0x40 / 0x41 / 0x42); null entries keep the initial scale.
        /// </summary>
        public string?[] ClockScaleKeyFrameIds { get; } = new string?[3];

        /// <summary>
        /// True if this generator attaches to the Sun or Moon, or links to celestial geometry.
        /// </summary>
        public bool IsCelestial =>
            AttachType == ParticleAttachType.Sun ||
            AttachType == ParticleAttachType.Moon ||
            (Setup != null && ZoneDefDecoder.IsCelestialMesh(Setup.LinkedDataId));

        public override string ToString() =>
            $"ParticleGen [{DatId}] Attach: {AttachType} (Mesh: '{Setup?.LinkedDataId}', UVScroll: {UVScrollVelocity})";
    }

    /// <summary>
    /// Clean-room binary decoder for FFXI DAT Section 0x05 (ParticleGenerator) chunks.
    /// Extracts particle generator definitions, attachment types, billboard setups,
    /// texture UV scroll velocities, and linked DAT meshes.
    /// Protocol specification referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xim ParticleGeneratorParser).
    /// </summary>
    public static class ParticleGeneratorDecoder
    {
        public const int MinimumHeaderSize = 0x80; // 128 bytes (excluding 16-byte chunk header)

        /// <summary>
        /// Decodes a Section 0x05 payload into a <see cref="ParticleGeneratorDefinition"/>.
        /// </summary>
        /// <param name="payload">Section 0x05 payload excluding the 16-byte chunk header.</param>
        /// <param name="datId">4-character chunk identifier string.</param>
        /// <returns>Decoded generator definition, or null if payload is undersized or malformed.</returns>
        public static ParticleGeneratorDefinition? DecodeGenerator(ReadOnlySpan<byte> payload, string datId)
        {
            if (payload.Length < MinimumHeaderSize)
            {
                return null;
            }

            var def = new ParticleGeneratorDefinition
            {
                DatId = datId ?? string.Empty
            };

            // Offset +0x00: Attach flags
            ushort attachFlags = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0x00, 2));
            byte attachTypeFlag = (byte)(attachFlags & 0x0F);
            def.AttachType = Enum.IsDefined(typeof(ParticleAttachType), attachTypeFlag)
                ? (ParticleAttachType)attachTypeFlag
                : ParticleAttachType.None;

            def.AttachedJoint0 = (attachFlags >> 4) & 0x3F;
            def.AttachedJoint1 = (attachFlags >> 10) & 0x3F;

            ushort additionalAttachFlags = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0x02, 2));
            def.AttachSourceOriented = (additionalAttachFlags & 0x0001) != 0;

            // Offset +0x10: Actor scale amounts
            def.ScalePositionAmount = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0x10, 4));
            def.ScaleSizeAmount = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(0x14, 4));

            // Offset +0x50: Environment & Unknown ID
            def.UnkId = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x50, 4));
            def.EnvironmentId = ReadDatId(payload.Slice(0x54, 4));

            // Offset +0x64 (0x74 from section start): Emission timing and flags
            def.EmissionVariance = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0x64, 2));
            def.FramesPerEmission = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(0x66, 2)) + 1);
            def.ParticlesPerEmission = payload[0x68];

            byte genFlags = payload[0x69];
            def.ContinuousSingleton = (genFlags & 0x04) != 0;
            def.AutoRun = (genFlags & 0x10) != 0;

            byte moreFlags = payload[0x6B];
            def.Batched = (moreFlags & 0x20) != 0;

            // Offset +0x70 (0x80 from section start): 4 opcode stream offsets (relative to section start, including 16B header)
            Span<uint> streamOffsets = stackalloc uint[4];
            for (int i = 0; i < 4; i++)
            {
                streamOffsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0x70 + (i * 4), 4));
            }

            // Parse opcode streams (Section 2 keyframe links must be known before Section 3 updaters consume them)
            var keyFrameLinks = new Dictionary<ushort, string>();
            var rotationVelocities = new Dictionary<ushort, Vector3>();
            for (int sec = 0; sec < 4; sec++)
            {
                uint rawOffset = streamOffsets[sec];
                if (rawOffset < 16) continue;

                int payloadOffset = (int)(rawOffset - 16);
                if (payloadOffset < 0 || payloadOffset + 4 > payload.Length) continue;

                ParseOpcodeStream(payload, payloadOffset, sec + 1, def, keyFrameLinks, rotationVelocities);
            }

            return def;
        }

        /// <summary>
        /// Section 2 opcodes whose initializer links a Section 0x19 keyframe curve into a particle allocation slot.
        /// Opcode set referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer,
        /// ui/js/particle/ops/initializers.js KEYFRAME_OPCODES, after xim ParticleGeneratorParser).
        /// </summary>
        private static bool IsKeyFrameLinkOpcode(byte opCode) => opCode switch
        {
            >= 0x21 and <= 0x2F => true,
            >= 0x33 and <= 0x37 => true,
            0x39 => true,
            >= 0x50 and <= 0x52 => true,
            >= 0x59 and <= 0x66 => true,
            0x68 or 0x69 or 0x6C => true,
            >= 0x6D and <= 0x70 => true,
            >= 0x74 and <= 0x78 => true,
            0x7C or 0x7D or 0x80 or 0x81 => true,
            >= 0x83 and <= 0x85 => true,
            >= 0x8B and <= 0x8D => true,
            >= 0x95 and <= 0x97 => true,
            _ => false
        };

        private static void ParseOpcodeStream(
            ReadOnlySpan<byte> payload,
            int startOffset,
            int sectionNumber,
            ParticleGeneratorDefinition def,
            Dictionary<ushort, string> keyFrameLinks,
            Dictionary<ushort, Vector3> rotationVelocities)
        {
            int currentOffset = startOffset;

            while (currentOffset + 4 <= payload.Length)
            {
                uint config = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(currentOffset, 4));
                byte opCode = (byte)(config & 0xFF);
                int opCodeSize = (int)((config >> 8) & 0x1F); // Size in 4-byte dwords
                ushort allocationOffset = (ushort)(config >> 13);

                if (opCode == 0x00 || opCodeSize == 0)
                {
                    break;
                }

                int byteLength = opCodeSize * 4;
                if (currentOffset + byteLength > payload.Length)
                {
                    break;
                }

                var opPayload = payload.Slice(currentOffset, byteLength);

                switch (sectionNumber)
                {
                    case 1: // Generator Updaters
                        ParseSection1Opcode(opCode, opPayload, def);
                        break;
                    case 2: // Initializers
                        def.Initializers.Add(ToParticleOpcode(opCode, allocationOffset, opPayload));
                        if (IsKeyFrameLinkOpcode(opCode) && opPayload.Length >= 12)
                        {
                            string curveId = ReadDatId(opPayload.Slice(8, 4));
                            keyFrameLinks[allocationOffset] = curveId;
                            // Bits 5-15 of the link config are the number of cycles over the particle's life.
                            int cycles = opPayload.Length >= 16
                                ? Math.Max(1, (int)((BinaryPrimitives.ReadUInt32LittleEndian(opPayload.Slice(12, 4)) & 0xFFFF) >> 5))
                                : 1;
                            def.KeyFrameLinks[allocationOffset] = new ParticleKeyFrameLink(curveId, cycles);
                        }
                        if (opCode == 0x0B && opPayload.Length >= 16) // RotationVelocitySetup
                        {
                            rotationVelocities[allocationOffset] = ReadVector3(opPayload.Slice(4));
                        }
                        ParseSection2Opcode(opCode, opPayload, allocationOffset, def);
                        break;
                    case 3: // Particle Updaters
                        def.Updaters.Add(ToParticleOpcode(opCode, allocationOffset, opPayload));
                        ParseSection3Opcode(opCode, opPayload, allocationOffset, def, keyFrameLinks, rotationVelocities);
                        break;
                    case 4: // Expiration handlers
                        def.ExpirationHandlers.Add(opCode);
                        break;
                }

                currentOffset += byteLength;
            }
        }

        private static void ParseSection1Opcode(byte opCode, ReadOnlySpan<byte> opPayload, ParticleGeneratorDefinition def)
        {
            switch (opCode)
            {
                case 0x0A: // GeneratorCullUpdater
                    if (opPayload.Length >= 8)
                    {
                        float maxEmitDistance = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(4, 4));
                        if (maxEmitDistance > 0f) def.MaxDrawDistance = maxEmitDistance;
                        def.MaxEmitDistance = maxEmitDistance;
                    }
                    break;
            }
        }

        private static void ParseSection2Opcode(byte opCode, ReadOnlySpan<byte> opPayload, ushort allocationOffset, ParticleGeneratorDefinition def)
        {
            switch (opCode)
            {
                case 0x01: // StandardParticleSetup
                    if (opPayload.Length >= 40)
                    {
                        var setup = new StandardParticleSetup();
                        ushort billboardFlags = BinaryPrimitives.ReadUInt16LittleEndian(opPayload.Slice(4, 2));
                        setup.ScaleBeforeRotate = (billboardFlags & 0x0002) != 0;
                        setup.FollowCamera = (billboardFlags & 0x0004) != 0;
                        setup.LocalPositionInCameraSpace = (billboardFlags & 0x000C) == 0x000C;
                        setup.DepthMask = (billboardFlags & 0x1000) != 0;

                        if ((billboardFlags & 0x00C0) == 0x00C0) setup.BillBoardType = ParticleBillBoardType.Camera;
                        else if ((billboardFlags & 0x0081) == 0x0081) setup.BillBoardType = ParticleBillBoardType.Movement;
                        else if ((billboardFlags & 0x0080) != 0) setup.BillBoardType = ParticleBillBoardType.MovementHorizontal;
                        else if ((billboardFlags & 0x0040) != 0) setup.BillBoardType = ParticleBillBoardType.Movement;
                        else if ((billboardFlags & 0x4000) != 0) setup.BillBoardType = ParticleBillBoardType.XZ;
                        else if ((billboardFlags & 0x0001) != 0) setup.BillBoardType = ParticleBillBoardType.XYZ;

                        ushort renderStateFlags = BinaryPrimitives.ReadUInt16LittleEndian(opPayload.Slice(6, 2));
                        setup.LightingEnabled = (renderStateFlags & 0x0001) != 0;
                        setup.CameraSpaceBillboard = (renderStateFlags & 0x0002) != 0;
                        setup.FollowGenerator = (renderStateFlags & 0x0080) == 0;
                        setup.FogEnabled = (renderStateFlags & 0x0200) == 0; // bit set disables fog
                        setup.CameraAttachedBasePosition = (renderStateFlags & 0x0400) != 0;
                        setup.LowPriorityDraw = (renderStateFlags & 0x0800) != 0;

                        setup.LinkedDataId = ReadDatId(opPayload.Slice(12, 4));

                        float posX = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(20, 4));
                        float posY = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(24, 4));
                        float posZ = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(28, 4));
                        setup.BasePosition = new Vector3(posX, posY, posZ);

                        byte linkedDataTypeVal = opPayload[33];
                        setup.LinkedDataType = Enum.IsDefined(typeof(ParticleLinkedDataType), linkedDataTypeVal)
                            ? (ParticleLinkedDataType)linkedDataTypeVal
                            : ParticleLinkedDataType.Unknown;

                        setup.MaxLifeSpan = BinaryPrimitives.ReadUInt16LittleEndian(opPayload.Slice(34, 2));
                        setup.LifeSpanVariance = BinaryPrimitives.ReadUInt16LittleEndian(opPayload.Slice(36, 2));

                        def.Setup = setup;
                    }
                    break;

                case 0x09: // RotationInitializer
                    if (opPayload.Length >= 16)
                    {
                        def.Rotation = ReadVector3(opPayload.Slice(4));
                    }
                    break;

                case 0x30: // DepthBiasInitializer (projection bias / painter's-order weight)
                    if (opPayload.Length >= 8)
                    {
                        def.ProjectionBias = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(4, 4));
                    }
                    break;

                case 0x0F: // ScaleInitializer
                    if (opPayload.Length >= 16)
                    {
                        float sx = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(4, 4));
                        float sy = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(8, 4));
                        float sz = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(12, 4));
                        def.Scale = new Vector3(sx, sy, sz);
                    }
                    break;

                case 0x16: // ColorSetup
                    if (opPayload.Length >= 8)
                    {
                        def.BaseColor = new Vector4(
                            opPayload[4] / 255.0f,
                            opPayload[5] / 255.0f,
                            opPayload[6] / 255.0f,
                            opPayload[7] / 255.0f);
                    }
                    break;

                case 0x1E: // BlendFuncInitializer
                    if (opPayload.Length >= 8)
                    {
                        byte p0 = opPayload[4];
                        byte p1 = opPayload[5];
                        def.AlphaOverride = (p0 & 0x20) != 0 ? (byte)Math.Min(255, p1 * 2) : null;
                        byte highNibble = (byte)((p0 >> 4) & 0b1101);
                        byte lowNibble = (byte)(p0 & 0x0F);
                        def.BlendFunc = (highNibble & 0x01) != 0
                            ? ParticleBlendFunc.OneZero
                            : lowNibble switch
                            {
                                0x1 or 0x2 => ParticleBlendFunc.SrcOneRevSub,
                                0x4 => ParticleBlendFunc.SrcInvSrcAdd,
                                0x6 => ParticleBlendFunc.ZeroInvSrcAdd,
                                _ => ParticleBlendFunc.SrcOneAdd
                            };
                        if (def.AlphaOverride != null)
                        {
                            def.IgnoreTextureAlpha = true;
                        }
                    }
                    break;
            }
        }

        private static void ParseSection3Opcode(
            byte opCode,
            ReadOnlySpan<byte> opPayload,
            ushort allocationOffset,
            ParticleGeneratorDefinition def,
            Dictionary<ushort, string> keyFrameLinks,
            Dictionary<ushort, Vector3> rotationVelocities)
        {
            switch (opCode)
            {
                case 0x05: // RotationUpdater: rotation += velocity per frame
                    if (rotationVelocities.TryGetValue(allocationOffset, out var rotationVelocity))
                    {
                        def.RotationVelocity = rotationVelocity;
                    }
                    break;

                case 0x3C: // ClockValueUpdater: color R = keyframe(time of day)
                case 0x3D: // ClockValueUpdater: color G
                case 0x3E: // ClockValueUpdater: color B
                    if (keyFrameLinks.TryGetValue(allocationOffset, out var colorCurveId))
                    {
                        def.ClockColorKeyFrameIds[opCode - 0x3C] = colorCurveId;
                    }
                    break;

                case 0x40: // ClockValueUpdater: scale X = keyframe(time of day)
                case 0x41: // ClockValueUpdater: scale Y
                case 0x42: // ClockValueUpdater: scale Z
                    if (keyFrameLinks.TryGetValue(allocationOffset, out var scaleCurveId))
                    {
                        def.ClockScaleKeyFrameIds[opCode - 0x40] = scaleCurveId;
                    }
                    break;

                case 0x6B: // ClockValueUpdater: position X = keyframe(time of day)
                case 0x6C: // ClockValueUpdater: position Y
                case 0x6D: // ClockValueUpdater: position Z
                    if (keyFrameLinks.TryGetValue(allocationOffset, out var positionCurveId))
                    {
                        def.ClockPositionKeyFrameIds[opCode - 0x6B] = positionCurveId;
                    }
                    break;

                case 0x3F: // ClockValueUpdater: alpha *= keyframe(time of day)
                    if (keyFrameLinks.TryGetValue(allocationOffset, out var clockCurveId))
                    {
                        def.ClockAlphaKeyFrameId = clockCurveId;
                    }
                    break;

                case 0x45: // MoonPhaseSpriteSheetUpdater
                    def.SpriteIndexFromMoonPhase = true;
                    break;

                case 0x4E: // DayOfWeekColorUpdater
                    def.DayOfWeekColors = ReadRgbaColors(opPayload, 8);
                    break;

                case 0x4F: // MoonPhaseColorUpdater
                    def.MoonPhaseColors = ReadRgbaColors(opPayload, 12);
                    break;

                case 0x27: // TextureCoordinateUpdater (Axis X / U)
                    if (opPayload.Length >= 8)
                    {
                        float scrollU = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(4, 4));
                        def.UVScrollVelocity = new Vector2(scrollU, def.UVScrollVelocity.Y);
                    }
                    break;

                case 0x28: // TextureCoordinateUpdater (Axis Y / V)
                    if (opPayload.Length >= 8)
                    {
                        float scrollV = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(4, 4));
                        def.UVScrollVelocity = new Vector2(def.UVScrollVelocity.X, scrollV);
                    }
                    break;

                case 0x2E: // DrawDistanceUpdater
                    if (opPayload.Length >= 8)
                    {
                        float drawDist = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(4, 4));
                        if (drawDist > 0f) def.MaxDrawDistance = drawDist;
                    }
                    if (opPayload.Length >= 12)
                    {
                        def.FadeNear = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(4, 4));
                        def.FadeFar = BinaryPrimitives.ReadSingleLittleEndian(opPayload.Slice(8, 4));
                    }
                    break;
            }
        }

        private static ParticleOpcode ToParticleOpcode(byte opCode, ushort allocationOffset, ReadOnlySpan<byte> opPayload)
        {
            var args = new uint[Math.Max(0, opPayload.Length / 4 - 1)];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = BinaryPrimitives.ReadUInt32LittleEndian(opPayload.Slice(4 + i * 4, 4));
            }
            return new ParticleOpcode(opCode, allocationOffset, args);
        }

        private static Vector3 ReadVector3(ReadOnlySpan<byte> span) => new(
            BinaryPrimitives.ReadSingleLittleEndian(span),
            BinaryPrimitives.ReadSingleLittleEndian(span.Slice(4)),
            BinaryPrimitives.ReadSingleLittleEndian(span.Slice(8)));

        private static Vector4[]? ReadRgbaColors(ReadOnlySpan<byte> opPayload, int count)
        {
            // Opcode header word, one reserved word, then RGBA byte quads.
            if (opPayload.Length < 8 + count * 4) return null;
            var colors = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                var c = opPayload.Slice(8 + i * 4, 4);
                colors[i] = new Vector4(c[0] / 255.0f, c[1] / 255.0f, c[2] / 255.0f, c[3] / 255.0f);
            }
            return colors;
        }

        private static string ReadDatId(ReadOnlySpan<byte> span)
        {
            if (span.Length < 4) return string.Empty;
            int len = 0;
            while (len < 4 && span[len] != 0 && span[len] != 0x20)
            {
                len++;
            }
            return Encoding.ASCII.GetString(span.Slice(0, len)).Trim();
        }
    }
}
