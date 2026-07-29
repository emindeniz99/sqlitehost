import assert from "node:assert/strict";
import { createHmac, createPublicKey, createVerify } from "node:crypto";
import { readFileSync } from "node:fs";
import { test } from "node:test";

import { generateDeliveryKeyPair, signScriptEnvelope } from "../delivery.js";
import { fixturePath } from "./helpers.js";

/**
 * The signing half of Script Delivery v1
 * (docs/proposals/script-delivery.md). The cross-language golden
 * (tests/delivery-golden/run.mjs + the C# DeliveryGoldenTests) proves
 * the committed fixtures round-trip; these tests pin the format rules
 * that make that round-trip possible in the first place.
 */

const KEY = generateDeliveryKeyPair("test-key-1");
const HMAC_SECRET = Buffer.from("test-hmac-secret");
const ISSUED_AT = Date.parse("2026-07-29T00:00:00.000Z");

function signRsa(overrides: Record<string, unknown> = {}): Buffer {
  return Buffer.from(
    signScriptEnvelope({
      scriptId: "daily-quest-rules",
      payload: '{"engine":"sqlite-host-v1"}',
      issuedAt: ISSUED_AT,
      expiresAt: ISSUED_AT + 86_400_000,
      minApiLevel: 1,
      keyId: "test-key-1",
      key: { alg: "rsa-sha256", privateKeyPem: KEY.privateKeyPem },
      ...overrides,
    } as Parameters<typeof signScriptEnvelope>[0]),
  );
}

/** The verifier computes this range from payloadLength; the signer must agree byte-for-byte. */
function signedRegion(envelope: Buffer): Buffer {
  const payloadStart = envelope.indexOf("\n\n", 0, "latin1") + 2;
  const payloadLength = Number(
    /payloadLength=(\d+)/.exec(envelope.toString("latin1"))![1],
  );
  return envelope.subarray(0, payloadStart + payloadLength + 1);
}

test("envelope is the pinned line-framed header with the payload verbatim", () => {
  // Fixed field order is what lets both sides skip canonicalization
  // entirely. If this layout drifts, the hand-rolled C# scanner —
  // which asserts the same seven keys in the same order — stops
  // parsing, so the shape is pinned here rather than described.
  const lines = signRsa().toString("latin1").split("\n");
  assert.deepEqual(lines.slice(0, 9), [
    "sqlite-host-delivery/1",
    "alg=rsa-sha256",
    "kid=test-key-1",
    "scriptId=daily-quest-rules",
    `issuedAt=${ISSUED_AT}`,
    `expiresAt=${ISSUED_AT + 86_400_000}`,
    "minApiLevel=1",
    'payloadLength=27',
    "",
  ]);
  assert.equal(lines[9], '{"engine":"sqlite-host-v1"}');
});

test("the signature covers the whole envelope except the sig line", () => {
  // This is THE format invariant: alg, kid and deliveryVersion sit
  // inside the signed region, so an attacker cannot downgrade the
  // algorithm or repoint the key id (the JWT "alg confusion" bug
  // class) without invalidating the signature. Verified here against
  // node's own verifier over the byte range the C# side recomputes.
  const envelope = signRsa();
  const signature = Buffer.from(
    /sig=(.*)\n$/.exec(envelope.toString("latin1"))![1],
    "base64",
  );
  const region = signedRegion(envelope);
  assert.ok(region.toString("latin1").includes("alg=rsa-sha256"));
  assert.ok(region.toString("latin1").includes("kid=test-key-1"));
  assert.ok(!region.toString("latin1").includes("sig="));
  assert.equal(
    createVerify("sha256").update(region).verify(KEY.privateKeyPem, signature),
    true,
  );
  // …and nothing wider: extending the range by one byte must not verify.
  assert.equal(
    createVerify("sha256")
      .update(envelope.subarray(0, region.length + 1))
      .verify(KEY.privateKeyPem, signature),
    false,
  );
});

test("the exported modulus/exponent reproduce the signing key's public half", () => {
  // netstandard2.0 cannot import SPKI, so the C# verifier is handed raw
  // modulus/exponent base64. If this conversion were wrong, every
  // signature would still be produced correctly and would verify
  // nowhere — a failure that only surfaces cross-language.
  const publicKey = createPublicKey({
    key: {
      kty: "RSA",
      n: KEY.publicKey.modulusBase64.replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, ""),
      e: KEY.publicKey.exponentBase64.replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, ""),
    },
    format: "jwk",
  });
  const envelope = signRsa();
  const signature = Buffer.from(/sig=(.*)\n$/.exec(envelope.toString("latin1"))![1], "base64");
  assert.equal(
    createVerify("sha256").update(signedRegion(envelope)).verify(publicKey, signature),
    true,
  );
});

test("absent expiresAt and minApiLevel serialize as empty, not as 0", () => {
  // "never expires" must not become "expired at epoch 0", and
  // "unspecified api level" must not become "requires level 0". The C#
  // verifier distinguishes empty from zero; the signer has to too.
  const lines = signRsa({ expiresAt: null, minApiLevel: undefined })
    .toString("latin1")
    .split("\n");
  assert.equal(lines[5], "expiresAt=");
  assert.equal(lines[6], "minApiLevel=");
});

test("payloadLength counts bytes, not characters", () => {
  // A multi-byte payload measured in UTF-16 code units would make the
  // verifier slice the payload short and then hash a region that never
  // matches. Non-ASCII in a script's string literals is ordinary.
  const payload = '{"label":"écu 🎁"}';
  const envelope = signRsa({ payload });
  const byteLength = Buffer.byteLength(payload, "utf8");
  assert.ok(envelope.includes(`payloadLength=${byteLength}\n`));
  assert.notEqual(byteLength, payload.length);
  const payloadStart = envelope.indexOf("\n\n", 0, "latin1") + 2;
  assert.equal(envelope.subarray(payloadStart, payloadStart + byteLength).toString("utf8"), payload);
});

test("a payload that itself contains an envelope tail survives intact", () => {
  // The payload is opaque bytes and may legally contain "\nsig=". This
  // is why payloadLength is a signed header field instead of the
  // verifier scanning for the last line.
  const payload = '{"note":"trap"}\nsig=AAAA\n';
  const envelope = signRsa({ payload });
  const payloadStart = envelope.indexOf("\n\n", 0, "latin1") + 2;
  assert.equal(
    envelope.subarray(payloadStart, payloadStart + Buffer.byteLength(payload)).toString("utf8"),
    payload,
  );
});

test("hmac-sha256 signs the same region with a plain HMAC", () => {
  // hmac-sha256 exists for dev loops, but it must sign exactly the
  // range rsa-sha256 does — otherwise the two algorithms would disagree
  // about what a signature attests to.
  const envelope = Buffer.from(
    signScriptEnvelope({
      scriptId: "daily-quest-rules",
      payload: "{}",
      issuedAt: ISSUED_AT,
      keyId: "test-hmac",
      key: { alg: "hmac-sha256", secret: HMAC_SECRET },
    }),
  );
  const expected = createHmac("sha256", HMAC_SECRET).update(signedRegion(envelope)).digest("base64");
  assert.ok(envelope.toString("latin1").endsWith(`sig=${expected}\n`));
});

test("signing rejects ids and timestamps the format cannot represent", () => {
  // Authoring input is the author's own data, so it fails loud here
  // rather than shipping bytes every client rejects. The id charset is
  // also what guarantees a header value can never contain a line break,
  // so a "clever" scriptId cannot inject an extra header line.
  assert.throws(() => signRsa({ scriptId: "has space" }), TypeError);
  assert.throws(() => signRsa({ scriptId: "quests\nkid=attacker" }), TypeError);
  assert.throws(() => signRsa({ keyId: "" }), TypeError);
  assert.throws(() => signRsa({ issuedAt: -1 }), TypeError);
  assert.throws(() => signRsa({ issuedAt: 1.5 }), TypeError);
  assert.throws(() => signRsa({ issuedAt: Number.MAX_SAFE_INTEGER + 2 }), TypeError);
});

test("signing a committed script fixture reproduces the committed envelope", () => {
  // Ties the signer to the cross-language goldens: the same key, params
  // and payload must yield the exact bytes the C# verifier is tested
  // against. Both signature algorithms are deterministic, which is what
  // makes this comparison meaningful.
  const expectations = JSON.parse(readFileSync(fixturePath("delivery/expectations.json"), "utf8"));
  const testCase = expectations.cases.find((c: { envelope: string }) => c.envelope === "valid-rsa.envelope");
  const envelope = signScriptEnvelope({
    scriptId: testCase.build.scriptId,
    payload: readFileSync(fixturePath("payloads/valid/example-001-read-then-conditional-write.json")),
    issuedAt: testCase.build.issuedAt,
    expiresAt: testCase.build.expiresAt,
    minApiLevel: testCase.build.minApiLevel,
    keyId: testCase.build.kid,
    key: {
      alg: "rsa-sha256",
      privateKeyPem: readFileSync(fixturePath("delivery/dev-rsa-1.insecure-private.pem"), "utf8"),
    },
  });
  assert.deepEqual(Buffer.from(envelope), readFileSync(fixturePath("delivery/valid-rsa.envelope")));
});
