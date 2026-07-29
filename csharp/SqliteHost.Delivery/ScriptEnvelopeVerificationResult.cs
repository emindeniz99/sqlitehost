namespace SqliteHost.Delivery
{
    /// <summary>
    /// Algorithm tokens the <c>alg</c> header may carry in
    /// <c>deliveryVersion</c> 1.
    /// </summary>
    public static class ScriptEnvelopeAlgorithms
    {
        /// <summary>RSASSA-PKCS#1 v1.5 over SHA-256. The production choice (broadest IL2CPP/Mono support).</summary>
        public const string RsaSha256 = "rsa-sha256";

        /// <summary>HMAC-SHA-256 over a shared secret. Development only — see <see cref="DeliveryKey.Hmac"/>.</summary>
        public const string HmacSha256 = "hmac-sha256";
    }

    /// <summary>
    /// Why an envelope was rejected. The app's reaction differs per
    /// reason, which is why these are not collapsed into one boolean —
    /// see docs/proposals/script-delivery.md (Verification order).
    /// </summary>
    public enum ScriptEnvelopeFailureReason
    {
        /// <summary>No failure; the envelope verified.</summary>
        None = 0,

        /// <summary>
        /// The bytes are not a well-formed v1 envelope — truncated
        /// download, an HTML error page, rewritten line endings, a header
        /// field out of order. Retry the fetch; do not ship an update.
        /// </summary>
        Malformed = 1,

        /// <summary>
        /// This build cannot process the envelope: <c>deliveryVersion</c>
        /// is not 1, or <c>alg</c> names an algorithm it does not
        /// implement. Both mean "the client is too old" — keep running the
        /// cached script and ship an app update.
        /// </summary>
        UnsupportedVersion = 2,

        /// <summary>
        /// No trusted key matches the envelope's <c>kid</c> <em>and</em>
        /// <c>alg</c>. Either a key was rotated out of this build, or an
        /// attacker repointed <c>kid</c>/substituted <c>alg</c>.
        /// </summary>
        UnknownKey = 3,

        /// <summary>
        /// The signature does not verify against the trusted key: the
        /// bytes were altered in transit, or signed by someone else.
        /// </summary>
        BadSignature = 4,

        /// <summary>
        /// The signature is genuine but <c>expiresAt</c> is in the past
        /// relative to the caller-supplied <c>now</c>. Only ever reported
        /// after the signature verified.
        /// </summary>
        Expired = 5
    }

    /// <summary>
    /// Outcome of <see cref="ScriptEnvelopeVerifier.Verify"/>. Fail-closed:
    /// the payload and the header fields are populated only when
    /// <see cref="IsValid"/> is true, so a caller cannot accidentally log,
    /// branch on, or cache attacker-controlled values from a rejected
    /// envelope.
    /// </summary>
    public sealed class ScriptEnvelopeVerificationResult
    {
        private ScriptEnvelopeVerificationResult(ScriptEnvelopeFailureReason reason)
        {
            Reason = reason;
        }

        public bool IsValid
        {
            get { return Reason == ScriptEnvelopeFailureReason.None; }
        }

        public ScriptEnvelopeFailureReason Reason { get; private set; }

        /// <summary>
        /// The payload exactly as delivered — hand these bytes to the
        /// app's existing script JSON parsing (docs/script-envelope.md).
        /// Null unless <see cref="IsValid"/>.
        /// </summary>
        public byte[] Payload { get; private set; }

        /// <summary>Verified <c>scriptId</c>; null unless <see cref="IsValid"/>.</summary>
        public string ScriptId { get; private set; }

        /// <summary>
        /// Verified <c>issuedAt</c> (Unix ms); null unless
        /// <see cref="IsValid"/>. Persist this per <see cref="ScriptId"/>
        /// and reject any later envelope whose value is not strictly
        /// greater — that rule, not the TTL, is what stops rollback replay
        /// (docs/proposals/script-delivery.md).
        /// </summary>
        public long? IssuedAtUnixMs { get; private set; }

        /// <summary>Verified <c>expiresAt</c> (Unix ms, inclusive); null when the envelope never expires or is invalid.</summary>
        public long? ExpiresAtUnixMs { get; private set; }

        /// <summary>
        /// Verified <c>minApiLevel</c>; null when unspecified or invalid.
        /// Reported, never enforced — this package does not know the
        /// host's api level (docs/api-levels.md).
        /// </summary>
        public int? MinApiLevel { get; private set; }

        internal static ScriptEnvelopeVerificationResult Failed(ScriptEnvelopeFailureReason reason)
        {
            return new ScriptEnvelopeVerificationResult(reason);
        }

        internal static ScriptEnvelopeVerificationResult Ok(
            byte[] payload, string scriptId, long issuedAtUnixMs, long? expiresAtUnixMs, int? minApiLevel)
        {
            var result = new ScriptEnvelopeVerificationResult(ScriptEnvelopeFailureReason.None);
            result.Payload = payload;
            result.ScriptId = scriptId;
            result.IssuedAtUnixMs = issuedAtUnixMs;
            result.ExpiresAtUnixMs = expiresAtUnixMs;
            result.MinApiLevel = minApiLevel;
            return result;
        }
    }
}
