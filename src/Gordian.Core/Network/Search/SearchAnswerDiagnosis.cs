// src/Gordian.Core/Network/Search/SearchAnswerDiagnosis.cs
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Gordian.Core.Network.Search
{
    /// <summary>Which key deciphered a search server answer (see <see cref="SearchAnswerDiagnosis"/>).</summary>
    public enum SearchAnswerKeyRule
    {
        /// <summary>No key gave a plain header that answers the request.</summary>
        None,

        /// <summary><c>MD5(constant + seed + plain request payload tail)</c>: LandSandBoat's answer key.</summary>
        SeedAndPlainTail,

        /// <summary><c>MD5(constant + seed)</c>: the request's own key.</summary>
        SeedOnly,

        /// <summary><c>MD5(constant + seed + enciphered request payload tail)</c>.</summary>
        SeedAndCipherTail,

        /// <summary><c>MD5(constant + answer trailer)</c>: the 4 bytes that end the answer as the seed (round 1: not our seed).</summary>
        TrailerSeed,

        /// <summary><c>MD5(constant + answer trailer + plain request payload tail)</c>.</summary>
        TrailerSeedAndPlainTail,

        /// <summary><c>MD5(constant + answer trailer + enciphered request payload tail)</c>.</summary>
        TrailerSeedAndCipherTail,

        /// <summary>No encipherment: the answer's bytes are read as they came.</summary>
        NotEnciphered,
    }

    /// <summary>What the 4 bytes that end a search server answer are, next to the request's seed.</summary>
    public enum SearchAnswerTrailer
    {
        /// <summary>The request's seed, as LandSandBoat sends it.</summary>
        Echoed,

        /// <summary>The request's seed with its bytes reversed.</summary>
        EchoedReversed,

        /// <summary>Four zero bytes.</summary>
        Zero,

        /// <summary>Anything else (a seed of the server's own, or no seed at all).</summary>
        Other,
    }

    /// <summary>What the MD5 at the end of a search server answer covers.</summary>
    public enum SearchAnswerHashCoverage
    {
        /// <summary>From byte 8 to the hash, padding included: LandSandBoat's rule.</summary>
        PayloadAndPadding,

        /// <summary>From byte 8 to the data length at 0x08 (no padding).</summary>
        DataOnly,

        /// <summary>From byte 0 to the hash (the 8 header bytes included).</summary>
        WithHeader,
    }

    /// <summary>
    /// Where the hash of a search server answer sits and how it was written. LandSandBoat's answers end with the hash, enciphered,
    /// and then the request's 4-byte key seed.
    /// </summary>
    /// <param name="SeedTrailer">True when 4 bytes (the seed) follow the hash; false when the hash ends the frame.</param>
    /// <param name="HashEnciphered">True when the hash's blocks are enciphered with the payload; false when it is sent plain.</param>
    /// <param name="Coverage">The bytes the hash covers.</param>
    public readonly record struct SearchAnswerHashRule(bool SeedTrailer, bool HashEnciphered, SearchAnswerHashCoverage Coverage)
    {
        /// <summary>LandSandBoat's answer layout.</summary>
        public static SearchAnswerHashRule LandSandBoat => new(true, true, SearchAnswerHashCoverage.PayloadAndPadding);

        public override string ToString() =>
            $"{(HashEnciphered ? "enciphered" : "plain")} hash {(SeedTrailer ? "before the seed" : "at the end")} over {Coverage}";
    }

    /// <summary>
    /// What a search server answer that failed <see cref="SearchFrame.TryDecryptResponse"/> looks like (#578: on retail every
    /// answer failed the check, on LandSandBoat none did). It tries the answer keys and hash layouts a server could plausibly
    /// use and reports the first that fits, or failing that the key that deciphers a header naming the request. Holds no
    /// payload bytes or key material in <see cref="Describe"/>: only sizes, header fields and the rules that matched.
    /// <para>
    /// The LandSandBoat layout (the only one known from a citable source) is from <c>src/search/search_handler.cpp</c>
    /// (https://github.com/LandSandBoat/server); the other layouts are guesses to tell retail's apart, not documented formats.
    /// </para>
    /// </summary>
    public sealed record SearchAnswerDiagnosis(
        int Length,
        int LengthWord,
        bool MagicIsIxff,
        bool SeedEchoed,
        SearchAnswerKeyRule KeyRule,
        SearchAnswerHashRule? HashRule,
        byte AnswerTypeByte,
        int DataLength,
        bool HeaderAnswersRequest)
    {
        /// <summary>What the answer's last 4 bytes are next to the request's seed.</summary>
        public SearchAnswerTrailer Trailer { get; init; }

        /// <summary>
        /// The key whose deciphered body has the most zero bytes. Search answers are mostly zero padding and small numbers,
        /// while a wrong key leaves about 1 byte in 256 zero, so this finds the key even when the header or hash layout differs
        /// from LandSandBoat's. Reported when nothing else fits.
        /// </summary>
        public SearchAnswerKeyRule BestZeroKey { get; init; }

        /// <summary>The zero byte share of <see cref="BestZeroKey"/>'s deciphered body, in percent.</summary>
        public int BestZeroPercent { get; init; }

        /// <summary>The type byte (0x0B) under <see cref="BestZeroKey"/>.</summary>
        public byte BestZeroTypeByte { get; init; }

        /// <summary>The data length word (0x08) under <see cref="BestZeroKey"/>.</summary>
        public int BestZeroDataLength { get; init; }

        /// <summary>The second highest zero byte share of the keys tried, in percent (how far ahead the best one is).</summary>
        public int RunnerUpZeroPercent { get; init; }

        /// <summary>The plain frame from its start to the hash, when <see cref="KeyRule"/> is not <see cref="SearchAnswerKeyRule.None"/>.</summary>
        public byte[]? Content { get; init; }

        /// <summary>True when a key and a hash layout both fit: the answer is verified, only differently from LandSandBoat.</summary>
        public bool Verified => KeyRule != SearchAnswerKeyRule.None && HashRule.HasValue;

        /// <summary>True when a key deciphers a header that answers the request (its answer type, plausible length), hash or not.</summary>
        public bool Plausible => KeyRule != SearchAnswerKeyRule.None && HeaderAnswersRequest;

        /// <summary>One log line: sizes, header fields and the rules that matched; no payload, no key bytes.</summary>
        public string Describe(byte requestType, int requestLength) =>
            $"request type 0x{requestType:X2} ({requestLength} bytes); answer {Length} bytes, length word {LengthWord}, " +
            $"magic {(MagicIsIxff ? "IXFF" : "other")}, trailer {Trailer}; " +
            (KeyRule == SearchAnswerKeyRule.None
                ? $"no key deciphers a header that answers the request (expected type 0x{ExpectedAnswerType(requestType):X2}); " +
                  $"most zeros {BestZeroPercent}% with key {BestZeroKey} (type byte 0x{BestZeroTypeByte:X2}, data length " +
                  $"{BestZeroDataLength}), next best {RunnerUpZeroPercent}%"
                : $"key {KeyRule}, type byte 0x{AnswerTypeByte:X2}, data length {DataLength}, " +
                  (HashRule is { } rule ? $"hash fits: {rule}" : "no hash layout fits"));

        /// <summary>
        /// Diagnoses an answer <paramref name="frame"/> as it came off the wire (still enciphered; it is not changed) to the
        /// request of type <paramref name="requestType"/> that left <paramref name="answerKey"/>.
        /// </summary>
        public static SearchAnswerDiagnosis Diagnose(ReadOnlySpan<byte> frame, SearchFrame.AnswerKey answerKey, byte requestType)
        {
            int length = frame.Length;
            int lengthWord = length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(frame) : 0;
            bool magic = length >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(4, 4)) == SearchFrame.Magic;
            uint trailerSeed = length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(length - 4, 4)) : 0;
            bool seedEchoed = length >= 4 && trailerSeed == answerKey.Seed;
            var trailer = seedEchoed ? SearchAnswerTrailer.Echoed
                : length >= 4 && trailerSeed == BinaryPrimitives.ReverseEndianness(answerKey.Seed) ? SearchAnswerTrailer.EchoedReversed
                : length >= 4 && trailerSeed == 0 ? SearchAnswerTrailer.Zero
                : SearchAnswerTrailer.Other;
            var none = new SearchAnswerDiagnosis(length, lengthWord, magic, seedEchoed, SearchAnswerKeyRule.None, null, 0, 0, false) { Trailer = trailer };
            if (length < SearchAnswerHeader.Size + 16) return none;

            SearchAnswerDiagnosis? fallback = null;
            var best = (Key: SearchAnswerKeyRule.None, Percent: -1, Type: (byte)0, DataLength: 0);
            int runnerUp = 0;
            foreach (var keyRule in KeyRules)
            {
                using var suite = KeySuite(keyRule, answerKey, trailerSeed);

                foreach (bool seedTrailer in new[] { true, false })
                {
                    int hashEnd = seedTrailer ? length - 4 : length;
                    int hashStart = hashEnd - 16;
                    if (hashStart < SearchAnswerHeader.Size) continue;

                    foreach (bool hashEnciphered in new[] { true, false })
                    {
                        // Without a key the hash is plain either way: one pass is enough.
                        if (suite == null && !hashEnciphered) continue;
                        var plain = frame.ToArray();
                        int cipherEnd = hashEnciphered ? hashEnd : hashStart;
                        int blocks = (cipherEnd - SearchFrame.HeaderSize) / 8;
                        suite?.DecipherWholeBlocks(plain.AsSpan(SearchFrame.HeaderSize, blocks * 8));

                        byte type = plain[0x0B];
                        int dataLength = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(0x08, 2));
                        bool answers = TypeAnswers(type, requestType)
                            && dataLength >= SearchAnswerHeader.Size && dataLength <= length;

                        ReadOnlySpan<byte> hash = hashEnciphered ? plain.AsSpan(hashStart, 16) : frame.Slice(hashStart, 16);
                        foreach (var coverage in new[] { SearchAnswerHashCoverage.PayloadAndPadding, SearchAnswerHashCoverage.DataOnly, SearchAnswerHashCoverage.WithHeader })
                        {
                            if (!HashFits(plain, hash, hashStart, dataLength, coverage)) continue;
                            return new SearchAnswerDiagnosis(length, lengthWord, magic, seedEchoed, keyRule,
                                new SearchAnswerHashRule(seedTrailer, hashEnciphered, coverage), type, dataLength, answers)
                            {
                                Trailer = trailer,
                                Content = plain.AsSpan(0, hashStart).ToArray(),
                            };
                        }

                        // LandSandBoat's block range (seed trailer, hash enciphered) is the one a header-only match reports, and
                        // the one the zero count reads (the body before the hash, the 8 header bytes left out).
                        if (!(seedTrailer && hashEnciphered)) continue;
                        if (answers && fallback == null)
                        {
                            fallback = new SearchAnswerDiagnosis(length, lengthWord, magic, seedEchoed, keyRule, null, type, dataLength, true)
                            {
                                Trailer = trailer,
                                Content = plain.AsSpan(0, hashStart).ToArray(),
                            };
                        }
                        int percent = ZeroPercent(plain.AsSpan(SearchFrame.HeaderSize, hashStart - SearchFrame.HeaderSize));
                        if (percent > best.Percent)
                        {
                            runnerUp = Math.Max(runnerUp, best.Percent);
                            best = (keyRule, percent, type, dataLength);
                        }
                        else
                        {
                            runnerUp = Math.Max(runnerUp, percent);
                        }
                    }
                }
            }
            var result = fallback ?? none;
            return result with
            {
                BestZeroKey = best.Key,
                BestZeroPercent = Math.Max(0, best.Percent),
                BestZeroTypeByte = best.Type,
                BestZeroDataLength = best.DataLength,
                RunnerUpZeroPercent = runnerUp,
            };
        }

        /// <summary>The answer keys tried, LandSandBoat's first.</summary>
        private static readonly SearchAnswerKeyRule[] KeyRules =
        [
            SearchAnswerKeyRule.SeedAndPlainTail, SearchAnswerKeyRule.SeedOnly, SearchAnswerKeyRule.SeedAndCipherTail,
            SearchAnswerKeyRule.TrailerSeed, SearchAnswerKeyRule.TrailerSeedAndPlainTail, SearchAnswerKeyRule.TrailerSeedAndCipherTail,
            SearchAnswerKeyRule.NotEnciphered,
        ];

        /// <summary>The cipher for <paramref name="rule"/>, or null for <see cref="SearchAnswerKeyRule.NotEnciphered"/>.</summary>
        internal static Crypto.LegacyBlowfishCryptoSuite? KeySuite(SearchAnswerKeyRule rule, SearchFrame.AnswerKey answerKey, uint trailerSeed) => rule switch
        {
            SearchAnswerKeyRule.SeedAndPlainTail => SearchFrame.NewSuite(answerKey.Seed, answerKey.PayloadTail),
            SearchAnswerKeyRule.SeedOnly => SearchFrame.NewSuite(answerKey.Seed, null),
            SearchAnswerKeyRule.SeedAndCipherTail => SearchFrame.NewSuite(answerKey.Seed, answerKey.CipherTail),
            SearchAnswerKeyRule.TrailerSeed => SearchFrame.NewSuite(trailerSeed, null),
            SearchAnswerKeyRule.TrailerSeedAndPlainTail => SearchFrame.NewSuite(trailerSeed, answerKey.PayloadTail),
            SearchAnswerKeyRule.TrailerSeedAndCipherTail => SearchFrame.NewSuite(trailerSeed, answerKey.CipherTail),
            _ => null,
        };

        private static int ZeroPercent(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty) return 0;
            int zeros = 0;
            foreach (byte b in bytes) if (b == 0) zeros++;
            return zeros * 100 / bytes.Length;
        }

        /// <summary>
        /// The answer type byte (0x0B) LandSandBoat sends for a request type: <c>/sea</c> and <c>/sea all</c> (0x03 / 0x00) are
        /// answered with 0x80, both price histories (0x05 / 0x06) with 0x85, both Auction House lists (0x15 / 0x10) with 0x95;
        /// otherwise the request type with bit 7 set (0x02 -> 0x82, 0x08 -> 0x88). LandSandBoat <c>src/search/packets/</c>.
        /// </summary>
        public static byte ExpectedAnswerType(byte requestType) => requestType switch
        {
            0x00 or 0x03 => 0x80,
            0x05 or 0x06 => 0x85,
            0x10 or 0x15 => 0x95,
            _ => (byte)(0x80 | requestType),
        };

        /// <summary>
        /// Whether an answer type byte answers <paramref name="requestType"/>: LandSandBoat's (<see cref="ExpectedAnswerType"/>)
        /// or the request type with bit 7 set, which retail sends for <c>/sea</c> (0x03 answered with 0x83; #578 round 2).
        /// </summary>
        public static bool TypeAnswers(byte typeByte, byte requestType) =>
            typeByte == ExpectedAnswerType(requestType) || typeByte == (byte)(0x80 | requestType);

        private static bool HashFits(byte[] plain, ReadOnlySpan<byte> hash, int hashStart, int dataLength, SearchAnswerHashCoverage coverage)
        {
            ReadOnlySpan<byte> covered;
            switch (coverage)
            {
                case SearchAnswerHashCoverage.PayloadAndPadding:
                    covered = plain.AsSpan(SearchFrame.HeaderSize, hashStart - SearchFrame.HeaderSize);
                    break;
                case SearchAnswerHashCoverage.DataOnly:
                    if (dataLength <= SearchFrame.HeaderSize || dataLength >= hashStart) return false;
                    covered = plain.AsSpan(SearchFrame.HeaderSize, dataLength - SearchFrame.HeaderSize);
                    break;
                default:
                    covered = plain.AsSpan(0, hashStart);
                    break;
            }
            Span<byte> computed = stackalloc byte[16];
            MD5.HashData(covered, computed);
            return computed.SequenceEqual(hash);
        }
    }
}
