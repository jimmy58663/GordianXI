// tests/Gordian.Core.Tests/Audio/Atrac3DecoderTests.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gordian.Core.Audio;
using Gordian.Core.Audio.Atrac3;
using Xunit;

namespace Gordian.Core.Tests.Audio
{
    /// <summary>
    /// The clean-room ATRAC3 decoder (#38, <c>docs/audio/atrac3.md</c>): its building blocks against their mathematical
    /// definitions, and, when the install is present, the retail files against reference PCM made by FFmpeg (outside the
    /// repository: set <c>GORDIAN_ATRAC3_REF</c> to the harness's <c>out</c> folder, see the spec's section 16).
    /// </summary>
    public class Atrac3DecoderTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public Atrac3DecoderTests(ITestOutputHelper output) => _output = output;

        // ---- Bit reader ----

        [Fact]
        public void BitReader_ReadsMostSignificantBitFirstAcrossBytes()
        {
            byte[] data = [0b1010_0011, 0b1100_0000, 0xFF];
            var bits = new Atrac3BitReader(data);
            Assert.Equal(0b101000, bits.Read(6));
            Assert.Equal(0b11, bits.Read(2));
            Assert.Equal(0b110, bits.Read(3));
            Assert.Equal(-1, ReadSignedAt(data, 16, 3)); // 111 → -1
            Assert.Equal(11, bits.Position);
            Assert.False(bits.Overrun);
        }

        [Fact]
        public void BitReader_SignedAndOverrun()
        {
            byte[] data = [0b0111_1000];
            var bits = new Atrac3BitReader(data);
            Assert.Equal(3, bits.ReadSigned(3)); // 011
            Assert.Equal(-2, bits.ReadSigned(3)); // 110
            bits.Read(2);
            Assert.False(bits.Overrun);
            Assert.Equal(0, bits.Read(1));
            Assert.True(bits.Overrun);
        }

        private static int ReadSignedAt(byte[] data, int skip, int n)
        {
            var bits = new Atrac3BitReader(data);
            bits.Skip(skip);
            return bits.ReadSigned(n);
        }

        // ---- Tables ----

        [Fact]
        public void Vlc_TablesAreCompletePrefixCodesThatDecodeEveryListedCode()
        {
            for (int s = 1; s < 8; s++)
            {
                double kraft = 0;
                foreach (string entry in Atrac3Tables.VlcCodes[s])
                {
                    string code = entry[..entry.IndexOf(':')];
                    kraft += Math.Pow(2, -code.Length);
                    int index = Convert.ToInt32(code, 2) << (8 - code.Length);
                    int packed = Atrac3Tables.Vlc[s][index];
                    Assert.Equal(code.Length, Atrac3Tables.VlcLength(packed));
                    string[] values = entry[(entry.IndexOf(':') + 1)..].Split(',');
                    Assert.Equal(int.Parse(values[0]), Atrac3Tables.VlcValue(packed));
                    if (s == 1)
                    {
                        Assert.Equal(int.Parse(values[1]), Atrac3Tables.VlcPairSecond(packed));
                    }
                }

                Assert.Equal(1.0, kraft, 12);
                Assert.All(Atrac3Tables.Vlc[s], e => Assert.NotEqual(0, Atrac3Tables.VlcLength(e)));
            }

            // Every value of each single-value table appears exactly once (-max..max).
            int[] max = [0, 0, 2, 3, 4, 7, 15, 31];
            for (int s = 2; s < 8; s++)
            {
                var values = Atrac3Tables.VlcCodes[s].Select(e => int.Parse(e[(e.IndexOf(':') + 1)..])).OrderBy(v => v).ToArray();
                Assert.Equal(Enumerable.Range(-max[s], 2 * max[s] + 1), values);
            }
        }

        [Fact]
        public void Tables_MatchTheSpecCheckValues()
        {
            Assert.Equal(1f, Atrac3Tables.ScaleFactor[15]);
            Assert.Equal(1f / 32, Atrac3Tables.ScaleFactor[0]);
            Assert.Equal(65536f, Atrac3Tables.ScaleFactor[63]);
            float[] w = Atrac3Tables.Window;
            Assert.Equal(9.413e-06, w[0], 8);
            Assert.Equal(8.4723e-05, w[1], 8);
            Assert.Equal(0.198977423, w[64], 6);
            Assert.Equal(0.993826699, w[127], 6);
            Assert.Equal(1.006098006, w[128], 6);
            Assert.Equal(1.108092771, w[200], 6);
            Assert.Equal(1.000009412, w[255], 6);
            Assert.Equal(w[10], w[501]);
            Assert.Equal(1.99989, Atrac3Tables.QmfFilter.Sum(x => (double)x), 4);
            Assert.Equal(16f, Atrac3Tables.GainLevel[0]);
            Assert.Equal(1f, Atrac3Tables.GainLevel[4]);
            Assert.Equal(1024, Atrac3Tables.SubbandStart[32]);
        }

        // ---- IMDCT ----

        [Fact]
        public void Imdct_MatchesTheDirectSum()
        {
            var random = new Random(38);
            var input = new float[256];
            var fast = new float[512];
            var direct = new float[512];
            var imdct = new Atrac3Imdct();
            for (int trial = 0; trial < 6; trial++)
            {
                for (int k = 0; k < 256; k++)
                {
                    input[k] = trial == 0 ? (k == 5 ? 1f : 0f) : (float)(random.NextDouble() * 2 - 1) * 1000f;
                }

                imdct.Transform(input, fast);
                Atrac3Imdct.Direct(input, direct);
                double peak = direct.Max(v => Math.Abs((double)v));
                for (int n = 0; n < 512; n++)
                {
                    Assert.True(Math.Abs(fast[n] - direct[n]) <= 2e-5 * peak, $"trial {trial} n {n}: {fast[n]} vs {direct[n]}");
                }
            }
        }

        // ---- QMF and a whole channel ----

        [Fact]
        public void Channel_SilentUnitDecodesToSilenceAndBadIdIsRejected()
        {
            var unit = new byte[192];
            unit[0] = 0xA0; // 101000 00: id, bandsCoded 0, then all-zero fields
            var channel = new Atrac3Channel();
            var output = new float[1024];
            Assert.True(channel.Decode(unit, output));
            Assert.All(output, v => Assert.Equal(0f, v));

            unit[0] = 0xA4; // id 101001
            Array.Fill(output, 5f);
            Assert.False(channel.Decode(unit, output));
            Assert.All(output, v => Assert.Equal(0f, v));
        }

        [Fact]
        public void Channel_MatchesTheSpecFormulasForOneSubband()
        {
            // One unit: bandsCoded 0, no gain points, no tonal groups, subbandCount 1, CLC, selector 7, scale 15,
            // eight 6-bit values. Followed by silent units, the decoder's output must equal the spec's arithmetic
            // (IMDCT by its sum, window, overlap-add, the three QMF stages) carried out here independently in double.
            int[] values = [5, -3, 31, -31, 0, 7, 1, -9];
            var bits = new BitWriter();
            bits.Write(0x28, 6);
            bits.Write(0, 2);
            bits.Write(0, 3); // band 0: no gain points
            bits.Write(0, 5); // no tonal groups
            bits.Write(0, 5); // 1 subband
            bits.Write(1, 1); // CLC
            bits.Write(7, 3); // selector 7
            bits.Write(15, 6); // scale 1
            foreach (int v in values)
            {
                bits.Write(v & 63, 6);
            }

            byte[] first = bits.ToArray(192);
            var silent = new byte[192];
            silent[0] = 0xA0;

            var channel = new Atrac3Channel();
            var output = new float[3 * 1024];
            Assert.True(channel.Decode(first, output.AsSpan(0, 1024)));
            Assert.True(channel.Decode(silent, output.AsSpan(1024, 1024)));
            Assert.True(channel.Decode(silent, output.AsSpan(2048, 1024)));

            double[] expected = ReferenceThreeFrames(values.Select(v => v / 31.5).ToArray());
            double peak = expected.Max(Math.Abs);
            Assert.True(peak > 1e-3);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(Math.Abs(output[i] - expected[i]) < 1e-5 * peak + 1e-6, $"sample {i}: {output[i]} vs {expected[i]}");
            }
        }

        /// <summary>The spec's sections 8-10 written out naively for a spectrum with only lines 0-7 of band 0 set in frame 0.</summary>
        private static double[] ReferenceThreeFrames(double[] lines)
        {
            double[] window = Enumerable.Range(0, 512).Select(n => (double)Atrac3Tables.Window[n]).ToArray();
            double[] h = Atrac3Tables.QmfFilter.Select(v => (double)v).ToArray();
            var bands = new double[3][][];
            var overlap = new double[4][];
            for (int b = 0; b < 4; b++) overlap[b] = new double[256];
            for (int f = 0; f < 3; f++)
            {
                bands[f] = new double[4][];
                for (int b = 0; b < 4; b++)
                {
                    var x = new double[256];
                    if (f == 0 && b == 0) Array.Copy(lines, x, lines.Length);
                    var y = new double[512];
                    for (int n = 0; n < 512; n++)
                    {
                        double s = 0;
                        for (int k = 0; k < 256; k++) s += x[k] * Math.Cos(Math.PI / 256 * (n + 0.5 + 128) * (k + 0.5));
                        y[n] = -s * window[n];
                    }

                    bands[f][b] = new double[256];
                    for (int n = 0; n < 256; n++) bands[f][b][n] = y[n] + overlap[b][n];
                    overlap[b] = y[256..];
                }
            }

            // The QMF as one long signal per stage (the delay lines just carry it across frames).
            double[] Concat(int b) => bands.SelectMany(fr => fr[b]).ToArray();
            double[] Stage(double[] lo, double[] hi)
            {
                var u = new double[46 + 2 * lo.Length];
                for (int i = 0; i < lo.Length; i++) { u[46 + 2 * i] = lo[i] + hi[i]; u[47 + 2 * i] = lo[i] - hi[i]; }
                var o = new double[2 * lo.Length];
                for (int j = 0; j < lo.Length; j++)
                {
                    double odd = 0, even = 0;
                    for (int m = 0; m < 48; m++) { if ((m & 1) == 1) odd += u[2 * j + m] * h[m]; else even += u[2 * j + m] * h[m]; }
                    o[2 * j] = odd;
                    o[2 * j + 1] = even;
                }

                return o;
            }

            // Stage outputs per frame interleave in time, so the concatenated stages equal frame-by-frame stages.
            double[] a = Stage(Concat(0), Concat(1));
            double[] c = Stage(Concat(3), Concat(2));
            return Stage(a, c);
        }

        private sealed class BitWriter
        {
            private readonly List<bool> _bits = [];

            public void Write(int value, int count)
            {
                for (int i = count - 1; i >= 0; i--) _bits.Add(((value >> i) & 1) == 1);
            }

            public byte[] ToArray(int length)
            {
                var bytes = new byte[length];
                for (int i = 0; i < _bits.Count; i++) if (_bits[i]) bytes[i >> 3] |= (byte)(0x80 >> (i & 7));
                return bytes;
            }
        }

        // ---- Header and stream on a synthetic file ----

        /// <summary>A synthetic ATRAC3 file: frame 0 is the key frame (all zero plaintext after the mask), then silent units.</summary>
        private static byte[] SyntheticAtrac3(bool music, int frames, int loopStart, int channels)
        {
            var file = new byte[FfxiSoundHeader.DataOffset + frames * 192 * channels];
            int body;
            if (music)
            {
                "BGMStream"u8.CopyTo(file);
                BitConverter.GetBytes(3).CopyTo(file, 0x0C);
                BitConverter.GetBytes(file.Length).CopyTo(file, 0x10);
                body = 0x14;
            }
            else
            {
                "SeWave"u8.CopyTo(file);
                BitConverter.GetBytes(file.Length).CopyTo(file, 0x08);
                BitConverter.GetBytes(3).CopyTo(file, 0x0C);
                body = 0x10;
            }

            BitConverter.GetBytes(99).CopyTo(file, body);
            BitConverter.GetBytes(frames * 1024).CopyTo(file, body + 4);
            BitConverter.GetBytes(loopStart).CopyTo(file, body + 8);
            BitConverter.GetBytes(24000).CopyTo(file, body + 0x0C);
            BitConverter.GetBytes(24000).CopyTo(file, body + 0x10);
            BitConverter.GetBytes(0x30).CopyTo(file, body + 0x14);
            file[body + 0x1A] = (byte)channels;
            file[body + 0x1B] = music ? (byte)0 : (byte)16;
            // Key = first block with the mask: storing zeros there makes every later all-zero block decode to A0 02 4E 9F 00...
            return file;
        }

        [Fact]
        public void Header_Atrac3FieldsAreSampleCounts()
        {
            Assert.True(FfxiSoundHeader.TryParse(SyntheticAtrac3(true, 10, 2500, 2), out FfxiSoundHeader m));
            Assert.Equal(10, m.SampleBlocks);
            Assert.Equal(10240, m.TotalFrames);
            Assert.Equal(2500, m.LoopStartFrame);
            Assert.True(m.IsLooped);
            Assert.Equal(48000, m.SampleRate);

            Assert.True(FfxiSoundHeader.TryParse(SyntheticAtrac3(false, 10, 2500, 1), out FfxiSoundHeader e));
            Assert.Equal(2500 - 1024, e.LoopStartFrame);

            Assert.True(FfxiSoundHeader.TryParse(SyntheticAtrac3(true, 10, -1, 2), out FfxiSoundHeader once));
            Assert.False(once.IsLooped);
            Assert.Equal(-1, once.LoopStartFrame);
        }

        [Fact]
        public void Stream_SyntheticFileDecodesSilenceAndLoops()
        {
            byte[] file = SyntheticAtrac3(true, 4, 1500, 2);
            // Not through FfxiSoundDecoder: another test class swaps its static decoder while tests run in parallel.
            Assert.True(FfxiSoundHeader.TryParse(file, out FfxiSoundHeader header));
            IPcmSource source = new Atrac3Decoder().Open(file, header, header.IsLooped)!;
            Assert.IsType<Atrac3Stream>(source);
            Assert.True(source.IsLooped);
            var buffer = new short[2 * 4096 * 3];
            Assert.Equal(buffer.Length, source.Read(buffer));
            Assert.All(buffer, s => Assert.Equal(0, s));
            Assert.Equal(0, ((Atrac3Stream)source).MalformedFrames);

            Assert.True(FfxiSoundHeader.TryParse(SyntheticAtrac3(false, 3, 2000, 1), out FfxiSoundHeader effect));
            Assert.Equal(2000 - 1024, effect.LoopStartFrame);
        }

        [Fact]
        public void ToInt16_RoundsHalfToEvenAndClamps()
        {
            Assert.Equal(2, Atrac3Stream.ToInt16(2.5f));
            Assert.Equal(4, Atrac3Stream.ToInt16(3.5f));
            Assert.Equal(-2, Atrac3Stream.ToInt16(-2.5f));
            Assert.Equal(short.MaxValue, Atrac3Stream.ToInt16(40000f));
            Assert.Equal(short.MinValue, Atrac3Stream.ToInt16(-40000f));
        }

        // ---- Retail data against FFmpeg reference output ----

        private static string? ReferenceDirectory()
        {
            string? dir = Environment.GetEnvironmentVariable("GORDIAN_ATRAC3_REF");
            return dir is not null && Directory.Exists(dir) && Directory.Exists(GameDirectory) ? dir : null;
        }

        [Theory]
        [InlineData("music040", true, 40)]
        [InlineData("music041", true, 41)]
        [InlineData("music069", true, 69)]
        [InlineData("music900", true, 900)]
        [InlineData("se036124", false, 36124)]
        [InlineData("se041035", false, 41035)]
        [InlineData("se041044", false, 41044)]
        public void Retail_MatchesReferencePcm(string name, bool music, int id)
        {
            string? refDir = ReferenceDirectory();
            if (refDir is null)
            {
                return;
            }

            var locator = new FfxiSoundLocator(GameDirectory);
            byte[] file = File.ReadAllBytes((music ? locator.FindMusic(id) : locator.FindEffect(id))!);
            Assert.True(FfxiSoundHeader.TryParse(file, out FfxiSoundHeader header));
            Atrac3Stream stream = Atrac3Stream.Open(file, header, loop: false)!;
            int channels = stream.Channels;
            long total = stream.TotalSamples * channels;

            var decoded = new float[total];
            Assert.Equal(total, stream.ReadFloat(decoded));
            Assert.Equal(0, stream.ReadFloat(new float[16]));
            Assert.Equal(0, stream.MalformedFrames);

            byte[] f32 = File.ReadAllBytes(Path.Combine(refDir, name + ".f32"));
            Assert.Equal(total * 4, f32.Length);
            ReadOnlySpan<float> reference = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(f32);
            double signal = 0, noise = 0, maxError = 0;
            for (long i = 0; i < total; i++)
            {
                double r = reference[(int)i];
                double d = decoded[i] / 32768.0 - r;
                signal += r * r;
                noise += d * d;
                maxError = Math.Max(maxError, Math.Abs(d));
            }

            double snr = noise == 0 ? double.PositiveInfinity : 10 * Math.Log10(signal / noise);

            short[] s16 = ReadWav(Path.Combine(refDir, name + ".s16.wav"));
            Assert.Equal(total, s16.Length);
            int off = 0, maxLsb = 0;
            for (long i = 0; i < total; i++)
            {
                int d = Math.Abs(Atrac3Stream.ToInt16(decoded[i]) - s16[i]);
                if (d != 0) off++;
                maxLsb = Math.Max(maxLsb, d);
            }

            _output.WriteLine($"{name}: {channels} ch {stream.SampleRate} Hz, {stream.FrameCount} frames, raw {stream.RawFrames}; "
                + $"SNR {snr:F1} dB, max float error {maxError:E2}; s16 off by one {off} ({100.0 * off / total:F3} %), max {maxLsb}");
            Assert.True(snr > 100, $"SNR {snr}");
            Assert.True(maxLsb <= 1, $"max LSB {maxLsb}");

            // Loop: the second pass must replay the first pass from the loop sample (snapshot), and match the reference.
            if (header.IsLooped)
            {
                long loopSample = stream.LoopStartSample;
                Atrac3Stream looped = Atrac3Stream.Open(file, header, loop: true)!;
                long second = (stream.TotalSamples - loopSample) * channels;
                var both = new short[total + second];
                Assert.Equal(both.Length, looped.Read(both));
                for (long i = 0; i < second; i++)
                {
                    short own = Atrac3Stream.ToInt16(decoded[loopSample * channels + i]);
                    Assert.True(own == both[total + i], $"loop sample {i}: {both[total + i]} vs first pass {own}");
                }

                if (music && File.Exists(Path.Combine(refDir, name + ".loop2.s16.wav")))
                {
                    short[] loop2 = ReadWav(Path.Combine(refDir, name + ".loop2.s16.wav"));
                    Assert.Equal(both.Length, loop2.Length);
                    int worst = 0;
                    for (long i = total; i < both.Length; i++) worst = Math.Max(worst, Math.Abs(both[i] - loop2[i]));
                    _output.WriteLine($"{name}: loop restart at {loopSample}, max LSB vs reference loop {worst}");
                    Assert.True(worst <= 1);
                }
                else
                {
                    _output.WriteLine($"{name}: effect loop restart at {loopSample} (loopStart - 1024) replays the first pass");
                }
            }
        }

        [Fact]
        public void Retail_Music178_LoopRestartsImmediatelyAndTheGapIsInTheData()
        {
            // In-game round 2: Aht Urhgan Whitegate (music178) went silent for ~24 s at its loop. The silence is authored:
            // the track fades out at ~246 s and the file then holds ~23.7 s of digitally silent frames up to the header's
            // total, with the loop back to sample 1663 (the top). Every "repeat from the top" track (loop within 0.5 s of
            // the start: 9 ATRAC3, 13 ADPCM) has such a tail, no track with a mid-song loop does, and no header field marks
            // an earlier loop end. Retail pauses there too (the maintainer's check in Aht Urhgan Whitegate, 2026-10-05), so the
            // decoder plays the file as written; this test pins that the gap comes from the data, not from the stream, and
            // that the music resumes at once after the restart.
            if (!Directory.Exists(GameDirectory))
            {
                return;
            }

            var locator = new FfxiSoundLocator(GameDirectory);
            byte[] file = File.ReadAllBytes(locator.FindMusic(178)!);
            Assert.True(FfxiSoundHeader.TryParse(file, out FfxiSoundHeader header));
            Assert.Equal(1663, header.LoopStartFrame);
            Atrac3Stream stream = Atrac3Stream.Open(file, header, loop: true)!;
            int rate = stream.SampleRate;
            long total = stream.TotalSamples;
            var pcm = new short[(total + rate) * 2];
            Assert.Equal(pcm.Length, stream.Read(pcm));

            long lastLoud = -1;
            for (long i = 0; i < total * 2; i++)
            {
                if (Math.Abs((int)pcm[i]) > 4) lastLoud = i / 2;
            }

            double tail = (total - lastLoud) / (double)rate;
            long firstLoudAfter = -1;
            for (long i = total * 2; i < pcm.Length && firstLoudAfter < 0; i++)
            {
                if (Math.Abs((int)pcm[i]) > 4) firstLoudAfter = i / 2 - total;
            }

            _output.WriteLine($"music178: total {total / (double)rate:F2} s, silent tail {tail:F2} s, music resumes {firstLoudAfter} samples after the restart");
            Assert.InRange(tail, 20.0, 26.0); // the authored gap, decoded as written
            Assert.InRange(firstLoudAfter, 0, rate / 20); // within 50 ms of the restart: the stream adds no silence
        }

        private static short[] ReadWav(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int pos = 12;
            while (pos + 8 <= bytes.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                if (id == "data")
                {
                    return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(bytes.AsSpan(pos + 8, Math.Min(size, bytes.Length - pos - 8))).ToArray();
                }

                pos += 8 + size + (size & 1);
            }

            throw new InvalidDataException(path);
        }

        [Fact]
        public void Retail_EveryAtrac3FileDecodes()
        {
            if (!Directory.Exists(GameDirectory) || Environment.GetEnvironmentVariable("GORDIAN_ATRAC3_ALL") != "1")
            {
                return; // decoding all of them takes a while: opt in with GORDIAN_ATRAC3_ALL=1
            }

            var locator = new FfxiSoundLocator(GameDirectory);
            var watch = Stopwatch.StartNew();
            int files = 0, malformed = 0, raw = 0;
            long samples = 0;
            var buffer = new short[1 << 16];
            foreach (string root in locator.Roots)
            {
                foreach (string path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(path);
                    if (ext != ".spw" && ext != ".bgw")
                    {
                        continue;
                    }

                    byte[] file = File.ReadAllBytes(path);
                    if (!FfxiSoundHeader.TryParse(file, out FfxiSoundHeader h) || h.Format != FfxiSampleFormat.Atrac3 || h.IsEncrypted)
                    {
                        continue;
                    }

                    Atrac3Stream stream = Atrac3Stream.Open(file, h, loop: false)!;
                    Assert.NotNull(stream);
                    long got = 0;
                    int n;
                    while ((n = stream.Read(buffer)) > 0) got += n;
                    Assert.Equal(stream.TotalSamples * stream.Channels, got);
                    if (stream.MalformedFrames > 0) _output.WriteLine($"{path}: {stream.MalformedFrames} malformed frames");
                    malformed += stream.MalformedFrames;
                    raw += stream.RawFrames;
                    samples += got / stream.Channels;
                    files++;
                }
            }

            _output.WriteLine($"{files} ATRAC3 files, {samples} samples per channel, {malformed} malformed channel frames, "
                + $"{raw} raw (unobfuscated) channel frames, {watch.Elapsed.TotalSeconds:F1} s");
            Assert.True(files > 1000);
            Assert.Equal(0, malformed);
        }
    }
}
