namespace SqliteHost.Delivery
{
    /// <summary>
    /// Policy an app applies on top of the wire format, for the checks the
    /// format itself leaves to the caller. Pass one to
    /// <see cref="ScriptEnvelopeVerifier.Verify(byte[], System.Collections.Generic.IList{DeliveryKey}, long, ScriptEnvelopeVerificationOptions)"/>.
    ///
    /// See docs/proposals/script-delivery.md (Downgrade and replay).
    /// </summary>
    public sealed class ScriptEnvelopeVerificationOptions
    {
        /// <summary>Five minutes, in milliseconds.</summary>
        public const long DefaultMaxIssuedAtSkewMs = 5L * 60L * 1000L;

        public ScriptEnvelopeVerificationOptions()
        {
            MaxIssuedAtSkewMs = DefaultMaxIssuedAtSkewMs;
        }

        /// <summary>
        /// How far past <c>nowUnixMs</c> an envelope's <c>issuedAt</c> may
        /// sit before it is rejected as
        /// <see cref="ScriptEnvelopeFailureReason.IssuedInFuture"/>. The
        /// ceiling is inclusive.
        ///
        /// Without it, one envelope carrying a maximal <c>issuedAt</c>
        /// permanently freezes a <c>scriptId</c>: the app's own
        /// "strictly greater than the stored value" rule then rejects every
        /// legitimate envelope that follows. The device clock is untrusted
        /// for *ordering* but is sound as a sanity ceiling, because a
        /// wound-back clock only makes this check stricter.
        ///
        /// <see cref="long.MaxValue"/> disables the check, for a fleet whose
        /// devices have no usable clock at all.
        /// </summary>
        public long MaxIssuedAtSkewMs { get; set; }
    }
}
