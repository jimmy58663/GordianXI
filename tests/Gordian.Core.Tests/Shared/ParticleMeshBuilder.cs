// tests/Gordian.Core.Tests/Shared/ParticleMeshBuilder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Gordian.Core.Resources.Graphics;

namespace Gordian.Tests.Shared
{
    /// <summary>Synthetic Section 0x1F particle mesh payloads for the decoder and effect tests. Compiled into both test projects.</summary>
    internal static class ParticleMeshBuilder
    {
        /// <summary>
        /// Builds a Section 0x1F payload with one textured and one untextured single-triangle mesh.
        /// </summary>
        internal static byte[] BuildParticleMeshPayload(string textureName, byte alpha, uint version = 5)
        {
            var bytes = new List<byte>();
            void U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); bytes.AddRange(b); }
            void U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); bytes.AddRange(b); }
            void F32(float v) { var b = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(b, v); bytes.AddRange(b); }
            void Name(string name) { var b = new byte[16]; Encoding.ASCII.GetBytes(name).CopyTo(b, 0); bytes.AddRange(b); }

            U32(version);
            bytes.Add(1); // textured meshes
            bytes.Add(1); // untextured meshes
            U16(2);       // total triangles
            U16(1); U16(1); U16(0); // triangle counts, padded to 3 slots
            if (version == 3) U16(0);

            Name(textureName);
            if (version == 3) { Name(string.Empty); Name(string.Empty); Name(string.Empty); }

            for (int mesh = 0; mesh < 2; mesh++)
            {
                var positions = new[] { new Vector3(1f, 2f, 3f), new Vector3(3f, 2f, 3f), new Vector3(1f, 2f, 5f) };
                foreach (var p in positions)
                {
                    F32(p.X); F32(p.Y); F32(p.Z);
                    F32(0f); F32(-1f); F32(0f);
                    bytes.Add(0x10); bytes.Add(0x20); bytes.Add(0x30); bytes.Add(alpha); // BGRA
                    F32(0.25f); F32(0.75f);
                }
            }

            return bytes.ToArray();
        }
    }
}
