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
        /// An RSA public key for <c>rsa-sha256</c> (RSASSA-PKCS#1 v1.5 over
        /// SHA-256). Only <see cref="RSAParameters.Modulus"/> and
        /// <see cref="RSAParameters.Exponent"/> are used.
        /// </summary>
        public static DeliveryKey Rsa(string keyId, RSAParameters publicKey)
        {
            RequireKeyId(keyId);
            if (publicKey.Modulus == null || publicKey.Modulus.Length == 0
                || publicKey.Exponent == null || publicKey.Exponent.Length == 0)
            {
                throw new ArgumentException("RSA public key needs a non-empty modulus and exponent.", "publicKey");
            }
            var key = new DeliveryKey(keyId, ScriptEnvelopeAlgorithms.RsaSha256);
            key.RsaPublicKey = new RSAParameters
            {
                Modulus = Copy(publicKey.Modulus),
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
