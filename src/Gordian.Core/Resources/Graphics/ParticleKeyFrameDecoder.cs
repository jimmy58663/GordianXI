// src/Gordian.Core/Resources/Graphics/ParticleKeyFrameDecoder.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Gordian.Core.Resources.Graphics
{
    /// <summary>
    /// Represents a single time-value point on a keyframe curve.
    /// Time is normalized between 0.0 and 1.0.
    /// </summary>
    public readonly record struct KeyFrameEntry(float Time, float Value);

    /// <summary>
    /// Represents an evaluated piecewise-linear keyframe curve extracted from FFXI DAT Section 0x19.
    /// Drives dynamic particle properties over time (such as opacity, scale, color, UV drift, or emission rate).
    /// Protocol specification referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xim ParticleKeyFrameValueSection).
    /// </summary>
    public sealed class KeyFrameCurve
    {
        public string DatId { get; }
        public IReadOnlyList<KeyFrameEntry> Entries { get; }

        public KeyFrameCurve(string datId, IReadOnlyList<KeyFrameEntry> entries)
        {
            DatId = datId ?? string.Empty;
            Entries = entries ?? Array.Empty<KeyFrameEntry>();
        }

        /// <summary>
        /// Evaluates the curve at a normalized progress value in the range [0.0, 1.0].
        /// </summary>
        /// <param name="progress">Normalized time progress (0.0 = start, 1.0 = end).</param>
        /// <param name="initialValueOverride">Optional initial value override for the first keyframe (used by progress-value updaters).</param>
        /// <returns>Interpolated value at the given progress point.</returns>
        public float Evaluate(float progress, float? initialValueOverride = null)
        {
            if (Entries.Count == 0) return 0f;
            if (progress >= 1.0f || Entries.Count == 1) return Entries[^1].Value;
            if (progress <= Entries[0].Time)
            {
                return initialValueOverride ?? Entries[0].Value;
            }

            int nextIndex = -1;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Time > progress)
                {
                    nextIndex = i;
                    break;
                }
            }

            if (nextIndex <= 0) return Entries[0].Value;

            int prevIndex = nextIndex - 1;
            var next = Entries[nextIndex];
            var prev = Entries[prevIndex];

            float prevValue = (prevIndex == 0 && initialValueOverride.HasValue)
                ? initialValueOverride.Value
                : prev.Value;

            float span = next.Time - prev.Time;
            float t = span <= 0f ? 0f : (progress - prev.Time) / span;
            return (1.0f - t) * prevValue + t * next.Value;
        }
    }

    /// <summary>
    /// Clean-room binary decoder for FFXI DAT Section 0x19 (ParticleKeyFrameData) chunks.
    /// Extracts piecewise-linear (time, value) curves driving animated particle and weather properties.
    /// Protocol specification referenced from xi-model-viewer (https://github.com/vekien/xi-model-viewer)
    /// and xi-tools (xim ParticleKeyFrameValueSection).
    /// </summary>
    public static class ParticleKeyFrameDecoder
    {
        public const int EntryStride = 8; // sizeof(float) + sizeof(float)

        /// <summary>
        /// Decodes a Section 0x19 payload into a <see cref="KeyFrameCurve"/>.
        /// </summary>
        /// <param name="payload">Section 0x19 payload excluding the 16-byte chunk header.</param>
        /// <param name="datId">4-character chunk identifier string.</param>
        /// <returns>Decoded keyframe curve, or null if payload is empty or malformed.</returns>
        public static KeyFrameCurve? DecodeKeyFrame(ReadOnlySpan<byte> payload, string datId)
        {
            if (payload.Length < EntryStride)
            {
                return null;
            }

            var entries = new List<KeyFrameEntry>();
            int offset = 0;

            while (offset + EntryStride <= payload.Length)
            {
                float time = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(offset, 4));
                float value = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(offset + 4, 4));
                offset += EntryStride;

                entries.Add(new KeyFrameEntry(time, value));

                // Curves terminate upon reaching normalized time 1.0f
                if (time >= 1.0f)
                {
                    break;
                }
            }

            if (entries.Count == 0)
            {
                return null;
            }

            return new KeyFrameCurve(datId, entries);
        }
    }
}
