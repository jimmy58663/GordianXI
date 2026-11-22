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
        /// <summary>The plain frame from its start to the hash, when <see cref="KeyRule"/> is not <see cref="SearchAnswerKeyRule.None"/>.</summary>
        public byte[]? Content { get; init; }

        /// <summary>True when a key and a hash layout both fit: the answer is verified, only differently from LandSandBoat.</summary>
        public bool Verified => KeyRule != SearchAnswerKeyRule.None && HashRule.HasValue;

        /// <summary>True when a key deciphers a header that answers the request (its answer type, plausible length), hash or not.</summary>
        public bool Plausible => KeyRule != SearchAnswerKeyRule.None && HeaderAnswersRequest;

        /// <summary>One log line: sizes, header fields and the rules that matched; no payload, no key bytes.</summary>
        public string Describe(byte requestType, int requestLength) =>
            $"request type 0x{requestType:X2} ({requestLength} bytes); answer {Length} bytes, length word {LengthWord}, " +
            $"magic {(MagicIsIxff ? "IXFF" : "other")}, seed {(SeedEchoed ? "echoed" : "not echoed")}; " +
            (KeyRule == SearchAnswerKeyRule.None
                ? "no key deciphers a header that answers the request"
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
            bool seedEchoed = length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(length - 4, 4)) == answerKey.Seed;
            var none = new SearchAnswerDiagnosis(length, lengthWord, magic, seedEchoed, SearchAnswerKeyRule.None, null, 0, 0, false);
            if (length < SearchAnswerHeader.Size + 16) return none;

            SearchAnswerDiagnosis? fallback = null;
            foreach (var keyRule in new[] { SearchAnswerKeyRule.SeedAndPlainTail, SearchAnswerKeyRule.SeedOnly, SearchAnswerKeyRule.SeedAndCipherTail })
            {
                uint? tail = keyRule switch
                {
                    SearchAnswerKeyRule.SeedAndPlainTail => answerKey.PayloadTail,
                    SearchAnswerKeyRule.SeedAndCipherTail => answerKey.CipherTail,
                    _ => null,
                };
                using var suite = SearchFrame.NewSuite(answerKey.Seed, tail);

                foreach (bool seedTrailer in new[] { true, false })
                {
                    int hashEnd = seedTrailer ? length - 4 : length;
                    int hashStart = hashEnd - 16;
                    if (hashStart < SearchAnswerHeader.Size) continue;

                    foreach (bool hashEnciphered in new[] { true, false })
                    {
                        var plain = frame.ToArray();
                        int cipherEnd = hashEnciphered ? hashEnd : hashStart;
                        int blocks = (cipherEnd - SearchFrame.HeaderSize) / 8;
                        suite.DecipherWholeBlocks(plain.AsSpan(SearchFrame.HeaderSize, blocks * 8));

                        byte type = plain[0x0B];
                        int dataLength = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(0x08, 2));
                        bool answers = type == ExpectedAnswerType(requestType)
                            && dataLength >= SearchAnswerHeader.Size && dataLength <= length;

                        ReadOnlySpan<byte> hash = hashEnciphered ? plain.AsSpan(hashStart, 16) : frame.Slice(hashStart, 16);
                        foreach (var coverage in new[] { SearchAnswerHashCoverage.PayloadAndPadding, SearchAnswerHashCoverage.DataOnly, SearchAnswerHashCoverage.WithHeader })
                        {
                            if (!HashFits(plain, hash, hashStart, dataLength, coverage)) continue;
                            return new SearchAnswerDiagnosis(length, lengthWord, magic, seedEchoed, keyRule,
                                new SearchAnswerHashRule(seedTrailer, hashEnciphered, coverage), type, dataLength, answers)
                            {
                                Content = plain.AsSpan(0, hashStart).ToArray(),
                            };
                        }

                        // LandSandBoat's block range (seed trailer, hash enciphered) is the one a header-only match reports.
                        if (answers && fallback == null && seedTrailer && hashEnciphered)
                        {
                            fallback = new SearchAnswerDiagnosis(length, lengthWord, magic, seedEchoed, keyRule, null, type, dataLength, true)
                            {
                                Content = plain.AsSpan(0, hashStart).ToArray(),
                            };
                        }
                    }
                }
            }
            return fallback ?? none;
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
