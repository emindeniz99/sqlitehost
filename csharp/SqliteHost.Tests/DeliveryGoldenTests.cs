using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SqliteHost.Delivery;
using SqliteHost.Tests.Fixtures;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// The C# half of the cross-language delivery golden
    /// (docs/proposals/script-delivery.md). Every envelope under
    /// fixtures/delivery/ was produced by the TypeScript signer in
    /// @sqlite-host/authoring; tests/delivery-golden/run.mjs proves those
    /// committed bytes are what the signer emits, and this proves the C#
    /// verifier reaches the outcome the matrix declares for each one.
    ///
    /// Neither half is sufficient alone: the runner could agree with a
    /// signer that emits bytes nothing can verify, and these tests could
    /// agree with a verifier that accepts bytes no signer produces. The
    /// contract is that both hold against the same committed files.
    /// </summary>
    public class DeliveryGoldenTests
    {
        [Theory]
        [MemberData(nameof(Cases))]
        public void GoldenEnvelope_VerifiesToTheDeclaredOutcome(string envelopeFile)
        {
            DeliveryCase testCase = LoadCase(envelopeFile);
            byte[] envelope = File.ReadAllBytes(FixturePaths.Delivery(envelopeFile));

            ScriptEnvelopeVerificationResult result =
                ScriptEnvelopeVerifier.Verify(envelope, TrustedKeys(), testCase.NowUnixMs);

            Assert.Equal(testCase.ExpectedReason, result.Reason);
            if (testCase.ExpectedReason != ScriptEnvelopeFailureReason.None)
            {
                // Fail-closed: a rejected envelope must expose nothing an
                // attacker controls, or callers will log/cache/branch on it.
                Assert.False(result.IsValid);
                Assert.Null(result.Payload);
                Assert.Null(result.ScriptId);
                Assert.Null(result.IssuedAtUnixMs);
                Assert.Null(result.ExpiresAtUnixMs);
                Assert.Null(result.MinApiLevel);
                return;
            }

            Assert.True(result.IsValid);
            Assert.Equal(testCase.ScriptId, result.ScriptId);
            Assert.Equal(testCase.IssuedAt, result.IssuedAtUnixMs);
            Assert.Equal(testCase.ExpiresAt, result.ExpiresAtUnixMs);
            Assert.Equal(testCase.MinApiLevel, result.MinApiLevel);

            // The delivery layer is a wrapper, not a transform: the bytes
            // handed to the app's existing JSON parsing must be the authored
            // script, unchanged. Anything else and the signature would be
            // attesting to something other than what runs.
            Assert.Equal(File.ReadAllBytes(PayloadPath()), result.Payload);
        }

        [Fact]
        public void ValidRsaEnvelope_IsRejectedOneMillisecondAfterExpiry()
        {
            // expiresAt is inclusive, and the boundary is the only place a
            // TTL can be off by one. Asserted here rather than as another
            // fixture because it is the same bytes at two clocks — which is
            // exactly what a caller-supplied `now` makes testable.
            DeliveryCase testCase = LoadCase("valid-rsa.envelope");
            byte[] envelope = File.ReadAllBytes(FixturePaths.Delivery("valid-rsa.envelope"));
            long expiresAt = testCase.ExpiresAt.Value;

            Assert.True(ScriptEnvelopeVerifier.Verify(envelope, TrustedKeys(), expiresAt).IsValid);
            Assert.Equal(
                ScriptEnvelopeFailureReason.Expired,
                ScriptEnvelopeVerifier.Verify(envelope, TrustedKeys(), expiresAt + 1).Reason);
        }

        [Fact]
        public void ValidHmacEnvelope_NeverExpires()
        {
            // An absent expiresAt must mean "no TTL", not "expired at epoch
            // 0". Verified at a clock far past every other fixture's window.
            byte[] envelope = File.ReadAllBytes(FixturePaths.Delivery("valid-hmac.envelope"));
            Assert.True(ScriptEnvelopeVerifier.Verify(envelope, TrustedKeys(), long.MaxValue).IsValid);
        }

        [Fact]
        public void ExpiredEnvelope_StillVerifiesBeforeItsExpiry()
        {
            // Proves expired.envelope fails on freshness alone: the same
            // bytes verify at an earlier clock, so its signature is genuine
            // and `expired` was not a signature failure in disguise.
            DeliveryCase testCase = LoadCase("expired.envelope");
            byte[] envelope = File.ReadAllBytes(FixturePaths.Delivery("expired.envelope"));
            Assert.True(
                ScriptEnvelopeVerifier.Verify(envelope, TrustedKeys(), testCase.IssuedAt).IsValid);
        }

        [Fact]
        public void UnknownKeyEnvelope_VerifiesOnceItsKeyIdIsTrusted()
        {
            // unknown-key.envelope is correctly signed by dev-rsa-1's private
            // key under a kid this build does not ship. Trusting that kid with
            // the same public key makes it verify, which proves the rejection
            // was key *selection* (rotation working) and not a bad signature.
            byte[] envelope = File.ReadAllBytes(FixturePaths.Delivery("unknown-key.envelope"));
            TrustedKeyMaterial rsa = LoadKeyMaterial("dev-rsa-1");
            var keys = new List<DeliveryKey>
            {
                DeliveryKey.Rsa("retired-2024-01", rsa.ModulusBase64, rsa.ExponentBase64)
            };
            Assert.True(ScriptEnvelopeVerifier.Verify(envelope, keys, LoadCase("unknown-key.envelope").NowUnixMs).IsValid);
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (JsonElement element in Expectations().GetProperty("cases").EnumerateArray())
            {
                yield return new object[] { element.GetProperty("envelope").GetString() };
            }
        }

        private static JsonElement Expectations()
        {
            // Re-read per call: JsonDocument is disposable and these are tiny
            // fixture files, so cloning the root keeps the tests independent.
            using (JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(FixturePaths.Delivery("expectations.json"))))
            {
                return document.RootElement.Clone();
            }
        }

        private static string PayloadPath()
        {
            return Path.GetFullPath(FixturePaths.Delivery(
                Expectations().GetProperty("payload").GetString()));
        }

        private static DeliveryCase LoadCase(string envelopeFile)
        {
            foreach (JsonElement element in Expectations().GetProperty("cases").EnumerateArray())
            {
                if (element.GetProperty("envelope").GetString() != envelopeFile)
                {
                    continue;
                }
                var testCase = new DeliveryCase
                {
                    NowUnixMs = element.GetProperty("nowUnixMs").GetInt64(),
                    ExpectedReason = ParseReason(element.GetProperty("outcome").GetString())
                };
                JsonElement expect;
                if (element.TryGetProperty("expect", out expect))
                {
                    testCase.ScriptId = expect.GetProperty("scriptId").GetString();
                    testCase.IssuedAt = expect.GetProperty("issuedAt").GetInt64();
                    testCase.ExpiresAt = OptionalInt64(expect, "expiresAt");
                    testCase.MinApiLevel = (int?)OptionalInt64(expect, "minApiLevel");
                }
                return testCase;
            }
            throw new InvalidOperationException("No delivery expectation for " + envelopeFile);
        }

        private static long? OptionalInt64(JsonElement parent, string name)
        {
            JsonElement value = parent.GetProperty(name);
            return value.ValueKind == JsonValueKind.Null ? (long?)null : value.GetInt64();
        }

        /// <summary>
        /// The wire spellings from expectations.json. Kept as an explicit
        /// map so a renamed enum member cannot silently start matching the
        /// wrong fixture — an unknown spelling throws.
        /// </summary>
        private static ScriptEnvelopeFailureReason ParseReason(string outcome)
        {
            switch (outcome)
            {
                case "ok": return ScriptEnvelopeFailureReason.None;
                case "malformed": return ScriptEnvelopeFailureReason.Malformed;
                case "unsupported-version": return ScriptEnvelopeFailureReason.UnsupportedVersion;
                case "unknown-key": return ScriptEnvelopeFailureReason.UnknownKey;
                case "bad-signature": return ScriptEnvelopeFailureReason.BadSignature;
                case "expired": return ScriptEnvelopeFailureReason.Expired;
                default: throw new InvalidOperationException("Unknown delivery outcome: " + outcome);
            }
        }

        /// <summary>The key set a client would ship, read from fixtures/delivery/dev-keys.json.</summary>
        internal static List<DeliveryKey> TrustedKeys()
        {
            var keys = new List<DeliveryKey>();
            using (JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(FixturePaths.Delivery(Expectations().GetProperty("keys").GetString()))))
            {
                foreach (JsonElement key in document.RootElement.GetProperty("trusted").EnumerateArray())
                {
                    string keyId = key.GetProperty("keyId").GetString();
                    string alg = key.GetProperty("alg").GetString();
                    if (alg == ScriptEnvelopeAlgorithms.RsaSha256)
                    {
                        keys.Add(DeliveryKey.Rsa(
                            keyId,
                            key.GetProperty("modulusBase64").GetString(),
                            key.GetProperty("exponentBase64").GetString()));
                    }
                    else
                    {
                        keys.Add(DeliveryKey.Hmac(
                            keyId, Convert.FromBase64String(key.GetProperty("secretBase64").GetString())));
                    }
                }
            }
            return keys;
        }

        internal static TrustedKeyMaterial LoadKeyMaterial(string keyId)
        {
            using (JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(FixturePaths.Delivery(Expectations().GetProperty("keys").GetString()))))
            {
                foreach (JsonElement key in document.RootElement.GetProperty("trusted").EnumerateArray())
                {
                    if (key.GetProperty("keyId").GetString() != keyId)
                    {
                        continue;
                    }
                    return new TrustedKeyMaterial
                    {
                        ModulusBase64 = ReadOptionalString(key, "modulusBase64"),
                        ExponentBase64 = ReadOptionalString(key, "exponentBase64"),
                        SecretBase64 = ReadOptionalString(key, "secretBase64")
                    };
                }
            }
            throw new InvalidOperationException("No trusted key material for " + keyId);
        }

        private static string ReadOptionalString(JsonElement parent, string name)
        {
            JsonElement value;
            return parent.TryGetProperty(name, out value) ? value.GetString() : null;
        }

        internal sealed class TrustedKeyMaterial
        {
            public string ModulusBase64;
            public string ExponentBase64;
            public string SecretBase64;
        }

        private sealed class DeliveryCase
        {
            public long NowUnixMs;
            public ScriptEnvelopeFailureReason ExpectedReason;
            public string ScriptId;
            public long IssuedAt;
            public long? ExpiresAt;
            public int? MinApiLevel;
        }
    }
}
