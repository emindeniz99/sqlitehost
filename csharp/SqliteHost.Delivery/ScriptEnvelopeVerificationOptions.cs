namespace SqliteHost.Delivery
{
    /// <summary>
    /// Policy an app applies on top of the wire format, for the checks the
    /// format itself leaves to the caller. Pass one to
    /// <see cref="ScriptEnvelopeVerifier.Verify(byte[], System.Collections.Generic.IList{DeliveryKey}, long, ScriptEnvelopeVerificationOptions)"/>.
    ///
    /// A default-constructed instance is deliberately STRICTER than the
    /// three-argument <c>Verify</c> overload. That overload keeps
    /// implementing <c>deliveryVersion</c> 1 as specified — where an empty
    /// <c>expiresAt</c> means "never expires" — because that is the wire
    /// format and the cross-language golden corpus pins it. An app that
    /// reaches for this type is choosing policy, and the policy worth
    /// defaulting to is the one that bounds a key compromise.
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
            RequireExpiry = true;
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

        /// <summary>
        /// When true (the default for this type), an envelope with no
        /// <c>expiresAt</c> is rejected as
        /// <see cref="ScriptEnvelopeFailureReason.MissingExpiry"/>.
        ///
        /// Revocation in this design is an app update, so <c>expiresAt</c>
        /// is the only thing bounding the window in which a compromised key
        /// keeps minting envelopes the app accepts — and an envelope with
        /// no <c>expiresAt</c> never closes that window at all. Any app
        /// that CACHES a delivered script wants this on. Turn it off only
        /// where a never-expiring envelope is genuinely intended and
        /// nothing is written to disk.
        /// </summary>
        public bool RequireExpiry { get; set; }
    }
}
