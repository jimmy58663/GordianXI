// tests/Gordian.Core.Tests/Audio/FfxiSoundDecodeTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gordian.Core.Audio;
using Xunit;
using Xunit.Abstractions;

namespace Gordian.Core.Tests.Audio
{
    /// <summary>The retail <c>.bgw</c> / <c>.spw</c> header, ADPCM codec, streaming and locator (#38).</summary>
    public class FfxiSoundDecodeTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";
        private readonly ITestOutputHelper _output;

        public FfxiSoundDecodeTests(ITestOutputHelper output) => _output = output;

        /// <summary>A synthetic mono SeWave ADPCM file of <paramref name="blocks"/> 9-byte frames.</summary>
        internal static byte[] SyntheticEffect(int id, int blocks, int loopBlock, int rate, Func<int, byte[]> frame, int channels = 1)
        {
            int frameSize = 9;
            var data = new byte[FfxiSoundHeader.DataOffset + blocks * frameSize * channels];
            "SeWave"u8.CopyTo(data);
            BitConverter.GetBytes(data.Length).CopyTo(data, 0x08);
            BitConverter.GetBytes(0).CopyTo(data, 0x0C);
            BitConverter.GetBytes(id).CopyTo(data, 0x10);
            BitConverter.GetBytes(blocks).CopyTo(data, 0x14);
            BitConverter.GetBytes(loopBlock).CopyTo(data, 0x18);
            BitConverter.GetBytes(rate - 1000).CopyTo(data, 0x1C);
            BitConverter.GetBytes(1000).CopyTo(data, 0x20);
            BitConverter.GetBytes(0x30).CopyTo(data, 0x24);
            data[0x2A] = (byte)channels;
            data[0x2B] = 16;
            for (int b = 0; b < blocks; b++)
            {
                for (int c = 0; c < channels; c++)
                {
                    frame(b).CopyTo(data, FfxiSoundHeader.DataOffset + (b * channels + c) * frameSize);
                }
            }

            return data;
        }

        [Fact]
        public void Header_SeWave_ReadsSequentialFieldsAndSumsTheRate()
        {
            byte[] file = SyntheticEffect(2060, 4, 1, 48000, _ => new byte[9]);
            Assert.True(FfxiSoundHeader.TryParse(file, out FfxiSoundHeader h));
            Assert.Equal(FfxiSoundKind.Effect, h.Kind);
            Assert.Equal(FfxiSampleFormat.Adpcm, h.Format);
            Assert.Equal(2060, h.Id);
            Assert.Equal(4, h.SampleBlocks);
            Assert.Equal(1, h.LoopStartBlock);
            Assert.Equal(48000, h.SampleRate);
            Assert.Equal(1, h.Channels);
            Assert.Equal(9, h.FrameSize);
            Assert.Equal(16, h.SamplesPerBlock);
            Assert.True(h.IsLooped);
            Assert.Equal(16, h.LoopStartFrame);
            Assert.True(h.IsDecodable);
        }

        [Fact]
        public void Header_UnknownMarker_IsRejected()
        {
            Assert.False(FfxiSoundHeader.TryParse(new byte[0x40], out _));
            Assert.False(FfxiSoundHeader.TryParse("SeWave"u8.ToArray(), out _));
        }

        [Fact]
        public void Adpcm_Filter0_ShiftsNibblesByTwelveMinusRange()
        {
            // Range 12 → shift 0, filter 0 → no prediction: the nibbles come out as signed values, low nibble first.
            var frame = new byte[] { 0x0C, 0x21, 0xF7, 0x08, 0, 0, 0, 0, 0 };
            var output = new short[16];
            int h0 = 0, h1 = 0;
            Assert.Equal(16, FfxiAdpcm.DecodeFrame(frame, output, 1, ref h0, ref h1));
            Assert.Equal(new short[] { 1, 2, 7, -1, -8, 0 }, output.Take(6).ToArray());
        }

        [Fact]
        public void Adpcm_Prediction_UsesArithmeticShift()
        {
            // Filter 1 (240/256 of the last sample), range 12: a negative history must floor, not truncate.
            var frame = new byte[] { 0x1C, 0x00, 0, 0, 0, 0, 0, 0, 0 };
            var output = new short[16];
            int h0 = -1, h1 = 0;
            FfxiAdpcm.DecodeFrame(frame, output, 1, ref h0, ref h1);
            Assert.Equal(-1, output[0]); // (-240) >> 8 = -1, where -240 / 256 would be 0
        }

        [Fact]
        public void Adpcm_FilterAboveFour_IsSilentAndKeepsHistory()
        {
            var frame = new byte[] { 0x5C, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77 };
            var output = Enumerable.Repeat((short)99, 16).ToArray();
            int h0 = 1234, h1 = 55;
            FfxiAdpcm.DecodeFrame(frame, output, 1, ref h0, ref h1);
            Assert.All(output, s => Assert.Equal(0, s));
            Assert.Equal(1234, h0);
            Assert.Equal(55, h1);
        }

        [Fact]
        public void Stream_LoopsBackToTheLoopBlock()
        {
            // Block b decodes to the constant b + 1 (filter 0, range 12, every nibble = b + 1).
            byte[] file = SyntheticEffect(1, 3, 1, 22050, b =>
            {
                var f = new byte[9];
                f[0] = 0x0C;
                for (int i = 1; i < 9; i++)
                {
                    f[i] = (byte)((b + 1) | ((b + 1) << 4));
                }

                return f;
            });

            FfxiSoundStream stream = FfxiSoundStream.Open(file)!;
            var buffer = new short[16 * 5];
            Assert.Equal(buffer.Length, stream.Read(buffer));
            int[] perBlock = Enumerable.Range(0, 5).Select(b => (int)buffer[b * 16]).ToArray();
            Assert.Equal(new[] { 1, 2, 3, 2, 3 }, perBlock);

            PcmClip clip = FfxiSoundStream.DecodeClip(file)!;
            Assert.Equal(48, clip.Samples.Length);
            Assert.Equal(16, clip.LoopStartFrame);
        }

        [Fact]
        public void Stream_StereoFramesAreChannelOrderedPerBlock()
        {
            byte[] file = SyntheticEffect(1, 1, -1, 22050, _ => new byte[9], channels: 2);
            // Channel 1's frame decodes to 3s, channel 0's to 0s.
            file[FfxiSoundHeader.DataOffset + 9] = 0x0C;
            for (int i = 1; i < 9; i++)
            {
                file[FfxiSoundHeader.DataOffset + 9 + i] = 0x33;
            }

            PcmClip clip = FfxiSoundStream.DecodeClip(file)!;
            Assert.Equal(32, clip.Samples.Length);
            Assert.Equal(0, clip.Samples[0]);
            Assert.Equal(3, clip.Samples[1]);
            Assert.False(clip.IsLooped);
        }

        [Fact]
        public void Decoder_HandsAtrac3ToTheRegisteredDecoder()
        {
            byte[] file = SyntheticEffect(7, 2, -1, 48000, _ => new byte[9]);
            BitConverter.GetBytes(3).CopyTo(file, 0x0C); // format 3 = ATRAC3
            IAtrac3Decoder? previous = FfxiSoundDecoder.Atrac3;
            Assert.IsType<Gordian.Core.Audio.Atrac3.Atrac3Decoder>(previous); // registered by default, so the App's "no decoder" log stays quiet
            try
            {
                FfxiSoundDecoder.Atrac3 = null;
                Assert.Null(FfxiSoundDecoder.Open(file));
                FfxiSoundDecoder.Atrac3 = new FakeAtrac3();
                IPcmSource? source = FfxiSoundDecoder.Open(file);
                Assert.NotNull(source);
                PcmClip clip = FfxiSoundDecoder.DecodeClip(file)!;
                Assert.Equal(10, clip.Samples.Length);
                Assert.Equal(7, clip.Id);

                // The real decoder through the seam: a synthetic one-frame ATRAC3 effect (key frame only) decodes to silence.
                FfxiSoundDecoder.Atrac3 = previous;
                byte[] atrac3 = new byte[FfxiSoundHeader.DataOffset + 192];
                file.AsSpan(0, FfxiSoundHeader.DataOffset).CopyTo(atrac3);
                BitConverter.GetBytes(atrac3.Length).CopyTo(atrac3, 0x08);
                BitConverter.GetBytes(1024).CopyTo(atrac3, 0x14);
                PcmClip real = FfxiSoundDecoder.DecodeClip(atrac3)!;
                Assert.Equal(1024, real.Samples.Length);
                Assert.All(real.Samples, s => Assert.Equal(0, s));
            }
            finally
            {
                FfxiSoundDecoder.Atrac3 = previous;
            }
        }

        private sealed class FakeAtrac3 : IAtrac3Decoder
        {
            public IPcmSource? Open(byte[] file, in FfxiSoundHeader header, bool loop) =>
                new PcmClip(new short[10], 1, header.SampleRate, -1).Open(false);
        }

        [Fact]
        public void Locator_BuildsLowerCasePaths()
        {
            Assert.Equal(Path.Combine("win", "se", "se002", "se002060.spw"), FfxiSoundLocator.EffectRelativePath(2060));
            Assert.Equal(Path.Combine("win", "se", "se016", "se016023.spw"), FfxiSoundLocator.EffectRelativePath(16023));
            Assert.Equal(Path.Combine("win", "music", "data", "music023.bgw"), FfxiSoundLocator.MusicRelativePath(23));
        }

        // ---- Retail data ----

        [Fact]
        public void Retail_KnownHeaders()
        {
            if (!Directory.Exists(GameDirectory))
            {
                return;
            }

            var locator = new FfxiSoundLocator(GameDirectory);
            string music = locator.FindMusic(23)!;
            Assert.True(FfxiSoundHeader.TryParse(File.ReadAllBytes(music), out FfxiSoundHeader m));
            Assert.Equal(FfxiSoundKind.Music, m.Kind);
            Assert.Equal(23, m.Id);
            Assert.Equal(2, m.Channels);
            Assert.Equal(65, m.FrameSize);
            Assert.Equal(128, m.SamplesPerBlock);
            Assert.True(m.IsLooped);
            _output.WriteLine($"music023: {m.SampleRate} Hz, {m.DurationSeconds:F1} s, loop at {m.LoopStartFrame / (double)m.SampleRate:F1} s");

            string effect = locator.FindEffect(2060)!;
            Assert.True(FfxiSoundHeader.TryParse(File.ReadAllBytes(effect), out FfxiSoundHeader e));
            Assert.Equal(FfxiSoundKind.Effect, e.Kind);
            Assert.Equal(2060, e.Id);
            Assert.Equal(48000, e.SampleRate);
            Assert.Equal(1, e.Channels);
            Assert.Equal(9, e.FrameSize);
            Assert.Equal(16, e.SamplesPerBlock);
        }

        [Fact]
        public void Retail_StereoChannelsCorrelate()
        {
            // If the per-block channel order were wrong the two decoded channels would be unrelated noise.
            if (!Directory.Exists(GameDirectory))
            {
                return;
            }

            var locator = new FfxiSoundLocator(GameDirectory);
            FfxiSoundStream stream = FfxiSoundStream.Open(File.ReadAllBytes(locator.FindMusic(23)!))!;
            var buffer = new short[2 * 48000 * 10];
            int n = stream.Read(buffer);
            double sumLr = 0, sumLl = 0, sumRr = 0;
            for (int i = 0; i + 1 < n; i += 2)
            {
                sumLr += buffer[i] * (double)buffer[i + 1];
                sumLl += buffer[i] * (double)buffer[i];
                sumRr += buffer[i + 1] * (double)buffer[i + 1];
            }

            double correlation = sumLr / Math.Sqrt(sumLl * sumRr);
            double rms = Math.Sqrt(sumLl / (n / 2));
            _output.WriteLine($"music023 L/R correlation {correlation:F3}, left RMS {rms:F0}");
            Assert.True(correlation > 0.3, $"correlation {correlation}");
            Assert.True(rms > 100, $"rms {rms}");
        }

        [Fact]
        public void Retail_EveryFileParsesAndAdpcmDecodes()
        {
            if (!Directory.Exists(GameDirectory))
            {
                return;
            }

            var locator = new FfxiSoundLocator(GameDirectory);
            var formats = new Dictionary<string, int>();
            int unparsed = 0;
            int decoded = 0;
            int checkedFiles = 0;
            int mismatched = 0;
            foreach (string root in locator.Roots)
            {
                foreach (string path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(path);
                    if (ext != ".spw" && ext != ".bgw")
                    {
                        continue;
                    }

                    checkedFiles++;
                    byte[] head = new byte[FfxiSoundHeader.DataOffset];
                    using (FileStream fs = File.OpenRead(path))
                    {
                        fs.ReadExactly(head);
                    }

                    if (!FfxiSoundHeader.TryParse(head, out FfxiSoundHeader h))
                    {
                        unparsed++;
                        continue;
                    }

                    string key = $"{h.Kind}/{(h.IsEncrypted ? "encrypted" : h.Format.ToString())}";
                    formats[key] = formats.GetValueOrDefault(key) + 1;

                    // Decode a sample of the effects fully (all of them takes too long for a unit test).
                    if (h.Kind == FfxiSoundKind.Effect && h.IsDecodable && decoded < 400 && checkedFiles % 7 == 0)
                    {
                        PcmClip? clip = FfxiSoundStream.DecodeClip(File.ReadAllBytes(path));
                        Assert.NotNull(clip);
                        if (h.TotalFrames * h.Channels != clip!.Samples.Length)
                        {
                            _output.WriteLine($"{path}: {h.Format} expected {h.TotalFrames * h.Channels} got {clip.Samples.Length} size {h.FileSize} disk {new FileInfo(path).Length} frame {h.FrameSize} blocks {h.SampleBlocks} ch {h.Channels}");                            mismatched++;
                        }

                        decoded++;
                    }
                }
            }

            foreach (var pair in formats.OrderBy(p => p.Key))
            {
                _output.WriteLine($"{pair.Key}: {pair.Value}");
            }

            _output.WriteLine($"unparsed {unparsed}, decoded {decoded} effects of {checkedFiles} files");
            Assert.True(decoded > 100);
            Assert.Equal(0, mismatched);
        }
    }
}
