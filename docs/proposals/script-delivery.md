# Proposal: script delivery v1 (signed envelope + freshness)

Status: **implemented** (TS signer in `@sqlite-host/authoring`, C#
verifier in the optional `SqliteHost.Delivery` package, cross-language
golden under `tests/delivery-golden/`). Rollout policy (staged
percentages, cohorts) and any transport remain out of scope.

## Motivation

`docs/why-sql-not-a-vm.md` lists remote delivery, signing and TTL as v1
non-goals, and that non-goal is read as "SqliteHost has no hot-update
story". The architecture already says otherwise: a script is a JSON
envelope the runtime parses at run time, so **new scripts already ship
without a build**. What is genuinely missing is not an engine change,
it is the trust and freshness layer around those bytes:

- the app has no way to tell a script its backend signed from a script
  an attacker (or a stale CDN, or a MITM proxy) handed it;
- there is no expiry, so a captured script is valid forever;
- there is no key rotation story, so the first key you ship is the key
  you ship forever.

Script Delivery v1 supplies exactly that layer and nothing else.

## Scope

**In scope.** An envelope format that binds a script payload to an
issuer key, a TS-side signer, a C#-side verifier, and the documented
cache contract an app must implement to resist replay.

**Explicitly out of scope — and this is a design decision, not a
backlog entry.**

1. **No HTTP, no transport, no storage.** The package never opens a
   socket and never touches the filesystem. Transport differs per
   engine and per studio — `UnityWebRequest` on Unity, `HttpClient` on
   a .NET backend service, a platform CDN SDK elsewhere — and every
   one of those pulls in dependencies the runtime packages refuse to
   take. **The app supplies bytes; the package supplies trust.**
2. **No clock.** `Verify` takes `nowUnixMs` from the caller and reads
   no wall clock. This keeps the library deterministic and testable,
   lets a replay harness verify a year-old capture at its original
   instant, and leaves clock-source policy (device clock vs. a server
   `Date` header vs. a monotonic anchor) with the app, which is the
   only layer that knows how much it trusts the device.
3. **No JSON parsing.** The verifier returns *payload bytes*. Parsing
   them is the app's existing job (`docs/script-envelope.md`); the
   shipped C# packages contain no JSON parser today and this one does
   not add the first.

## Why not JSON for the envelope itself

The signature must cover an exact byte sequence. If the envelope were
JSON, the signer and the verifier would have to agree on a *canonical*
JSON — key order, number formatting, escaping of non-ASCII, whether
`1e3` and `1000` are the same. That is the entire JCS/JWS-canon
problem, it is a classic source of signature-bypass bugs, and closing
it needs a JSON writer *and* reader that agree byte-for-byte across
Node and .NET. Rejected.

The alternatives considered:

- **JWS compact (`b64header.b64payload.b64sig`).** Familiar, but the
  protected header is JSON, so the verifier needs a JSON parser to
  read `alg`/`kid` — the thing we are trying to avoid. Rejected.
- **Length-prefixed binary framing.** Unambiguous and compact, but it
  makes the envelope opaque: no `head -8 x.envelope` during an
  incident, no diff in a code review, and it introduces endianness as
  a cross-language contract. It also throws away the property
  `docs/why-sql-not-a-vm.md` sells hardest — that the shipped artifact
  is *diffable plain text, not signed bytecode*. Rejected.
- **Line-framed ASCII header + verbatim payload + trailing signature.**
  Chosen. There is no canonicalization step at all: the header field
  order is fixed, every header value is drawn from a charset that
  cannot contain a line break, and the payload is copied verbatim.
  The signer emits bytes; the verifier hashes the very same bytes it
  received. Nothing is ever re-serialized on either side.

## Wire format (`deliveryVersion` = 1)

An envelope is a byte string. Header lines are US-ASCII, terminated by
a single `\n` (0x0A). The payload region is opaque bytes.

```
sqlite-host-delivery/1\n
alg=<alg>\n
kid=<keyId>\n
scriptId=<scriptId>\n
issuedAt=<int64>\n
expiresAt=<int64 | empty>\n
minApiLevel=<int32 | empty>\n
payloadLength=<int32>\n
\n
<exactly payloadLength bytes, verbatim>\n
sig=<base64>\n
```

Rules, all of which a verifier MUST enforce:

| Rule | Value |
|---|---|
| Line 0 | literal `sqlite-host-delivery/` followed by the decimal `deliveryVersion` |
| Lines 1-7 | exactly those seven keys, **in that order**, each `key=value` |
| Line 8 | empty — it separates the header from the payload |
| `alg` | `rsa-sha256` or `hmac-sha256` |
| `kid`, `scriptId` | 1-128 chars from `[A-Za-z0-9._:-]` |
| `issuedAt` | non-negative decimal Unix milliseconds, no leading zeros |
| `expiresAt` | same as `issuedAt`, **or empty** meaning "never expires" |
| `minApiLevel` | non-negative decimal, **or empty** meaning "unspecified" |
| `payloadLength` | non-negative decimal count of payload **bytes** |
| after the payload | one `\n`, then `sig=`, base64 (standard alphabet, padded), then a final `\n` and EOF |

The trailing `\n` is required, not optional, and nothing may follow it:
one envelope has exactly one byte-level spelling, so "same envelope,
different bytes" never arises.

Fixed field order is what removes canonicalization: there is no map to
sort, no optional field to omit, no whitespace to normalize. Absent or
reordered fields are `malformed`, not defaults.

`payloadLength` is carried explicitly rather than inferred by scanning
backwards for the last line, because the payload is opaque bytes that
may legally contain `\n`, `sig=`, or a nested envelope. With the length
in the header the payload boundary — and therefore the signed range —
is computed, never guessed.

Values need no escaping because none of them can contain `\n`: the
numeric fields are digits, and `kid`/`scriptId` are restricted at both
the signer and the verifier. A `\r` is not in any value charset, so a
transport that rewrites line endings produces `malformed` rather than
a silently broken signature — fail loud.

### The signed byte sequence

```
sigLineOffset = payloadStart + payloadLength + 1
signedBytes   = envelope[0 .. sigLineOffset)
```

where `payloadStart` is the offset just past the empty line 8. In
words: **the signed bytes are the whole envelope up to but excluding
the `sig=` line, including the `\n` that terminates the payload.**

Two properties follow, and they are the reason for this layout:

1. **The signed region is a contiguous prefix of the received bytes.**
   The verifier hashes a slice of the array it was handed. It never
   rebuilds the header from parsed fields, so a parser bug cannot
   widen what a signature covers.
2. **Everything except the signature is signed** — including
   `deliveryVersion`, `alg` and `kid`. An attacker cannot rewrite
   `alg=rsa-sha256` to `alg=hmac-sha256` (the JWT `alg`-confusion bug
   class) or repoint `kid` at a weaker key without invalidating the
   signature.

`alg` and `kid` are read *before* verification, which is unavoidable —
you cannot check a signature without knowing which key and which
algorithm. They are nevertheless covered by it, so tampering is
detected. As defence in depth, the key set also declares each key's
algorithm and the verifier requires it to equal the envelope's `alg`
(see check 5 below).

### Algorithms

| `alg` | Definition | Use |
|---|---|---|
| `rsa-sha256` | RSASSA-**PKCS#1 v1.5** over SHA-256 of `signedBytes`, per RFC 8017 §8.2 | production |
| `hmac-sha256` | HMAC-SHA-256 over `signedBytes`, compared in constant time | dev/internal only |

**Why PKCS#1 v1.5 and not PSS.** Unity/IL2CPP is the hard constraint:
PKCS#1 v1.5 verification is the path with the broadest Mono and
`netstandard2.0` support, and both signatures are deterministic, which
is what lets a TS-produced fixture be byte-compared in CI. PSS is
strictly better cryptography and is the natural `deliveryVersion` 2 or
an `alg=rsa-pss-sha256` addition; the format already carries `alg`
inside the signed region precisely so that adding one is not a
breaking change.

**Why ECDSA is absent.** `ECDsa` availability on IL2CPP/Mono is
uneven, and the same generation of curve support varies by platform.
The whole point of this package is that it links into a Unity player
without surprises.

**`hmac-sha256` is documented as weaker, deliberately.** It is a
*shared* secret: the verifying client holds the same bytes the signer
holds, so anyone who unpacks the app can extract it and mint scripts
the app will accept. It exists because it is genuinely useful for
local development, integration tests and server-to-server checks where
both ends are already trusted. **It must not be shipped to players.**
The guide says this in the same words.

## Key model

A key is `(keyId, algorithm, material)`. The app hands `Verify` a list
of keys; the verifier selects by `kid` **and** `alg`.

- **`keyId` is opaque** to the library — any `[A-Za-z0-9._:-]{1,128}`
  string. A date-stamped convention (`prod-2026-07`) makes rotation
  self-documenting.
- **RSA public keys are supplied as raw modulus + exponent**
  (`RSAParameters`, or base64 strings the library converts). Not
  SPKI/PEM: `netstandard2.0` has no `ImportSubjectPublicKeyInfo`
  (that arrived in .NET Core 3.0), and adding a DER parser to a
  zero-dependency package to work around it is not worth it. The TS
  keygen helper emits the two base64 strings directly.
- **Rotation is additive and needs no protocol step.** Ship the app
  with N trusted keys; sign with one. To rotate: ship a build trusting
  {old, new}, wait for adoption, start signing with new, drop old from
  the next build. Because `kid` is inside the signed region, a
  rotation cannot be forced by an attacker — only by an app update.
- **Revocation is an app update.** There is no CRL, no OCSP, no
  online check — those all need a transport, which this package does
  not have. A compromised key is dropped from the next build's key
  set, and the `expiresAt` on already-signed envelopes bounds the
  damage window in the meantime. That bound is the main operational
  argument for keeping TTLs short.

## Verification order (normative)

`Verify(envelopeBytes, keys, nowUnixMs)` performs, in this order:

1. Line 0 parses as magic + decimal version → else `malformed`.
2. `deliveryVersion` != 1 → `unsupported-version`. Checked here, before
   the rest of the framing: a v2 envelope must not be judged against
   the v1 layout and reported as `malformed`.
3. Remaining framing, charset, field-order and value checks →
   `malformed`.
4. `alg` not implemented by this build → `unsupported-version`.
5. No key with this `kid` **and** this `alg` → `unknown-key`.
6. Signature over `signedBytes` does not verify → `bad-signature`.
7. `expiresAt` present and `nowUnixMs > expiresAt` → `expired`.
8. Otherwise `Ok(payloadBytes)`.

Three of those orderings are load-bearing:

- **Signature before expiry (6 before 7).** Until step 6 passes,
  `expiresAt` is an attacker-controlled integer. Reporting `expired`
  for an unsigned envelope would mean the library acted on an
  unverified header field, and would hand an attacker an oracle for
  probing the client's clock. Every field is untrusted until the
  signature says otherwise.
- **Unimplemented `alg` is `unsupported-version`, not `malformed`.**
  Both `deliveryVersion` and `alg` answer the same operational
  question — *this client build cannot process this envelope* — and
  the app's reaction is the same: keep running the cached script and
  ship an app update. `malformed` means something different and more
  alarming: these bytes are not a v1 envelope at all (truncated
  download, HTML error page, wrong URL).
- **`alg` mismatch against a known `kid` is `unknown-key` (5).** The
  key set is keyed by the *pair*; "I have that id but not for that
  algorithm" is precisely "I do not have that key".

`expiresAt` is inclusive: `nowUnixMs == expiresAt` still verifies, and
the envelope dies at the first millisecond after. `minApiLevel` is
**reported, never enforced** — the library has no idea what api level
the host was generated at (`docs/api-levels.md`), so it hands the value
to the app, which does.

### Fail-closed result shape

On success the result carries the payload bytes *and* the header
fields (`scriptId`, `issuedAt`, `expiresAt`, `minApiLevel`). On
failure it carries a reason and **nothing else** — no payload, no
partially-parsed header. This is deliberate: an API that exposed
`ScriptId` on a `bad-signature` result would invite callers to log,
branch on, or cache attacker-controlled data. If it is not verified,
the caller cannot reach it.

`Verify` never throws for any input — arbitrary bytes, truncated
envelopes, `null`, an empty key list. It is the first code in the
process to touch bytes from the network, so a thrown exception is a
denial-of-service primitive. Constructing a `DeliveryKey` *does*
validate and throw, because that is the app's own configuration, not
attacker input, and a malformed trusted key must fail loudly at
startup rather than degrade to "nothing verifies".

## Downgrade and replay: the app's cache contract

The library does not store anything, so it cannot detect replay on its
own — replay detection needs memory across runs, and memory means
storage, and storage means a platform dependency. Here is the exact
threat and the exact contract, so an app can implement it correctly.

**The attack.** Envelope A (`issuedAt=100`) enables a generous offer;
envelope B (`issuedAt=200`) turns it off. Both are validly signed and
neither has expired. An attacker who captured A serves it back after B
and the client happily accepts it — the signature is genuine.

**The contract, which the app MUST implement:**

> Persist `issuedAt` alongside the cached script, per `scriptId`.
> Accept a newly verified envelope only if its `issuedAt` is strictly
> greater than the stored value for that `scriptId`. Otherwise keep
> what you have.

That is why `scriptId` and `issuedAt` are inside the signed region and
are returned on success: they are not decoration, they are the inputs
to this rule.

Consequences worth stating plainly:

- **`expiresAt` bounds the window, it does not close it.** A short TTL
  limits how long a captured envelope is replayable; monotonic
  `issuedAt` is what actually stops the rollback.
- **Rollback is a re-sign, not a re-serve.** To intentionally go back
  to yesterday's rules, sign that payload again with a *new*
  `issuedAt`. Re-serving the old envelope is indistinguishable from
  the attack above and must be rejected by the same rule.
- **`issuedAt` must be monotonic per `scriptId` at the signer.** Two
  signers racing on the same `scriptId` can emit out-of-order
  timestamps and clients will pin the higher one.
- **Device clock is not trusted for ordering.** The rule compares two
  *signed* `issuedAt` values against each other, never against the
  device clock. Only `expiresAt` involves `nowUnixMs`, and a device
  with a wound-back clock merely delays expiry — it cannot resurrect a
  superseded script.

## What this does not defend against

Stated so the guide does not have to overclaim, and consistent with
`docs/validation.md` ("not a security sandbox"):

- **A rooted/jailbroken device or a patched binary.** The client holds
  the verification code; an attacker who owns the process can remove
  the call. Client-executed logic is client-trusted — §3.1 of the
  market audit, and unchanged here.
- **A malicious script signed by a *valid* key.** Signing proves
  origin, not safety. Script content is the validators' job.
- **Confidentiality.** Envelopes are signed, not encrypted; the
  payload is readable by anyone holding the bytes.

## Package placement

`SqliteHost.Delivery` is a **new, separate, optional** package:
`netstandard2.0`, `LangVersion 8`, zero external dependencies, and no
reference to `SqliteHost.Runtime` or `SqliteHost.Abstractions`. It
depends only on `System.Security.Cryptography` from the BCL.

Not folded into the existing packages because:

- **`SqliteHost.Runtime` and `SqliteHost.Abstractions` must not grow.**
  Their size is a measured, published claim
  (`docs/reports/il2cpp-size-report.md`); a crypto dependency every
  non-delivery consumer would link is exactly the kind of growth
  that claim exists to prevent.
- **Delivery is optional by construction.** Apps that ship scripts in
  their bundle need none of this, and apps that already have a signed
  asset pipeline may verify with their own code and hand the runtime
  the payload directly.
- **The dependency arrow would point the wrong way.** Delivery knows
  nothing about hosts, methods or SQL — it moves opaque bytes. Keeping
  it uncoupled is what lets it verify anything, and what keeps the
  runtime free of crypto.
