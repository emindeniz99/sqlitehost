# Guide: shipping new scripts without an app update

New **scripts** already ship without a build — a script is a JSON
envelope the runtime parses at run time. What this guide adds is the
missing half: proving the script your app just downloaded is the one
your backend signed, and that it has not gone stale.

> **What still needs a build:** new *host methods*. The typed
> C#/Java/TS surface is generated and compiled. Scripts can add rules,
> change economy tuning, and rewire the steps they run — they cannot
> call a method the shipped binary does not have.

The pieces:

| Step | Where | What |
|---|---|---|
| 1. sign | your backend, TypeScript | `signScriptEnvelope()` from `@sqlite-host/authoring/delivery` |
| 2. serve | your CDN / API | opaque bytes — any transport |
| 3. fetch | your app | `UnityWebRequest`, `HttpClient`, whatever you already use |
| 4. verify | your app, C# | `ScriptEnvelopeVerifier.Verify()` from `SqliteHost.Delivery` |
| 5. run | your app | the payload bytes go to your existing JSON parsing + `SqliteHostRuntime` |

**`SqliteHost.Delivery` does no networking and reads no clock.** You
supply the bytes and the current time; it supplies trust and freshness.
Which clock you supply is a policy decision the library leaves to you —
step 4 says what the choice costs.
That is deliberate — see `docs/proposals/script-delivery.md`.

## 0. Make a key pair (development)

```ts
import { generateDeliveryKeyPair } from "@sqlite-host/authoring/delivery";

const key = generateDeliveryKeyPair("prod-2026-07");
console.log(key.privateKeyPem);              // keep on the backend
console.log(key.publicKey.modulusBase64);    // ship in the app
console.log(key.publicKey.exponentBase64);   // ship in the app
```

For production use a KMS/HSM and never let the private key touch a
developer machine. The public half is not a secret — it ships inside
the app precisely so the app can check signatures offline.

A date-stamped `keyId` (`prod-2026-07`) makes rotation self-documenting.

**One signing key per app.** Do not share a key across two titles, even
in the same studio. Nothing in the envelope names the application it is
for — the signed region carries `alg`, `kid`, `scriptId`, `issuedAt`,
`expiresAt`, `minApiLevel` and the payload, and no audience or bundle
id (this is where a JWT would have `aud`). So an envelope signed for
title A verifies in title B whenever both trust that key, and
`scriptId` is no defence: studios reuse names like
`daily-quest-rules` across titles precisely because they mean the same
thing. Adding an audience field is a `deliveryVersion` 2 change and is
not on the table today, so the separation has to be operational.

## 1. Sign, on the backend

```ts
import { signScriptEnvelope } from "@sqlite-host/authoring/delivery";

const envelope = signScriptEnvelope({
  scriptId: "daily-quest-rules",
  payload: JSON.stringify(script),      // the script envelope, as-is
  issuedAt: Date.now(),
  expiresAt: Date.now() + 24 * 60 * 60 * 1000,
  minApiLevel: 1,
  keyId: "prod-2026-07",
  key: { alg: "rsa-sha256", privateKeyPem: process.env.DELIVERY_KEY! },
});
// Uint8Array — serve these bytes verbatim.
```

Three rules that matter more than they look:

- **`issuedAt` must strictly increase per `scriptId`.** It is the app's
  rollback defence (step 4). A single signer with a monotonic clock is
  the easy way; two signers racing on one `scriptId` is not.
- **`expiresAt` is a blast-radius limit, not the security model.** It
  bounds how long a captured envelope stays replayable and how long a
  compromised key keeps working before your next app update drops it.
  Hours-to-days is the useful range. Set it on every envelope you
  expect a client to cache — see step 4.
- **`issuedAt` must be a real timestamp, not a counter.** The verifier
  rejects an envelope issued more than five minutes past the `now` you
  hand it (`IssuedInFuture`). That bound exists because the rollback
  rule below has no upper end of its own: one envelope stamped with a
  far-future `issuedAt` — a signer bug, microseconds in a milliseconds
  field, or an attacker during a key compromise — pins the client's
  high-water mark where nothing legitimate can pass it again, and the
  only recovery is an app update that wipes the cache.
- **To roll back, re-sign the old payload with a new `issuedAt`.** Do
  not re-serve yesterday's envelope: that is byte-identical to an
  attacker replaying it, and step 4 will (correctly) reject it.

The payload is passed through **verbatim** — nothing here parses,
reformats or minifies your script. Whatever bytes you hand in are the
bytes that come out the other end, which is what the signature attests
to.

## 2-3. Serve and fetch — your transport, unchanged

The envelope is a byte string. Put it on a CDN, in an existing config
response, in an asset bundle; fetch it however your app already
fetches things. Nothing in `SqliteHost.Delivery` cares.

It is text with a readable header, so `curl` and a code review both
work on it:

```text
sqlite-host-delivery/1
alg=rsa-sha256
kid=prod-2026-07
scriptId=daily-quest-rules
issuedAt=1785283200000
expiresAt=1785369600000
minApiLevel=1
payloadLength=1133

{ …your script JSON… }
sig=Nn9…
```

## 4. Verify, in the app

Build the trusted key set once at startup:

```csharp
using SqliteHost.Delivery;

static readonly List<DeliveryKey> TrustedKeys = new List<DeliveryKey>
{
    // Trust the next key BEFORE you start signing with it.
    DeliveryKey.Rsa("prod-2026-07", ModulusBase64, ExponentBase64),
    DeliveryKey.Rsa("prod-2026-01", OldModulusBase64, OldExponentBase64),
};
```

Then, per download:

```csharp
// Policy for the checks the wire format leaves to you. The defaults
// are the ones an app that caches wants: expiresAt required, and
// issuedAt no more than five minutes ahead of `now`.
static readonly ScriptEnvelopeVerificationOptions Policy =
    new ScriptEnvelopeVerificationOptions();

// The DEVICE clock — the least trustworthy of the three sources the
// library deliberately refuses to choose between. Fine for catching a
// stale CDN; not fine if expiresAt is load-bearing, because the owner
// of the device can wind it back and keep an expired envelope
// verifying. Where it matters, anchor a server `Date` header to a
// monotonic timer at launch and pass that instead.
long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
ScriptEnvelopeVerificationResult result =
    ScriptEnvelopeVerifier.Verify(downloadedBytes, TrustedKeys, nowUnixMs, Policy);

if (!result.IsValid)
{
    // Keep running the script you already have. Never fall through to
    // "use it anyway" — an unverified script is the whole threat.
    Report(result.Reason);
    return;
}

// Rollback defence — the library cannot do this for you, because it
// has no storage. Persist LastIssuedAt per scriptId.
if (result.IssuedAtUnixMs.Value <= LastIssuedAt(result.ScriptId))
{
    return; // stale or replayed; keep the newer script
}

// Cache the ENVELOPE, not the payload. See below.
SaveEnvelope(result.ScriptId, downloadedBytes, result.IssuedAtUnixMs.Value);
Run(result.Payload);
```

### Cache the envelope, and re-verify it every time you load it

**Store the verified envelope bytes — the same `downloadedBytes` you
just handed to `Verify` — not `result.Payload`.** Saving the payload
throws away the signature and the `kid`, which are the only things that
can ever be re-checked. Then run `Verify` again on every load from
cache, against the *current* key set:

```csharp
byte[] cached = LoadEnvelope(scriptId);
if (cached == null) { return null; }

ScriptEnvelopeVerificationResult result = ScriptEnvelopeVerifier.Verify(
    cached, TrustedKeys, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Policy);
if (!result.IsValid)
{
    DeleteEnvelope(scriptId);   // revoked key, or expired while offline
    return null;                // fall back to the bundled script
}
return result.Payload;
```

That is one signature check per launch, and it is what makes revocation
actually revoke. Without it, a script signed by a compromised key
survives the very app update that dropped the key — the update replaces
the key set, but nothing ever re-examines what is already on disk, and
the app keeps running that script until a *newer* envelope arrives.

Two rules follow, and the default `ScriptEnvelopeVerificationOptions`
enforces the first one for you:

- **`expiresAt` is mandatory for anything you cache.** An envelope
  without one never expires, so re-verifying it can never reject it and
  the compromise window never closes. With the default policy such an
  envelope is rejected outright (`MissingExpiry`). If you deliberately
  verify with `RequireExpiry = false`, then treat a null
  `result.ExpiresAtUnixMs` as **do not cache**: use the payload for this
  session and write nothing to disk.
- **Keep the stored `issuedAt` even when you drop the envelope.** The
  high-water mark is what stops rollback replay; deleting it alongside
  an expired envelope re-opens the window the mark was closing.

`IssuedAtUnixMs` is a `long?`, null only when `IsValid` is false — the
`.Value` above is guarded by the `IsValid` check two lines earlier, but
write it explicitly rather than relying on the lifted `<=` operator: a
comparison against a null nullable is `false`, not an error, so a bare
`result.IssuedAtUnixMs <= …` would silently pass a null through instead
of throwing.

`result.ScriptId` may contain `.` and `:` (the verifier's charset for
`scriptId` allows them alongside letters, digits, `_` and `-`) — do not
use it as a filesystem path component without sanitizing it first.

`Verify` never throws — not on truncated downloads, not on an HTML
error page, not on `null`. It returns a reason instead:

| `Reason` | Means | Do |
|---|---|---|
| `Malformed` | not a v1 envelope at all — truncated download, error page, a proxy that rewrote line endings | retry the fetch |
| `UnsupportedVersion` | this build cannot process it: newer `deliveryVersion`, or an `alg` it does not implement | keep the cached script; ship an app update |
| `UnknownKey` | no trusted key for that `kid`+`alg` — rotated out, or an attacker repointed the key | keep the cached script; check your rotation |
| `BadSignature` | altered in transit, or signed by someone else | keep the cached script; this one is worth an alert |
| `Expired` | genuinely signed, but past `expiresAt` | keep the cached script; your publish job is late |
| `IssuedInFuture` | genuinely signed, but `issuedAt` is more than five minutes ahead of the `now` you passed | keep the cached script; a clock is wrong — the signer's, or this device's |

On failure the result carries **nothing else** — no payload, no
`scriptId`. Unverified data is unreachable by construction.

`MinApiLevel` is reported, never enforced: the delivery package does
not know your host's api level. Compare it against your generated
host's level yourself (`docs/api-levels.md`) and clean-skip if the
script is newer than the binary.

## 5. Run it

`result.Payload` is your script JSON, byte-for-byte as authored. It
goes into whatever you already use to turn payload bytes into a
`SqliteHostScript`, and from there into `SqliteHostRuntime` exactly as
a bundled script would. Delivery changes nothing downstream of this
point — see `docs/guides/getting-started.md`.

## Rotating keys

1. Ship a build that trusts **both** the current and the next key.
2. Wait for adoption. Clients that never update keep working on the
   old key, which is the point of shipping both.
3. Switch the backend to sign with the next key.
4. Drop the old key from a later build.

Revocation is the same move, urgently: a compromised key is removed in
the next build. There is no online revocation check — that would need
a transport, and this package does not have one. Two things bound the
damage, and both are yours to implement: short `expiresAt` values,
which cap how long any envelope the stolen key minted stays valid, and
re-verifying the cache on load (step 4), which is what makes the app
update take effect on scripts already on disk. Without the second, a
script signed by the revoked key outlives the update that revoked it.

## `hmac-sha256`: for your dev loop only

```ts
key: { alg: "hmac-sha256", secret: process.env.DEV_SECRET! }
```

```csharp
DeliveryKey.Hmac("dev-1", secretBytes)
```

Convenient for local work, integration tests and server-to-server
checks. **Never ship it to players.** It is a *shared* secret: the
verifying client holds the same bytes the signer holds, so anyone who
unpacks your app can extract it and mint scripts your app will accept.
`rsa-sha256` exists so the client only ever holds the public half.

## What this does not protect you from

- **A player who owns the device.** Client-executed logic is
  client-trusted; someone who can patch your binary can remove the
  verification call. Signing stops *network* attackers and stale
  content, not a determined owner of the hardware. If you need
  authority, the decision belongs on a server.
- **A bad script signed by a good key.** Signing proves origin, not
  safety. Lint payloads before you publish them (`docs/validation.md`).
- **Reading the script.** Envelopes are signed, not encrypted.

## Trying it

`fixtures/delivery/` holds a signed envelope for every case above —
valid, tampered, expired, wrong key, unknown algorithm — with an
`expectations.json` that says which reason each must produce and why.
`tests/delivery-golden/run.mjs` proves the TypeScript signer emits
exactly those bytes; the C# `DeliveryGoldenTests` prove the verifier
reaches exactly those outcomes on the same files.

Its keys are committed in the open and are named `insecure` for a
reason. They are for the test suite. Do not use them.
