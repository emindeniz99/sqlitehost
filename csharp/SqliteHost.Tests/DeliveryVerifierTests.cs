using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using SqliteHost.Delivery;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Parser and policy tests for <see cref="ScriptEnvelopeVerifier"/>
    /// (docs/proposals/script-delivery.md). The golden fixtures pin the
    /// cross-language contract; these pin the behaviours that only show up
    /// under bytes a signer would never emit — which is precisely the input
    /// this code exists to survive.
    ///
    /// Envelopes here are built and signed in-process with HMAC so a test
    /// can construct exactly the malformation it is about.
    /// </summary>
    public class DeliveryVerifierTests
    {
        private static readonly byte[] Secret = Encoding.ASCII.GetBytes("delivery-unit-test-secret");
        private const string KeyId = "unit-hmac";
        private const long IssuedAt = 1785283200000L;
        private const long Now = 1785283200000L;

        private static List<DeliveryKey> Keys()
        {
            return new List<DeliveryKey> { DeliveryKey.Hmac(KeyId, Secret) };
        }

        /// <summary>
        /// Builds a signed envelope, allowing every header value to be set
        /// as a raw string so tests can inject spellings the TS signer
        /// forbids. <paramref name="declaredPayloadLength"/> overrides the
        /// real length when a test needs the header to lie.
        /// </summary>
        private static byte[] Build(
            byte[] payload,
            string magicLine = "sqlite-host-delivery/1",
            string alg = "hmac-sha256",
            string kid = KeyId,
            string scriptId = "unit-script",
            string issuedAt = "1785283200000",
            string expiresAt = "",
            string minApiLevel = "",
            string declaredPayloadLength = null,
            string[] headerOrderOverride = null)
        {
            string[] header = headerOrderOverride ?? new[]
            {
                "alg=" + alg,
                "kid=" + kid,
                "scriptId=" + scriptId,
                "issuedAt=" + issuedAt,
                "expiresAt=" + expiresAt,
                "minApiLevel=" + minApiLevel,
                "payloadLength=" + (declaredPayloadLength ?? payload.Length.ToString())
            };
            string headerText = magicLine + "\n" + string.Join("\n", header) + "\n\n";

            var signed = new List<byte>();
            signed.AddRange(Encoding.ASCII.GetBytes(headerText));
            signed.AddRange(payload);
            signed.Add((byte)'\n');
            byte[] signedBytes = signed.ToArray();

            string signature;
            using (var hmac = new HMACSHA256(Secret))
            {
                signature = Convert.ToBase64String(hmac.ComputeHash(signedBytes));
            }
            var envelope = new List<byte>(signedBytes);
            envelope.AddRange(Encoding.ASCII.GetBytes("sig=" + signature + "\n"));
            return envelope.ToArray();
        }

        private static byte[] Payload(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        [Fact]
        public void WellFormedEnvelope_Verifies()
        {
            ScriptEnvelopeVerificationResult result =
                ScriptEnvelopeVerifier.Verify(Build(Payload("{\"a\":1}")), Keys(), Now);
            Assert.True(result.IsValid);
            Assert.Equal("unit-script", result.ScriptId);
            Assert.Equal(IssuedAt, result.IssuedAtUnixMs);
            Assert.Null(result.ExpiresAtUnixMs);
            Assert.Null(result.MinApiLevel);
            Assert.Equal(Payload("{\"a\":1}"), result.Payload);
        }

        [Fact]
        public void PayloadContainingAnEnvelopeTail_IsDelimitedByPayloadLength()
        {
            // The payload is opaque bytes and may legally contain "\nsig=" —
            // a script fixture with an envelope pasted into a string literal
            // is enough. A verifier that found the signature by scanning for
            // the last line would truncate the payload here and then verify a
            // signature over a shorter region than was actually delivered.
            // payloadLength in the signed header is what prevents that.
            byte[] payload = Payload("{\"note\":\"ends with\\n\"}\nsig=AAAA\n");
            ScriptEnvelopeVerificationResult result =
                ScriptEnvelopeVerifier.Verify(Build(payload), Keys(), Now);
            Assert.True(result.IsValid);
            Assert.Equal(payload, result.Payload);
        }

        [Fact]
        public void SignatureIsCheckedBeforeExpiry()
        {
            // An envelope that is BOTH expired and forged must report
            // bad-signature. Until the signature verifies, expiresAt is just
            // an integer an attacker typed: reporting `expired` would mean
            // the library acted on an unverified header field, and would hand
            // an attacker an oracle for the client's clock.
            byte[] envelope = Build(Payload("{}"), expiresAt: (IssuedAt - 1).ToString());
            envelope[envelope.Length - 6] ^= 0x01; // corrupt the signature
            Assert.Equal(
                ScriptEnvelopeFailureReason.BadSignature,
                ScriptEnvelopeVerifier.Verify(envelope, Keys(), Now).Reason);
        }

        [Theory]
        [InlineData("lead")]
        [InlineData("mid")]
        [InlineData("trail")]
        public void SignatureWithInjectedWhitespace_IsMalformed(string where)
        {
            // Convert.FromBase64String silently ignores embedded ASCII
            // whitespace, so "sig= <b64>", "<b64 wi th>" and "<b64>  " would
            // otherwise be extra byte spellings of one valid signature — the
            // spec requires exactly one. A space injected anywhere in the sig
            // line's base64 must fail, not verify.
            byte[] envelope = Build(Payload("{}"));
            int sigStart = IndexOf(envelope, Encoding.ASCII.GetBytes("sig=")) + 4;
            int insertAt = where == "trail" ? envelope.Length - 1 // before the final '\n'
                : where == "mid" ? sigStart + 4
                : sigStart;                                       // leading
            var mangled = new List<byte>(envelope);
            mangled.Insert(insertAt, (byte)' ');
            var result = ScriptEnvelopeVerifier.Verify(mangled.ToArray(), Keys(), Now);
            Assert.False(result.IsValid);
            Assert.Equal(ScriptEnvelopeFailureReason.Malformed, result.Reason);
        }

        [Fact]
        public void KeysAreSelectedByKeyIdAndAlgorithmTogether()
        {
            // Same kid published under both algorithms. Selecting on kid
            // alone would let a forged alg header pick the wrong material.
            byte[] envelope = Build(Payload("{}"));
            var keys = new List<DeliveryKey>
            {
                DeliveryKey.Rsa(KeyId, Convert.ToBase64String(Modulus(256)), Convert.ToBase64String(new byte[] { 1, 0, 1 })),
                DeliveryKey.Hmac(KeyId, Secret)
            };
            Assert.True(ScriptEnvelopeVerifier.Verify(envelope, keys, Now).IsValid);
        }

        [Fact]
        public void NoTrustedKeys_IsUnknownKeyRatherThanAnAccident()
        {
            // An app that ships an empty (or null) key list must verify
            // nothing. A "no keys configured means accept" fallback is the
            // classic way a trust layer becomes decorative.
            byte[] envelope = Build(Payload("{}"));
            Assert.Equal(
                ScriptEnvelopeFailureReason.UnknownKey,
                ScriptEnvelopeVerifier.Verify(envelope, new List<DeliveryKey>(), Now).Reason);
            Assert.Equal(
                ScriptEnvelopeFailureReason.UnknownKey,
                ScriptEnvelopeVerifier.Verify(envelope, null, Now).Reason);
        }

        [Fact]
        public void WrongSecret_IsBadSignature()
        {
            byte[] envelope = Build(Payload("{}"));
            var keys = new List<DeliveryKey> { DeliveryKey.Hmac(KeyId, Encoding.ASCII.GetBytes("other-secret")) };
            Assert.Equal(
                ScriptEnvelopeFailureReason.BadSignature,
                ScriptEnvelopeVerifier.Verify(envelope, keys, Now).Reason);
        }

        [Theory]
        // Reordered header: the fixed field order is what removes any need
        // for canonicalization, so it has to be enforced, not tolerated.
        [InlineData("kid=unit-hmac|alg=hmac-sha256|scriptId=unit-script|issuedAt=1785283200000|expiresAt=|minApiLevel=|payloadLength=2")]
        // Missing field: absent means malformed, never a default value.
        [InlineData("alg=hmac-sha256|kid=unit-hmac|scriptId=unit-script|issuedAt=1785283200000|expiresAt=|payloadLength=2")]
        // Extra field: an unknown header line must not be skipped over.
        [InlineData("alg=hmac-sha256|kid=unit-hmac|scriptId=unit-script|issuedAt=1785283200000|expiresAt=|minApiLevel=|cohort=beta|payloadLength=2")]
        public void HeaderShapeDeviations_AreMalformed(string pipeSeparatedHeader)
        {
            string[] header = pipeSeparatedHeader.Split('|');
            Assert.Equal(
                ScriptEnvelopeFailureReason.Malformed,
                ScriptEnvelopeVerifier.Verify(Build(Payload("{}"), headerOrderOverride: header), Keys(), Now).Reason);
        }

        [Theory]
        [InlineData("01785283200000")] // leading zero: one integer, one spelling
        [InlineData("-1")]             // signed: the format has no negative timestamps
        [InlineData("1785283200000.0")]
        [InlineData("1e12")]
        [InlineData("")]               // issuedAt is not optional
        [InlineData("99999999999999999999")]
        public void NonCanonicalIssuedAt_IsMalformed(string issuedAt)
        {
            // issuedAt is the input to the app's rollback defence. Two
            // spellings of one instant, or a value that silently truncates,
            // would make "strictly greater than last seen" unreliable.
            Assert.Equal(
                ScriptEnvelopeFailureReason.Malformed,
                ScriptEnvelopeVerifier.Verify(Build(Payload("{}"), issuedAt: issuedAt), Keys(), Now).Reason);
        }

        [Theory]
        [InlineData("1")]   // shorter than the real payload
        [InlineData("99")]  // past the end of the envelope
        [InlineData("0")]
        public void LyingPayloadLength_IsMalformed(string declared)
        {
            // payloadLength is inside the signed region, so a mismatch is
            // either corruption or an attempt to make the verifier hash a
            // different range than was delivered. Both must fail before any
            // crypto runs, and neither may read outside the array.
            Assert.Equal(
                ScriptEnvelopeFailureReason.Malformed,
                ScriptEnvelopeVerifier.Verify(
                    Build(Payload("{\"a\":1}"), declaredPayloadLength: declared), Keys(), Now).Reason);
        }

        [Fact]
        public void NonAsciiHeaderByte_IsMalformed()
        {
            // Header bytes are restricted to printable US-ASCII so that
            // "what the signer wrote" and "what the verifier read" cannot
            // diverge through an encoding.
            byte[] envelope = Build(Payload("{}"), scriptId: "unit-script");
            int at = IndexOf(envelope, Encoding.ASCII.GetBytes("scriptId=unit"));
            envelope[at + 9] = 0xc3;
            Assert.Equal(
                ScriptEnvelopeFailureReason.Malformed,
                ScriptEnvelopeVerifier.Verify(envelope, Keys(), Now).Reason);
        }

        [Fact]
        public void UnknownAlgorithm_IsUnsupportedVersionNotMalformed()
        {
            // The app's reaction differs: unsupported-version means "ship an
            // app update", malformed means "the download is broken, retry".
            Assert.Equal(
                ScriptEnvelopeFailureReason.UnsupportedVersion,
                ScriptEnvelopeVerifier.Verify(Build(Payload("{}"), alg: "rsa-pss-sha256"), Keys(), Now).Reason);
        }

        [Fact]
        public void ForeignBytes_AreMalformedNotUnsupportedVersion()
        {
            // A CDN error page or a redirect body is not a v1 envelope with
            // a version we lack — it is not an envelope at all.
            Assert.Equal(
                ScriptEnvelopeFailureReason.Malformed,
                ScriptEnvelopeVerifier.Verify(
                    Encoding.ASCII.GetBytes("<html><body>403 Forbidden</body></html>"), Keys(), Now).Reason);
        }

        [Fact]
        public void VerifyNeverThrows_ForAnyPrefixOfAValidEnvelope()
        {
            // Truncation is the single most likely real-world corruption
            // (a dropped connection), and it walks the parser through every
            // partial state. This code is the first thing in the process to
            // touch bytes from the network, so a thrown exception here is a
            // denial-of-service primitive, not a stack trace.
            byte[] envelope = Build(Payload("{\"a\":1,\"b\":[2,3]}"), expiresAt: "1785369600000", minApiLevel: "3");
            for (int length = 0; length <= envelope.Length; length++)
            {
                var prefix = new byte[length];
                Buffer.BlockCopy(envelope, 0, prefix, 0, length);
                ScriptEnvelopeVerificationResult result = ScriptEnvelopeVerifier.Verify(prefix, Keys(), Now);
                Assert.True(
                    length == envelope.Length ? result.IsValid : !result.IsValid,
                    "prefix of length " + length + " reported " + result.Reason);
            }
        }

        [Fact]
        public void VerifyNeverThrows_ForSingleByteCorruptionAnywhere()
        {
            // Every byte position, flipped. Nothing may throw and nothing
            // may verify: any change at all falls inside either the framing
            // rules or the signed region.
            byte[] envelope = Build(Payload("{\"a\":1}"), expiresAt: "1785369600000", minApiLevel: "3");
            for (int i = 0; i < envelope.Length; i++)
            {
                var corrupted = (byte[])envelope.Clone();
                corrupted[i] ^= 0x01;
                Assert.False(
                    ScriptEnvelopeVerifier.Verify(corrupted, Keys(), Now).IsValid,
                    "corrupting byte " + i + " still verified");
            }
        }

        [Fact]
        public void VerifyNeverThrows_ForNullOrEmptyInput()
        {
            Assert.Equal(ScriptEnvelopeFailureReason.Malformed, ScriptEnvelopeVerifier.Verify(null, Keys(), Now).Reason);
            Assert.Equal(ScriptEnvelopeFailureReason.Malformed, ScriptEnvelopeVerifier.Verify(new byte[0], Keys(), Now).Reason);
        }

        [Theory]
        [InlineData("")]
        [InlineData("has space")]
        [InlineData("has\nnewline")]
        [InlineData("has/slash")]
        public void MisconfiguredKeyId_ThrowsAtConstruction(string keyId)
        {
            // Key material is the app's OWN configuration, not attacker
            // input, so it fails loud at startup. The alternative — a key
            // that silently never matches — looks exactly like a working
            // trust layer right up until nothing verifies in production.
            Assert.Throws<ArgumentException>(() => DeliveryKey.Hmac(keyId, Secret));
        }

        [Fact]
        public void EmptyKeyMaterial_ThrowsAtConstruction()
        {
            Assert.Throws<ArgumentException>(() => DeliveryKey.Hmac(KeyId, new byte[0]));
            Assert.Throws<ArgumentException>(() => DeliveryKey.Rsa(KeyId, "", "AQAB"));
            Assert.Throws<ArgumentException>(() => DeliveryKey.Rsa(KeyId, "not base64!", "AQAB"));
        }

        [Theory]
        [InlineData(64)]  // 512-bit
        [InlineData(128)] // 1024-bit
        [InlineData(255)] // one byte short of the floor
        public void RsaKeyBelowTheModulusFloor_ThrowsAtConstruction(int modulusBytes)
        {
            // The one key misconfiguration that fails OPEN: a factorable
            // modulus keeps verifying happily, so the app looks fine while
            // anyone who recovers the private key mints envelopes it
            // accepts. Every other bad key here fails closed, which is why
            // this one has to be rejected at construction.
            Assert.Throws<ArgumentException>(
                () => DeliveryKey.Rsa(KeyId, Convert.ToBase64String(Modulus(modulusBytes)), "AQAB"));
            Assert.Throws<ArgumentException>(() => DeliveryKey.Rsa(
                KeyId,
                new RSAParameters { Modulus = Modulus(modulusBytes), Exponent = new byte[] { 1, 0, 1 } }));
        }

        [Theory]
        [InlineData("AQ==")]      // e = 1
        [InlineData("AAAAAQ==")]  // e = 1 again, written with leading zeros
        [InlineData("AQAA")]      // e = 65536, even
        public void RsaKeyWithADegenerateExponent_ThrowsAtConstruction(string exponentBase64)
        {
            // The exponent fails OPEN exactly like a short modulus, and the
            // construction check was the only place looking: e=1 makes RSA
            // the identity, so the "signature" is the padded digest and
            // anyone mints envelopes the app accepts without ever seeing a
            // private key. An even e is not an RSA exponent at all (no
            // inverse mod phi(n)) and can only be a typo or tampering. The
            // leading-zero case is the same value written differently, which
            // a length comparison would have waved through.
            Assert.Throws<ArgumentException>(
                () => DeliveryKey.Rsa(KeyId, Convert.ToBase64String(Modulus(256)), exponentBase64));
            Assert.Throws<ArgumentException>(() => DeliveryKey.Rsa(
                KeyId,
                new RSAParameters
                {
                    Modulus = Modulus(256),
                    Exponent = Convert.FromBase64String(exponentBase64)
                }));
        }

        [Theory]
        [InlineData("AQAB")]  // e = 65537, what generateDeliveryKeyPair() emits
        [InlineData("Aw==")]  // e = 3, small but legitimate
        public void RsaKeyWithAUsableExponent_IsAccepted(string exponentBase64)
        {
            // Guards against over-tightening: the check rejects degenerate
            // values, it does not audit key quality, so it must not start
            // demanding one blessed exponent.
            var key = DeliveryKey.Rsa(KeyId, Convert.ToBase64String(Modulus(256)), exponentBase64);
            Assert.Equal(ScriptEnvelopeAlgorithms.RsaSha256, key.Algorithm);
        }

        [Fact]
        public void RsaKeyAtTheModulusFloor_IsAccepted()
        {
            // 2048 bits is the floor, not a target: generateDeliveryKeyPair()
            // mints exactly this size, so the on-ramp must stay usable.
            var key = DeliveryKey.Rsa(KeyId, Convert.ToBase64String(Modulus(256)), "AQAB");
            Assert.Equal(ScriptEnvelopeAlgorithms.RsaSha256, key.Algorithm);
        }

        [Fact]
        public void RsaModulusWithALeadingZeroByte_IsAcceptedAtItsTrueBitLength()
        {
            // Java's BigInteger.toByteArray() prepends a 0x00 sign byte to a
            // 2048-bit modulus, so a legitimate key from the Java side of
            // this project arrives as 257 bytes. Rejecting leading-zero
            // padding would lock out a producer we ship; the floor has to
            // count significant bytes, not array length.
            var padded = new byte[257];
            Buffer.BlockCopy(Modulus(256), 0, padded, 1, 256);
            var key = DeliveryKey.Rsa(KeyId, Convert.ToBase64String(padded), "AQAB");
            Assert.Equal(ScriptEnvelopeAlgorithms.RsaSha256, key.Algorithm);
        }

        [Theory]
        [InlineData(1)]   // 2040 bits dressed as 256 bytes
        [InlineData(128)] // a 1024-bit modulus left-padded to the floor
        public void RsaModulusZeroPaddedUpToTheFloor_ThrowsAtConstruction(int leadingZeros)
        {
            // The floor exists to catch the one misconfiguration that fails
            // OPEN. Counting encoded bytes defeats it with the most likely
            // shape of that misconfiguration: HSM/KMS and JWK-adjacent
            // tooling emit fixed-width zero-padded moduli, so a 1024-bit key
            // arrives as 256 bytes and .NET verifies signatures made by the
            // factorable private half.
            var padded = new byte[256];
            Buffer.BlockCopy(Modulus(256 - leadingZeros), 0, padded, leadingZeros, 256 - leadingZeros);
            Assert.Throws<ArgumentException>(
                () => DeliveryKey.Rsa(KeyId, Convert.ToBase64String(padded), "AQAB"));
        }

        [Fact]
        public void ZeroPaddedModulusVerifiesARealRsaSignature()
        {
            // The padded form is not merely tolerated at construction: the
            // key it produces must verify the same bytes the unpadded form
            // does, or "accepted" would only mean "accepted and then broken".
            using (var rsa = RSA.Create(2048))
            {
                RSAParameters parameters = rsa.ExportParameters(false);
                var padded = new byte[parameters.Modulus.Length + 1];
                Buffer.BlockCopy(parameters.Modulus, 0, padded, 1, parameters.Modulus.Length);

                byte[] envelope = BuildRsaSigned(rsa, Payload("{}"));
                var keys = new List<DeliveryKey>
                {
                    DeliveryKey.Rsa(
                        KeyId,
                        Convert.ToBase64String(padded),
                        Convert.ToBase64String(parameters.Exponent))
                };
                Assert.True(ScriptEnvelopeVerifier.Verify(envelope, keys, Now).IsValid);
            }
        }

        /// <summary>
        /// The same envelope <see cref="Build"/> makes, signed with a real
        /// RSA private key instead of the HMAC secret.
        /// </summary>
        private static byte[] BuildRsaSigned(RSA privateKey, byte[] payload)
        {
            string headerText = "sqlite-host-delivery/1\n"
                + "alg=rsa-sha256\n"
                + "kid=" + KeyId + "\n"
                + "scriptId=unit-script\n"
                + "issuedAt=" + IssuedAt + "\n"
                + "expiresAt=\n"
                + "minApiLevel=\n"
                + "payloadLength=" + payload.Length + "\n\n";
            var signed = new List<byte>();
            signed.AddRange(Encoding.ASCII.GetBytes(headerText));
            signed.AddRange(payload);
            signed.Add((byte)'\n');
            byte[] signedBytes = signed.ToArray();
            string signature = Convert.ToBase64String(privateKey.SignData(
                signedBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var envelope = new List<byte>(signedBytes);
            envelope.AddRange(Encoding.ASCII.GetBytes("sig=" + signature + "\n"));
            return envelope.ToArray();
        }

        /// <summary>
        /// A stand-in modulus of exactly <paramref name="byteLength"/> bytes,
        /// high bit set so its byte length is also its true bit length.
        /// </summary>
        private static byte[] Modulus(int byteLength)
        {
            var modulus = new byte[byteLength];
            modulus[0] = 0x80;
            modulus[byteLength - 1] = 0x01;
            return modulus;
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) { return i; }
            }
            throw new InvalidOperationException("needle not found in envelope");
        }
    }
}
