using System;
using System.Security.Cryptography;

namespace SqliteHost.Delivery
{
    /// <summary>
    /// One trusted signing key: the pair (<see cref="KeyId"/>,
    /// <see cref="Algorithm"/>) plus its material. Apps ship a list of
    /// these and hand it to <see cref="ScriptEnvelopeVerifier.Verify"/>;
    /// rotation is additive (trust {old,new}, switch signers, drop old
    /// in a later build) and revocation is an app update, because this
    /// package has no transport to check a revocation list with.
    /// See docs/proposals/script-delivery.md (Key model).
    /// </summary>
    public sealed class DeliveryKey
    {
        private DeliveryKey(string keyId, string algorithm)
        {
            KeyId = keyId;
            Algorithm = algorithm;
        }

        /// <summary>Matched against the envelope's <c>kid</c> header.</summary>
        public string KeyId { get; private set; }

        /// <summary>
        /// <see cref="ScriptEnvelopeAlgorithms.RsaSha256"/> or
        /// <see cref="ScriptEnvelopeAlgorithms.HmacSha256"/>. The verifier
        /// requires this to equal the envelope's <c>alg</c>, so a key can
        /// never be pressed into service under a different algorithm.
        /// </summary>
        public string Algorithm { get; private set; }

        internal RSAParameters RsaPublicKey;
        internal byte[] HmacSecret;

        /// <summary>
        /// Modulus floor for <see cref="Rsa(string, RSAParameters)"/>: 2048
        /// bits, the size <c>generateDeliveryKeyPair()</c> mints.
        /// </summary>
        private const int MinimumRsaModulusBytes = 256;

        /// <summary>
        /// An RSA public key for <c>rsa-sha256</c> (RSASSA-PKCS#1 v1.5 over
        /// SHA-256). Only <see cref="RSAParameters.Modulus"/> and
        /// <see cref="RSAParameters.Exponent"/> are used. The modulus must
        /// be at least 2048 bits and the exponent must be odd and greater
        /// than 1.
        /// </summary>
        public static DeliveryKey Rsa(string keyId, RSAParameters publicKey)
        {
            RequireKeyId(keyId);
            if (publicKey.Modulus == null || publicKey.Modulus.Length == 0
                || publicKey.Exponent == null || publicKey.Exponent.Length == 0)
            {
                throw new ArgumentException("RSA public key needs a non-empty modulus and exponent.", "publicKey");
            }
            // Leading zeros are legal padding in a big-endian integer, and
            // real producers emit them: Java's BigInteger.toByteArray()
            // prepends a sign byte, and HSM/KMS and JWK-adjacent tooling
            // emit fixed-width fields. So the floor is measured on the
            // SIGNIFICANT bytes, and the padding is dropped rather than
            // rejected — .NET wants the modulus at its true width anyway.
            byte[] modulus = TrimLeadingZeros(publicKey.Modulus);
            // A too-small modulus is the misconfiguration that fails OPEN:
            // verification keeps succeeding while the private key is within
            // reach of factoring, so an attacker mints envelopes the app
            // accepts. Rejected here for the same reason as a mistyped id.
            // Comparing the ENCODED length instead would wave through a
            // 1024-bit modulus left-padded to 256 bytes, which is exactly
            // the shape the misconfiguration arrives in.
            if (modulus.Length < MinimumRsaModulusBytes)
            {
                throw new ArgumentException(
                    "RSA public key must be at least 2048 bits (a " + MinimumRsaModulusBytes
                    + "-byte modulus); this one is " + (modulus.Length * 8) + " bits.",
                    "publicKey");
            }
            // A degenerate exponent fails OPEN the same way. e=1 makes RSA
            // the identity, so m^e mod n is the padded digest itself and
            // anyone forges a signature without the private key; an even e
            // has no inverse mod phi(n), so it is not an RSA exponent at
            // all. Both are typos or tampering, never a key
            // generateDeliveryKeyPair() minted.
            if (!IsUsableRsaExponent(publicKey.Exponent))
            {
                throw new ArgumentException(
                    "RSA public key exponent must be odd and greater than 1.",
                    "publicKey");
            }
            var key = new DeliveryKey(keyId, ScriptEnvelopeAlgorithms.RsaSha256);
            key.RsaPublicKey = new RSAParameters
            {
                Modulus = modulus,
                Exponent = Copy(publicKey.Exponent)
            };
            return key;
        }

        /// <summary>
        /// An RSA public key as raw modulus/exponent in standard base64 —
        /// the form <c>generateDeliveryKeyPair()</c> emits. netstandard2.0
        /// has no <c>ImportSubjectPublicKeyInfo</c>, so SPKI/PEM would mean
        /// hand-rolling a DER parser in a zero-dependency package.
        /// </summary>
        public static DeliveryKey Rsa(string keyId, string modulusBase64, string exponentBase64)
        {
            return Rsa(keyId, new RSAParameters
            {
                Modulus = DecodeBase64(modulusBase64, "modulusBase64"),
                Exponent = DecodeBase64(exponentBase64, "exponentBase64")
            });
        }

        /// <summary>
        /// A shared secret for <c>hmac-sha256</c>. WEAKER BY CONSTRUCTION:
        /// the verifying client holds the same bytes the signer holds, so
        /// anyone who unpacks the app can extract it and mint envelopes the
        /// app will accept. Development and server-to-server only — never
        /// ship one to players.
        /// </summary>
        public static DeliveryKey Hmac(string keyId, byte[] secret)
        {
            RequireKeyId(keyId);
            if (secret == null || secret.Length == 0)
            {
                throw new ArgumentException("HMAC secret must be non-empty.", "secret");
            }
            var key = new DeliveryKey(keyId, ScriptEnvelopeAlgorithms.HmacSha256);
            key.HmacSecret = Copy(secret);
            return key;
        }

        // Key construction is the app's own configuration, not attacker
        // input, so it throws: a mistyped trusted key must fail loudly at
        // startup rather than silently degrade to "nothing verifies".
        // Verify() itself never throws.
        private static void RequireKeyId(string keyId)
        {
            if (!ScriptEnvelopeVerifier.IsValidId(keyId))
            {
                throw new ArgumentException(
                    "keyId must be 1-128 characters from [A-Za-z0-9._:-].", "keyId");
            }
        }

        /// <summary>
        /// Big-endian public exponent that is odd and greater than 1 — the
        /// two properties every real RSA exponent has (65537 = AQAB, the one
        /// generateDeliveryKeyPair() emits, and 3 both pass). No upper bound
        /// and no primality test: this rejects degenerate values, it does not
        /// audit key quality.
        /// </summary>
        private static bool IsUsableRsaExponent(byte[] exponent)
        {
            int last = exponent.Length - 1;
            if ((exponent[last] & 1) == 0)
            {
                return false;
            }
            if (exponent[last] > 1)
            {
                return true;
            }
            // Trailing byte is exactly 1: greater than 1 only if some
            // higher-order byte carries a value (leading zeros are legal
            // padding, so scan rather than compare lengths).
            for (int i = 0; i < last; i++)
            {
                if (exponent[i] != 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The significant bytes of a big-endian unsigned integer, always as
        /// a fresh array (key material is copied out of caller-owned buffers
        /// by construction). An all-zero input trims to length 0, which the
        /// modulus floor then rejects.
        /// </summary>
        private static byte[] TrimLeadingZeros(byte[] value)
        {
            int start = 0;
            while (start < value.Length && value[start] == 0)
            {
                start++;
            }
            var trimmed = new byte[value.Length - start];
            Buffer.BlockCopy(value, start, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        private static byte[] DecodeBase64(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Value must be non-empty base64.", parameterName);
            }
            try
            {
                return Convert.FromBase64String(value);
            }
            catch (FormatException error)
            {
                throw new ArgumentException("Value is not valid base64.", parameterName, error);
            }
        }

        private static byte[] Copy(byte[] source)
        {
            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }
}
