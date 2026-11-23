// tests/Gordian.Core.Tests/Network/SearchAnswerDiagnosisTests.cs
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Network.Search;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    /// <summary>
    /// #578: retail's search server answers failed LandSandBoat's hash check. The diagnosis tries other answer keys and hash
    /// layouts and reports which fits; retail's client keeps an answer whose header plainly answers the request.
    /// </summary>
    public class SearchAnswerDiagnosisTests
    {
        private const byte RequestType = 0x15;

        private static (byte[] Frame, SearchFrame.AnswerKey Key) Request()
        {
            var body = new byte[0x20];
            body[0x0B] = RequestType;
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x1C), 0x11223344);
            byte[] frame = SearchFrame.EncryptRequest(body, 0x0BADF00D, out var key);
            return (frame, key);
        }

        /// <summary>An answer of 0x30 data bytes (type 0x95, final) plus 8 bytes of padding.</summary>
        private static byte[] Content()
        {
            var content = new byte[0x38];
            BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(0x08, 2), 0x30);
            content[0x0A] = 0x80;
            content[0x0B] = 0x80 | RequestType;
            content[0x20] = 0x5A;
            return content;
        }

        /// <summary>Builds an answer frame with a chosen key and hash layout (the layouts the diagnosis is meant to tell apart).</summary>
        private static byte[] Answer(byte[] content, uint seed, uint? tail, bool seedTrailer, bool hashEnciphered, SearchAnswerHashCoverage coverage, bool breakHash = false)
        {
            int hashStart = content.Length;
            int hashEnd = hashStart + 16;
            var frame = new byte[hashEnd + (seedTrailer ? 4 : 0)];
            content.CopyTo(frame, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0, 2), (ushort)frame.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4, 4), SearchFrame.Magic);
            int dataLength = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(0x08, 2));
            ReadOnlySpan<byte> covered = coverage switch
            {
                SearchAnswerHashCoverage.PayloadAndPadding => frame.AsSpan(8, hashStart - 8),
                SearchAnswerHashCoverage.DataOnly => frame.AsSpan(8, dataLength - 8),
                _ => frame.AsSpan(0, hashStart),
            };
            MD5.HashData(covered, frame.AsSpan(hashStart, 16));
            if (breakHash) frame[hashStart] ^= 0xFF;

            using var suite = SearchFrame.NewSuite(seed, tail);
            int cipherEnd = hashEnciphered ? hashEnd : hashStart;
            byte[] plainHash = frame.AsSpan(hashStart, 16).ToArray();
            // Encipher by deciphering's inverse: run the block cipher's encipher through EncryptAndSign on a scratch copy.
            var scratch = new byte[cipherEnd];
            frame.AsSpan(0, cipherEnd).CopyTo(scratch);
            int blocks = (cipherEnd - 8) / 8;
            Encipher(suite, scratch.AsSpan(8, blocks * 8));
            scratch.AsSpan(8, blocks * 8).CopyTo(frame.AsSpan(8));
            if (!hashEnciphered) plainHash.CopyTo(frame, hashStart);
            if (seedTrailer) BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(frame.Length - 4, 4), seed);
            return frame;
        }

        private static void Encipher(Gordian.Core.Network.Crypto.LegacyBlowfishCryptoSuite suite, Span<byte> blocks)
        {
            // EncryptAndSign hashes then enciphers payload + hash; give it a buffer whose "payload" is all but the last 16 bytes,
            // with room for the hash, then copy the enciphered blocks back.
            var buffer = new byte[blocks.Length + 16];
            blocks.CopyTo(buffer);
            suite.EncryptAndSign(buffer, 0, blocks.Length);
            buffer.AsSpan(0, blocks.Length).CopyTo(blocks);
        }

        [Fact]
        public void LandSandBoatAnswer_VerifiesWithTheLandSandBoatRules()
        {
            var (_, key) = Request();
            byte[] frame = SearchFrame.EncryptResponse(Content(), key);

            var diagnosis = SearchAnswerDiagnosis.Diagnose(frame, key, RequestType);

            Assert.Equal(SearchAnswerKeyRule.SeedAndPlainTail, diagnosis.KeyRule);
            Assert.Equal(SearchAnswerHashRule.LandSandBoat, diagnosis.HashRule);
            Assert.True(diagnosis.Verified);
            Assert.True(diagnosis.SeedEchoed);
            Assert.Equal(0x30, diagnosis.DataLength);
            Assert.Equal(0x5A, diagnosis.Content![0x20]);
        }

        [Theory]
        [InlineData(SearchAnswerKeyRule.SeedOnly, true, true, SearchAnswerHashCoverage.DataOnly)]
        [InlineData(SearchAnswerKeyRule.SeedAndCipherTail, true, false, SearchAnswerHashCoverage.PayloadAndPadding)]
        [InlineData(SearchAnswerKeyRule.SeedAndPlainTail, false, true, SearchAnswerHashCoverage.WithHeader)]
        [InlineData(SearchAnswerKeyRule.SeedAndPlainTail, true, true, SearchAnswerHashCoverage.DataOnly)]
        public void OtherLayouts_FailTheLandSandBoatCheckButAreNamed(SearchAnswerKeyRule keyRule, bool seedTrailer, bool hashEnciphered, SearchAnswerHashCoverage coverage)
        {
            var (_, key) = Request();
            uint? tail = keyRule switch
            {
                SearchAnswerKeyRule.SeedAndPlainTail => key.PayloadTail,
                SearchAnswerKeyRule.SeedAndCipherTail => key.CipherTail,
                _ => null,
            };
            byte[] frame = Answer(Content(), key.Seed, tail, seedTrailer, hashEnciphered, coverage);
            Assert.False(SearchFrame.TryDecryptResponse((byte[])frame.Clone(), key, out _));

            var diagnosis = SearchAnswerDiagnosis.Diagnose(frame, key, RequestType);

            Assert.Equal(keyRule, diagnosis.KeyRule);
            Assert.Equal(new SearchAnswerHashRule(seedTrailer, hashEnciphered, coverage), diagnosis.HashRule);
            Assert.True(diagnosis.Verified);
            Assert.Equal(0x5A, diagnosis.Content![0x20]);
            Assert.Contains("request type 0x15", diagnosis.Describe(RequestType, 0x34));
        }

        /// <summary>Round 1 on retail: the answer's trailer was not our seed. A server seed of its own keys the answer here.</summary>
        [Theory]
        [InlineData(SearchAnswerKeyRule.TrailerSeed)]
        [InlineData(SearchAnswerKeyRule.TrailerSeedAndPlainTail)]
        [InlineData(SearchAnswerKeyRule.TrailerSeedAndCipherTail)]
        public void AnswerKeyedByItsOwnTrailer_IsVerified(SearchAnswerKeyRule keyRule)
        {
            var (_, key) = Request();
            const uint serverSeed = 0x5EED1234;
            uint? tail = keyRule switch
            {
                SearchAnswerKeyRule.TrailerSeedAndPlainTail => key.PayloadTail,
                SearchAnswerKeyRule.TrailerSeedAndCipherTail => key.CipherTail,
                _ => null,
            };
            byte[] frame = Answer(Content(), serverSeed, tail, true, true, SearchAnswerHashCoverage.PayloadAndPadding);

            var diagnosis = SearchAnswerDiagnosis.Diagnose(frame, key, RequestType);

            Assert.Equal(SearchAnswerTrailer.Other, diagnosis.Trailer);
            Assert.Equal(keyRule, diagnosis.KeyRule);
            Assert.True(diagnosis.Verified);
            Assert.Equal(0x5A, diagnosis.Content![0x20]);
        }

        [Fact]
        public void AnswerSentPlain_IsVerified()
        {
            var (_, key) = Request();
            byte[] content = Content();
            var frame = new byte[content.Length + 16 + 4];
            content.CopyTo(frame, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0, 2), (ushort)frame.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4, 4), SearchFrame.Magic);
            MD5.HashData(frame.AsSpan(8, content.Length - 8), frame.AsSpan(content.Length, 16));

            var diagnosis = SearchAnswerDiagnosis.Diagnose(frame, key, RequestType);

            Assert.Equal(SearchAnswerKeyRule.NotEnciphered, diagnosis.KeyRule);
            Assert.True(diagnosis.Verified);
            Assert.Equal(SearchAnswerTrailer.Zero, diagnosis.Trailer);
        }

        /// <summary>
        /// A key that deciphers to a mostly-zero body is named even when the header's type byte and the hash fit nothing, so the
        /// next retail log tells a key difference from a header or hash difference.
        /// </summary>
        [Fact]
        public void KeyWithAnUnexpectedHeader_IsNamedByItsZeroBytes()
        {
            var (_, key) = Request();
            byte[] content = Content();
            content[0x0B] = 0x42;
            byte[] frame = Answer(content, 0x5EED1234, null, true, true, SearchAnswerHashCoverage.PayloadAndPadding, breakHash: true);

            var diagnosis = SearchAnswerDiagnosis.Diagnose(frame, key, RequestType);

            Assert.Equal(SearchAnswerKeyRule.None, diagnosis.KeyRule);
            Assert.Equal(SearchAnswerKeyRule.TrailerSeed, diagnosis.BestZeroKey);
            Assert.Equal(0x42, diagnosis.BestZeroTypeByte);
            Assert.Equal(0x30, diagnosis.BestZeroDataLength);
            Assert.True(diagnosis.BestZeroPercent > 80, $"{diagnosis.BestZeroPercent}%");
            Assert.True(diagnosis.RunnerUpZeroPercent < 20, $"{diagnosis.RunnerUpZeroPercent}%");
            string line = diagnosis.Describe(RequestType, 0x34);
            Assert.Contains("most zeros", line);
            Assert.Contains("key TrailerSeed (type byte 0x42, data length 48)", line);
        }

        [Fact]
        public void AnswerWithAnUnknownHash_IsPlausibleButNotVerified()
        {
            var (_, key) = Request();
            byte[] frame = Answer(Content(), key.Seed, key.PayloadTail, true, true, SearchAnswerHashCoverage.PayloadAndPadding, breakHash: true);

            var diagnosis = SearchAnswerDiagnosis.Diagnose(frame, key, RequestType);

            Assert.Equal(SearchAnswerKeyRule.SeedAndPlainTail, diagnosis.KeyRule);
            Assert.Null(diagnosis.HashRule);
            Assert.False(diagnosis.Verified);
            Assert.True(diagnosis.Plausible);
            Assert.Contains("no hash layout fits", diagnosis.Describe(RequestType, 0x34));
        }

        [Theory]
        [InlineData(0x03, 0x80)]
        [InlineData(0x00, 0x80)]
        [InlineData(0x06, 0x85)]
        [InlineData(0x10, 0x95)]
        [InlineData(0x02, 0x82)]
        [InlineData(0x08, 0x88)]
        public void AnswerType_FollowsLandSandBoatsTableNotTheRequestType(byte requestType, byte answerType)
        {
            Assert.Equal(answerType, SearchAnswerDiagnosis.ExpectedAnswerType(requestType));

            var (_, key) = Request();
            byte[] content = Content();
            content[0x0B] = answerType;
            byte[] frame = Answer(content, key.Seed, key.PayloadTail, true, true, SearchAnswerHashCoverage.PayloadAndPadding, breakHash: true);
            Assert.True(SearchAnswerDiagnosis.Diagnose(frame, key, requestType).Plausible);
        }

        [Fact]
        public void AnswerWithAnUnknownKey_IsNeither()
        {
            var (_, key) = Request();
            byte[] frame = SearchFrame.EncryptResponse(Content(), new SearchFrame.AnswerKey(key.Seed + 1, key.PayloadTail));
            // Neither our seed nor the trailer keys it.
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(frame.Length - 4, 4), key.Seed + 2);

            var diagnosis = SearchAnswerDiagnosis.Diagnose(frame, key, RequestType);

            Assert.Equal(SearchAnswerKeyRule.None, diagnosis.KeyRule);
            Assert.False(diagnosis.Plausible);
            Assert.Null(diagnosis.Content);
        }

        // ---- the client keeps what it may ----

        private sealed class OneAnswerTransport : ISearchTransport
        {
            public Func<SearchFrame.AnswerKey, byte[]> Answer = _ => Array.Empty<byte>();

            public Task<ISearchConnection> ConnectAsync(string host, int port, CancellationToken cancellationToken) =>
                Task.FromResult<ISearchConnection>(new Connection(this));

            private sealed class Connection(OneAnswerTransport owner) : ISearchConnection
            {
                private readonly Queue<byte[]> _answers = new();

                public Task SendAsync(byte[] frame, CancellationToken cancellationToken)
                {
                    var copy = (byte[])frame.Clone();
                    Assert.True(SearchFrame.TryDecryptRequest(copy, out var key));
                    _answers.Enqueue(owner.Answer(key));
                    return Task.CompletedTask;
                }

                public async Task<byte[]?> ReadFrameAsync(CancellationToken cancellationToken)
                {
                    if (_answers.Count > 0) return _answers.Dequeue();
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return null;
                }

                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public async Task Client_KeepsAPlausibleUnverifiedAnswerOnlyForRetail(bool retail, bool kept)
        {
            var transport = new OneAnswerTransport
            {
                Answer = key => Answer(Content(), key.Seed, key.PayloadTail, true, true, SearchAnswerHashCoverage.PayloadAndPadding, breakHash: true),
            };
            var client = new SearchClient("search.test", 54002, transport) { Timeout = TimeSpan.FromMilliseconds(300), AcceptUnverifiedAnswers = retail };
            var body = new byte[0x20];
            body[0x0B] = RequestType;

            var exchange = await client.ExchangeAsync(body);

            Assert.Equal(kept ? 1 : 0, exchange.Contents.Count);
            Assert.Equal(kept, exchange.Complete);
        }

        [Fact]
        public async Task Client_KeepsAnAnswerVerifiedByAnotherLayoutOnLandSandBoatToo()
        {
            var transport = new OneAnswerTransport
            {
                Answer = key => Answer(Content(), key.Seed, null, true, true, SearchAnswerHashCoverage.DataOnly),
            };
            var client = new SearchClient("search.test", 54002, transport) { Timeout = TimeSpan.FromMilliseconds(300) };
            var body = new byte[0x20];
            body[0x0B] = RequestType;

            var exchange = await client.ExchangeAsync(body);

            Assert.Single(exchange.Contents);
            Assert.True(exchange.Complete);
        }
    }
}
