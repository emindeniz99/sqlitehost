using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace SqliteHost.Delivery
{
    /// <summary>
    /// Verifies Script Delivery v1 envelopes and hands back the payload
    /// bytes. This is the whole package: the app supplies the bytes (over
    /// whatever transport it already uses) and the caller-supplied
    /// <c>now</c>; this supplies trust and freshness.
    ///
    /// The envelope is line-framed ASCII with a verbatim payload, so the
    /// signed region is a contiguous <em>prefix of the received bytes</em>
    /// — nothing is ever re-serialized, and there is no canonicalization
    /// step whose bugs could widen what a signature covers.
    /// The normative format lives in docs/proposals/script-delivery.md.
    /// </summary>
    public static class ScriptEnvelopeVerifier
    {
        /// <summary>The only <c>deliveryVersion</c> this build implements.</summary>
        public const int SupportedDeliveryVersion = 1;

        private const string MagicPrefix = "sqlite-host-delivery/";
        private const string SignaturePrefix = "sig=";
        private const int MaxIdLength = 128;
        private const byte Newline = 0x0a;

        // Fixed order is the reason no canonicalization is needed: there
        // is no map to sort and no optional field to omit. A reordered or
        // missing key is Malformed, never a default.
        private static readonly string[] HeaderKeys =
        {
            "alg=", "kid=", "scriptId=", "issuedAt=", "expiresAt=", "minApiLevel=", "payloadLength="
        };

        private const int IndexAlg = 0;
        private const int IndexKid = 1;
        private const int IndexScriptId = 2;
        private const int IndexIssuedAt = 3;
        private const int IndexExpiresAt = 4;
        private const int IndexMinApiLevel = 5;
        private const int IndexPayloadLength = 6;

        /// <summary>
        /// Verifies <paramref name="envelope"/> against
        /// <paramref name="trustedKeys"/> at the caller-supplied
        /// <paramref name="nowUnixMs"/>.
        ///
        /// Never throws, for any input — arbitrary bytes, a truncated
        /// download, null, an empty key list. This is the first code in
        /// the process to touch bytes from the network, so a thrown
        /// exception would be a denial-of-service primitive.
        ///
        /// No wall clock is read: <paramref name="nowUnixMs"/> comes from
        /// the caller, which keeps the library deterministic, makes replay
        /// harnesses possible, and leaves clock-trust policy with the app.
        /// </summary>
        /// <param name="envelope">The envelope bytes exactly as received.</param>
        /// <param name="trustedKeys">Keys this build trusts; selection is by (kid, alg).</param>
        /// <param name="nowUnixMs">Unix milliseconds used only for the <c>expiresAt</c> check.</param>
        public static ScriptEnvelopeVerificationResult Verify(
            byte[] envelope, IList<DeliveryKey> trustedKeys, long nowUnixMs)
        {
            if (envelope == null || envelope.Length == 0)
            {
                return Fail(ScriptEnvelopeFailureReason.Malformed);
            }

            int offset = 0;
            string line;

            // Line 0: magic + delivery version.
            if (!TryReadAsciiLine(envelope, ref offset, out line)
                || !line.StartsWith(MagicPrefix, StringComparison.Ordinal))
            {
                return Fail(ScriptEnvelopeFailureReason.Malformed);
            }
            int deliveryVersion;
            if (!TryParseUInt32(line.Substring(MagicPrefix.Length), out deliveryVersion))
            {
                return Fail(ScriptEnvelopeFailureReason.Malformed);
            }
            if (deliveryVersion != SupportedDeliveryVersion)
            {
                return Fail(ScriptEnvelopeFailureReason.UnsupportedVersion);
            }

            // Lines 1-7: the seven header fields, in order.
            var values = new string[HeaderKeys.Length];
            for (int i = 0; i < HeaderKeys.Length; i++)
            {
                if (!TryReadAsciiLine(envelope, ref offset, out line)
                    || !line.StartsWith(HeaderKeys[i], StringComparison.Ordinal))
                {
                    return Fail(ScriptEnvelopeFailureReason.Malformed);
                }
                values[i] = line.Substring(HeaderKeys[i].Length);
            }

            // Line 8: empty separator.
            if (!TryReadAsciiLine(envelope, ref offset, out line) || line.Length != 0)
            {
                return Fail(ScriptEnvelopeFailureReason.Malformed);
            }

            int payloadStart = offset;
            int payloadLength;
            if (!TryParseUInt32(values[IndexPayloadLength], out payloadLength)
                || payloadLength > envelope.Length - payloadStart - 1
                || envelope[payloadStart + payloadLength] != Newline)
            {
                return Fail(ScriptEnvelopeFailureReason.Malformed);
            }

            // The payload is opaque bytes that may legally contain '\n' or
            // even "sig=", which is exactly why payloadLength is carried in
            // the (signed) header instead of being guessed by scanning.
            int signatureLineOffset = payloadStart + payloadLength + 1;
            int afterSignature = signatureLineOffset;
            if (!TryReadAsciiLine(envelope, ref afterSignature, out line)
                || !line.StartsWith(SignaturePrefix, StringComparison.Ordinal)
                || afterSignature != envelope.Length)
            {
                return Fail(ScriptEnvelopeFailureReason.Malformed);
            }
            byte[] signature;
            if (!TryDecodeBase64(line.Substring(SignaturePrefix.Length), out signature))
            {
                return Fail(ScriptEnvelopeFailureReason.Malformed);
            }

            string keyId = values[IndexKid];
            string scriptId = values[IndexScriptId];
            // An empty expiresAt means "never expires"; an empty
            // minApiLevel means "unspecified". Both are absent values, not
            // zeros — hence the companion flags.
            bool hasExpiresAt = values[IndexExpiresAt].Length != 0;
            bool hasMinApiLevel = values[IndexMinApiLevel].Length != 0;
            long expiresAt = 0;
            int minApiLevel = 0;
            long issuedAt;
            if (!IsValidId(keyId)
                || !IsValidId(scriptId)
                || !TryParseUInt64(values[IndexIssuedAt], out issuedAt)
                || (hasExpiresAt && !TryParseUInt64(values[IndexExpiresAt], out expiresAt))
                || (hasMinApiLevel && !TryParseUInt32(values[IndexMinApiLevel], out minApiLevel)))
            {
                return Fail(ScriptEnvelopeFailureReason.Malformed);
            }

            string algorithm = values[IndexAlg];
            if (algorithm != ScriptEnvelopeAlgorithms.RsaSha256
                && algorithm != ScriptEnvelopeAlgorithms.HmacSha256)
            {
                // Same operational meaning as an unknown deliveryVersion:
                // this build cannot process the envelope, so keep the
                // cached script and ship an app update.
                return Fail(ScriptEnvelopeFailureReason.UnsupportedVersion);
            }

            // Selection is by the (kid, alg) PAIR. alg and kid are inside
            // the signed region, so tampering with them breaks the
            // signature — but matching on the pair also means a key can
            // never be pressed into service under an algorithm it was not
            // published for (the JWT "alg confusion" bug class).
            DeliveryKey key = FindKey(trustedKeys, keyId, algorithm);
            if (key == null)
            {
                return Fail(ScriptEnvelopeFailureReason.UnknownKey);
            }

            if (!SignatureVerifies(envelope, signatureLineOffset, signature, key))
            {
                return Fail(ScriptEnvelopeFailureReason.BadSignature);
            }

            // Expiry is checked only AFTER the signature: until then
            // expiresAt is an attacker-controlled integer, and acting on
            // it would mean trusting an unverified header field.
            // expiresAt is inclusive — the envelope dies one millisecond later.
            if (hasExpiresAt && nowUnixMs > expiresAt)
            {
                return Fail(ScriptEnvelopeFailureReason.Expired);
            }

            var payload = new byte[payloadLength];
            Buffer.BlockCopy(envelope, payloadStart, payload, 0, payloadLength);
            return ScriptEnvelopeVerificationResult.Ok(
                payload,
                scriptId,
                issuedAt,
                hasExpiresAt ? (long?)expiresAt : null,
                hasMinApiLevel ? (int?)minApiLevel : null);
        }

        private static ScriptEnvelopeVerificationResult Fail(ScriptEnvelopeFailureReason reason)
        {
            return ScriptEnvelopeVerificationResult.Failed(reason);
        }

        private static DeliveryKey FindKey(IList<DeliveryKey> trustedKeys, string keyId, string algorithm)
        {
            if (trustedKeys == null)
            {
                return null;
            }
            for (int i = 0; i < trustedKeys.Count; i++)
            {
                DeliveryKey candidate = trustedKeys[i];
                if (candidate != null
                    && string.Equals(candidate.KeyId, keyId, StringComparison.Ordinal)
                    && string.Equals(candidate.Algorithm, algorithm, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            return null;
        }

        /// <summary>
        /// Verifies over <c>envelope[0..signedLength)</c> — the received
        /// bytes themselves, never a re-assembled copy.
        /// </summary>
        private static bool SignatureVerifies(
            byte[] envelope, int signedLength, byte[] signature, DeliveryKey key)
        {
            try
            {
                if (key.Algorithm == ScriptEnvelopeAlgorithms.HmacSha256)
                {
                    using (var hmac = new HMACSHA256(key.HmacSecret))
                    {
                        return FixedTimeEquals(hmac.ComputeHash(envelope, 0, signedLength), signature);
                    }
                }
                using (RSA rsa = RSA.Create())
                {
                    rsa.ImportParameters(key.RsaPublicKey);
                    return rsa.VerifyData(
                        envelope, 0, signedLength, signature,
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (CryptographicException)
            {
                // A malformed signature blob (wrong length for the modulus,
                // for instance) reaches the provider as attacker-controlled
                // input. That is a failed verification, not an app crash.
                return false;
            }
        }

        /// <summary>
        /// Length-independent comparison. netstandard2.0 predates
        /// <c>CryptographicOperations.FixedTimeEquals</c>, so it is spelled
        /// out here: an early-exit compare on an HMAC tag leaks the tag
        /// byte by byte to an attacker who can retry.
        /// </summary>
        private static bool FixedTimeEquals(byte[] computed, byte[] provided)
        {
            if (provided == null || computed.Length != provided.Length)
            {
                return false;
            }
            int difference = 0;
            for (int i = 0; i < computed.Length; i++)
            {
                difference |= computed[i] ^ provided[i];
            }
            return difference == 0;
        }

        /// <summary>
        /// <c>kid</c>/<c>scriptId</c> charset. Restricting both ends means
        /// no header value can contain a line break, so the format needs
        /// no escaping and extra header lines cannot be injected.
        /// </summary>
        internal static bool IsValidId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxIdLength)
            {
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool allowed = (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '.' || c == '_' || c == ':' || c == '-';
                if (!allowed)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Reads one '\n'-terminated line of printable US-ASCII and
        /// advances past the terminator. Rejecting everything outside
        /// 0x20-0x7E is what makes a transport that rewrites line endings
        /// report Malformed (the '\r' fails here) instead of silently
        /// producing a broken signature.
        /// </summary>
        private static bool TryReadAsciiLine(byte[] data, ref int offset, out string line)
        {
            line = null;
            int start = offset;
            int i = start;
            while (i < data.Length && data[i] != Newline)
            {
                byte b = data[i];
                if (b < 0x20 || b > 0x7e)
                {
                    return false;
                }
                i++;
            }
            if (i >= data.Length)
            {
                return false; // unterminated line
            }
            var chars = new char[i - start];
            for (int j = 0; j < chars.Length; j++)
            {
                chars[j] = (char)data[start + j];
            }
            line = new string(chars);
            offset = i + 1;
            return true;
        }

        /// <summary>
        /// Non-negative decimal, no leading zeros (so one integer has
        /// exactly one signed spelling), no sign, no overflow.
        /// </summary>
        private static bool TryParseUInt64(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text) || text.Length > 19)
            {
                return false;
            }
            if (text.Length > 1 && text[0] == '0')
            {
                return false;
            }
            long parsed = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                {
                    return false;
                }
                parsed = parsed * 10 + (c - '0');
                if (parsed < 0)
                {
                    return false;
                }
            }
            value = parsed;
            return true;
        }

        private static bool TryParseUInt32(string text, out int value)
        {
            value = 0;
            long wide;
            if (!TryParseUInt64(text, out wide) || wide > int.MaxValue)
            {
                return false;
            }
            value = (int)wide;
            return true;
        }

        private static bool TryDecodeBase64(string text, out byte[] decoded)
        {
            decoded = null;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            // The spec requires exactly one byte-level spelling per envelope, so
            // the signature's base64 must be canonical. Convert.FromBase64String
            // silently ignores embedded ASCII whitespace and tolerates non-zero
            // trailing padding bits, which would let "sig= <b64>", "<b64 with a
            // space>" and non-canonical final groups all verify — signature
            // malleability. Reject any non-alphabet character up front, then
            // require the decoded bytes to re-encode back to the exact input.
            foreach (char c in text)
            {
                bool inAlphabet =
                    (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '=';
                if (!inAlphabet)
                {
                    return false;
                }
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(text);
            }
            catch (FormatException)
            {
                return false;
            }
            if (Convert.ToBase64String(bytes) != text)
            {
                return false;
            }
            decoded = bytes;
            return true;
        }
    }
}
