// src/Gordian.Core/Audio/Atrac3/Atrac3Imdct.cs
using System;

namespace Gordian.Core.Audio.Atrac3
{
    /// <summary>
    /// The 256-coefficient inverse MDCT of an ATRAC3 QMF band, with the sign and scale of the clean-room spec
    /// (<c>docs/audio/atrac3.md</c> section 8):
    /// <c>y[n] = -Σ_k X[k] cos((π / 256)(n + 1/2 + 128)(k + 1/2))</c>, <c>n = 0..511</c>.
    /// </summary>
    /// <remarks>
    /// Computed through a 256-point DCT-IV (the IMDCT's 512 outputs are that DCT-IV's values with the symmetries
    /// <c>D(-j-1) = D(j)</c>, <c>D(511-j) = -D(j)</c>, <c>D(j+512) = -D(j)</c>), and the DCT-IV through a 128-point
    /// complex FFT: <c>v[n] = (x[2n] + i·x[255-2n])·e^(-iπn/256)</c>, <c>V = FFT(v)</c>,
    /// <c>Z[p] = V[p]·e^(-iπ(4p+1)/1024)</c>, then <c>D[2p] = Re Z[p]</c> and <c>D[255-2p] = -Im Z[p]</c>.
    /// Derived here from the definition (a standard factorisation); <c>Atrac3DspTests</c> checks it against the direct sum.
    /// </remarks>
    internal sealed class Atrac3Imdct
    {
        private const int N = 256;
        private const int M = N / 2;

        private static readonly float[] PreCos = new float[M];
        private static readonly float[] PreSin = new float[M];
        private static readonly float[] PostCos = new float[M];
        private static readonly float[] PostSin = new float[M];
        private static readonly float[] FftCos = new float[M / 2];
        private static readonly float[] FftSin = new float[M / 2];
        private static readonly byte[] BitReverse = new byte[M];

        private readonly float[] _re = new float[M];
        private readonly float[] _im = new float[M];
        private readonly float[] _dct = new float[N];

        static Atrac3Imdct()
        {
            for (int n = 0; n < M; n++)
            {
                double pre = -Math.PI * n / N;
                PreCos[n] = (float)Math.Cos(pre);
                PreSin[n] = (float)Math.Sin(pre);
                double post = -Math.PI * (4 * n + 1) / (4.0 * N);
                PostCos[n] = (float)Math.Cos(post);
                PostSin[n] = (float)Math.Sin(post);
                int r = 0;
                for (int b = 0, v = n; b < 7; b++, v >>= 1)
                {
                    r = (r << 1) | (v & 1);
                }

                BitReverse[n] = (byte)r;
            }

            for (int k = 0; k < M / 2; k++)
            {
                double a = -2.0 * Math.PI * k / M;
                FftCos[k] = (float)Math.Cos(a);
                FftSin[k] = (float)Math.Sin(a);
            }
        }

        /// <summary>Transforms 256 coefficients <paramref name="input"/> into 512 samples <paramref name="output"/>.</summary>
        public void Transform(ReadOnlySpan<float> input, Span<float> output)
        {
            Span<float> re = _re;
            Span<float> im = _im;

            // Pre-twiddle into bit-reversed order.
            for (int n = 0; n < M; n++)
            {
                float a = input[2 * n];
                float b = input[N - 1 - 2 * n];
                float c = PreCos[n];
                float s = PreSin[n];
                int r = BitReverse[n];
                re[r] = a * c - b * s;
                im[r] = a * s + b * c;
            }

            // Iterative radix-2 forward FFT.
            for (int size = 2; size <= M; size <<= 1)
            {
                int half = size >> 1;
                int step = M / size;
                for (int start = 0; start < M; start += size)
                {
                    for (int k = 0; k < half; k++)
                    {
                        float wr = FftCos[k * step];
                        float wi = FftSin[k * step];
                        int i = start + k;
                        int j = i + half;
                        float tr = re[j] * wr - im[j] * wi;
                        float ti = re[j] * wi + im[j] * wr;
                        re[j] = re[i] - tr;
                        im[j] = im[i] - ti;
                        re[i] += tr;
                        im[i] += ti;
                    }
                }
            }

            // Post-twiddle into the DCT-IV.
            Span<float> d = _dct;
            for (int p = 0; p < M; p++)
            {
                float c = PostCos[p];
                float s = PostSin[p];
                float zr = re[p] * c - im[p] * s;
                float zi = re[p] * s + im[p] * c;
                d[2 * p] = zr;
                d[N - 1 - 2 * p] = -zi;
            }

            // Unfold into the IMDCT outputs, negated: y[n] = -D(n + 128).
            for (int n = 0; n < 128; n++)
            {
                output[n] = -d[n + 128];
            }

            for (int n = 128; n < 384; n++)
            {
                output[n] = d[383 - n];
            }

            for (int n = 384; n < 512; n++)
            {
                output[n] = d[n - 384];
            }
        }

        /// <summary>The spec's IMDCT by its direct sum (tests only).</summary>
        internal static void Direct(ReadOnlySpan<float> input, Span<float> output)
        {
            for (int n = 0; n < 2 * N; n++)
            {
                double sum = 0;
                for (int k = 0; k < N; k++)
                {
                    sum += input[k] * Math.Cos(Math.PI / N * (n + 0.5 + N / 2) * (k + 0.5));
                }

                output[n] = (float)-sum;
            }
        }
    }
}
