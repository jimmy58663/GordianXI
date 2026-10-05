// src/Gordian.Core/Audio/Atrac3/Atrac3Tables.cs
using System;

namespace Gordian.Core.Audio.Atrac3
{
    /// <summary>
    /// The constant tables of the ATRAC3 decoder: subband edges, value codes, scale factors, quantiser maxima, the
    /// IMDCT synthesis window and the QMF filter.
    /// </summary>
    /// <remarks>
    /// Tables A-G of the clean-room specification <c>docs/audio/atrac3.md</c> (section 15), which restates them from
    /// FFmpeg's reverse-engineered ATRAC3 description (https://github.com/FFmpeg/FFmpeg, <c>libavcodec/atrac3data.h</c>,
    /// <c>atrac.c</c>) and Sony's patents (US 5,974,379, US 5,758,316). Window and scale tables are computed here from the
    /// spec's formulas in double precision and stored as float, as the spec directs; nothing is copied from decoder source.
    /// </remarks>
    internal static class Atrac3Tables
    {
        /// <summary>Spectral lines per channel frame (and samples per channel per frame).</summary>
        public const int FrameSamples = 1024;

        /// <summary>Lines (and samples) per QMF band.</summary>
        public const int BandSamples = 256;

        /// <summary>Table A: subband <c>i</c> covers lines <c>SubbandStart[i] .. SubbandStart[i + 1] - 1</c>.</summary>
        public static ReadOnlySpan<short> SubbandStart =>
        [
            0, 8, 16, 24, 32, 40, 48, 56,
            64, 80, 96, 112, 128, 144, 160, 176,
            192, 224, 256, 288, 320, 352, 384, 416,
            448, 480, 512, 576, 640, 704, 768, 896,
            1024,
        ];

        /// <summary>Table D: constant-length code width per selector 0-7 (selector 1: per pair of values).</summary>
        public static ReadOnlySpan<byte> ClcBits => [0, 4, 3, 3, 4, 4, 5, 6];

        /// <summary>Table D: the reciprocal of the quantiser maximum per selector, as float (selector 0 is not coded).</summary>
        public static readonly float[] InverseMaxQuant = BuildInverseMaxQuant();

        /// <summary>Table C: <c>scale(i) = 2^((i - 15) / 3)</c>, computed in double, stored as float.</summary>
        public static readonly float[] ScaleFactor = BuildScaleFactors();

        /// <summary>Table E: the 512-point IMDCT synthesis window.</summary>
        public static readonly float[] Window = BuildWindow();

        /// <summary>Table F: the 48-tap QMF synthesis filter <c>h</c> (the 24 listed taps doubled and mirrored).</summary>
        public static readonly float[] QmfFilter = BuildQmf();

        /// <summary>Table G: the gain factor <c>2^(4 - L)</c> per level code <c>L</c>.</summary>
        public static readonly float[] GainLevel = BuildGainLevels();

        /// <summary>
        /// Table G: the per-sample ramp step <c>2^(d / 8)</c> for a level change <c>d = L_a - L_b</c>, indexed by
        /// <c>d + 15</c>.
        /// </summary>
        public static readonly float[] GainRamp = BuildGainRamps();

        /// <summary>Table B code lists as written in the spec: <c>"code:value"</c> (selector 1: <c>"code:first,second"</c>).</summary>
        internal static readonly string[][] VlcCodes =
        [
            [],
            ["0:0,0", "100:0,1", "101:0,-1", "1100:1,0", "1101:-1,0", "11100:1,1", "11101:1,-1", "11110:-1,1", "11111:-1,-1"],
            ["0:0", "100:1", "101:-1", "110:2", "111:-2"],
            ["0:0", "100:1", "101:-1", "1100:2", "1101:-2", "1110:3", "1111:-3"],
            ["0:0", "100:1", "101:-1", "1100:2", "1101:-2", "11100:3", "11101:-3", "11110:4", "11111:-4"],
            [
                "00:0", "010:1", "011:-1", "1000:2", "1001:-2", "1010:3", "1011:-3", "1100:7", "1101:-7",
                "11100:4", "11101:-4", "111100:5", "111101:-5", "111110:6", "111111:-6",
            ],
            [
                "000:0", "0010:1", "0011:-1", "0100:2", "0101:-2", "0110:3", "0111:-3", "1000:15", "1001:-15",
                "10100:4", "10101:-4", "10110:5", "10111:-5", "11000:6", "11001:-6",
                "110100:7", "110101:-7", "110110:8", "110111:-8", "111000:9", "111001:-9", "111010:10", "111011:-10",
                "1111000:11", "1111001:-11", "1111010:12", "1111011:-12", "1111100:13", "1111101:-13", "1111110:14", "1111111:-14",
            ],
            [
                "000:0", "0010:31", "0011:-31",
                "01000:1", "01001:-1", "01010:2", "01011:-2", "01100:3", "01101:-3", "01110:4", "01111:-4", "10000:5", "10001:-5",
                "100100:6", "100101:-6", "100110:7", "100111:-7", "101000:8", "101001:-8", "101010:9", "101011:-9",
                "101100:10", "101101:-10", "101110:11", "101111:-11", "110000:12", "110001:-12", "110010:13", "110011:-13",
                "1101000:14", "1101001:-14", "1101010:15", "1101011:-15", "1101100:16", "1101101:-16", "1101110:17", "1101111:-17",
                "1110000:18", "1110001:-18", "1110010:19", "1110011:-19", "1110100:20", "1110101:-20",
                "11101100:21", "11101101:-21", "11101110:22", "11101111:-22", "11110000:23", "11110001:-23",
                "11110010:24", "11110011:-24", "11110100:25", "11110101:-25", "11110110:26", "11110111:-26",
                "11111000:27", "11111001:-27", "11111010:28", "11111011:-28", "11111100:29", "11111101:-29",
                "11111110:30", "11111111:-30",
            ],
        ];

        /// <summary>
        /// Table B: one 256-entry lookup per selector 1-7, indexed by the next 8 bits of the stream. Each entry packs the
        /// code length (low byte) and the decoded value(s); see <see cref="VlcLength"/>, <see cref="VlcValue"/> and
        /// <see cref="VlcPairSecond"/>. Length 0 marks a prefix no code matches.
        /// </summary>
        public static readonly int[][] Vlc = BuildVlcTables();

        /// <summary>The code length of a <see cref="Vlc"/> entry (0: no code matches).</summary>
        public static int VlcLength(int entry) => entry & 0xFF;

        /// <summary>The decoded value of a <see cref="Vlc"/> entry (selector 1: the pair's first value).</summary>
        public static int VlcValue(int entry) => (sbyte)(entry >> 8);

        /// <summary>Selector 1 only: the pair's second value.</summary>
        public static int VlcPairSecond(int entry) => (sbyte)(entry >> 16);

        private static float[] BuildInverseMaxQuant()
        {
            ReadOnlySpan<double> maxQuant = [0.0, 1.5, 2.5, 3.5, 4.5, 7.5, 15.5, 31.5];
            var table = new float[8];
            for (int i = 1; i < 8; i++)
            {
                table[i] = (float)(1.0 / maxQuant[i]);
            }

            return table;
        }

        private static float[] BuildScaleFactors()
        {
            var table = new float[64];
            for (int i = 0; i < 64; i++)
            {
                table[i] = (float)Math.Pow(2.0, (i - 15) / 3.0);
            }

            return table;
        }

        private static float[] BuildWindow()
        {
            var w = new float[512];
            for (int n = 0; n < 256; n++)
            {
                double a = A(n);
                double b = A(255 - n);
                double v = 2.0 * a / (a * a + b * b);
                w[n] = (float)v;
                w[511 - n] = (float)v;
            }

            return w;

            static double A(int n) => 1.0 + Math.Sin(Math.PI * ((n + 0.5) / 256.0 - 0.5));
        }

        private static float[] BuildQmf()
        {
            ReadOnlySpan<double> q =
            [
                -0.00001461907, -0.00009205479, -0.000056157569, 0.00030117269, 0.0002422519, -0.00085293897,
                -0.0005205574, 0.0020340169, 0.00078333891, -0.0042153862, -0.00075614988, 0.0078402944,
                -0.000061169922, -0.01344162, 0.0024626821, 0.021736089, -0.007801671, -0.034090221,
                0.01880949, 0.054326009, -0.043596379, -0.099384367, 0.13207909, 0.46424159,
            ];
            var h = new float[48];
            for (int m = 0; m < 24; m++)
            {
                float v = (float)(2.0 * q[m]);
                h[m] = v;
                h[47 - m] = v;
            }

            return h;
        }

        private static float[] BuildGainLevels()
        {
            var table = new float[16];
            for (int l = 0; l < 16; l++)
            {
                table[l] = (float)Math.Pow(2.0, 4 - l);
            }

            return table;
        }

        private static float[] BuildGainRamps()
        {
            var table = new float[31];
            for (int d = -15; d <= 15; d++)
            {
                table[d + 15] = (float)Math.Pow(2.0, d / 8.0);
            }

            return table;
        }

        private static int[][] BuildVlcTables()
        {
            var tables = new int[8][];
            tables[0] = new int[256];
            for (int s = 1; s < 8; s++)
            {
                var table = new int[256];
                foreach (string entry in VlcCodes[s])
                {
                    int colon = entry.IndexOf(':');
                    string code = entry[..colon];
                    string[] values = entry[(colon + 1)..].Split(',');
                    int first = int.Parse(values[0], System.Globalization.CultureInfo.InvariantCulture);
                    int second = values.Length > 1 ? int.Parse(values[1], System.Globalization.CultureInfo.InvariantCulture) : 0;
                    int length = code.Length;
                    int prefix = Convert.ToInt32(code, 2) << (8 - length);
                    int packed = length | ((first & 0xFF) << 8) | ((second & 0xFF) << 16);
                    for (int fill = 0; fill < 1 << (8 - length); fill++)
                    {
                        table[prefix | fill] = packed;
                    }
                }

                tables[s] = table;
            }

            return tables;
        }
    }
}
