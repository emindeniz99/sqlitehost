#!/usr/bin/env node
// Script Delivery golden runner (docs/proposals/script-delivery.md):
// rebuilds every envelope in fixtures/delivery/expectations.json from its
// `build` block using the TypeScript signer, and byte-compares the result
// against the committed fixture. The C# side (DeliveryGoldenTests) reads
// the same committed bytes and must verify them to the same outcome — the
// two halves together are the cross-language contract: bytes signed by
// Node verify under .NET, and only those bytes do.
//
// Both signature algorithms are deterministic (RSASSA-PKCS#1 v1.5 and
// HMAC), which is what makes a byte-compare possible at all; a randomized
// scheme such as RSA-PSS would need a different golden strategy.
//
// Regenerate after an intentional format or payload change:
//   UPDATE_DELIVERY_GOLDENS=1 node tests/delivery-golden/run.mjs
import { execSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import assert from "node:assert/strict";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const deliveryDir = join(root, "fixtures", "delivery");
const update = process.env.UPDATE_DELIVERY_GOLDENS === "1";

console.log("==> building workspace packages");
execSync("pnpm -r --silent run build", { cwd: root, stdio: "inherit" });

const { signScriptEnvelope } = await import(join(root, "typescript/authoring-sdk/dist/delivery.js"));

const expectations = JSON.parse(readFileSync(join(deliveryDir, "expectations.json"), "utf8"));
const keys = JSON.parse(readFileSync(join(deliveryDir, expectations.keys), "utf8"));
const payload = readFileSync(join(deliveryDir, expectations.payload));

let checks = 0;
function check(name, fn) {
  fn();
  checks++;
  console.log(`ok  ${name}`);
}

/** Signing material by key id. dev-rsa-2 is intentionally absent from `trusted`. */
function signingKey(keyId) {
  if (keyId.startsWith("dev-rsa-")) {
    return {
      alg: "rsa-sha256",
      privateKeyPem: readFileSync(join(deliveryDir, `${keyId}.insecure-private.pem`), "utf8"),
    };
  }
  const trusted = keys.trusted.find((k) => k.keyId === keyId);
  assert.ok(trusted, `no signing material for ${keyId}`);
  return { alg: "hmac-sha256", secret: Buffer.from(trusted.secretBase64, "base64") };
}

const built = new Map();

function build(spec, name) {
  if (spec.kind === "sign") {
    return Buffer.from(
      signScriptEnvelope({
        scriptId: spec.scriptId,
        payload,
        issuedAt: spec.issuedAt,
        expiresAt: spec.expiresAt,
        minApiLevel: spec.minApiLevel,
        keyId: spec.kid,
        key: signingKey(spec.signWith),
      }),
    );
  }
  if (spec.kind === "derive") {
    // Derived cases are byte edits on an already-signed envelope — that
    // is precisely what an attacker or a broken transport does, so they
    // are produced the same way rather than re-signed into validity.
    const source = built.get(spec.from);
    assert.ok(source, `${name} derives from ${spec.from}, which must be listed before it`);
    let bytes = Buffer.from(source);
    for (const [find, replaceWith] of spec.replace ?? []) {
      const at = bytes.indexOf(find, 0, "latin1");
      assert.notEqual(at, -1, `${name}: ${JSON.stringify(find)} not found in ${spec.from}`);
      bytes = Buffer.concat([
        bytes.subarray(0, at),
        Buffer.from(replaceWith, "latin1"),
        bytes.subarray(at + Buffer.byteLength(find, "latin1")),
      ]);
    }
    if (spec.xorPayloadByte) {
      // The payload starts just past the blank line that ends the header.
      const payloadStart = bytes.indexOf("\n\n", 0, "latin1") + 2;
      const at = payloadStart + spec.xorPayloadByte.index;
      bytes[at] ^= spec.xorPayloadByte.mask;
    }
    return bytes;
  }
  throw new Error(`${name}: unknown build kind ${spec.kind}`);
}

for (const testCase of expectations.cases) {
  const bytes = build(testCase.build, testCase.envelope);
  built.set(testCase.envelope, bytes);
  const path = join(deliveryDir, testCase.envelope);
  if (update) {
    writeFileSync(path, bytes);
    console.log(`updated  ${testCase.envelope}`);
    continue;
  }
  check(`${testCase.envelope}: TS signer reproduces the committed bytes`, () => {
    assert.deepEqual(bytes, readFileSync(path));
  });
}

if (update) {
  console.log("\nfixtures/delivery updated — rerun without UPDATE_DELIVERY_GOLDENS to verify");
  process.exit(0);
}

// The valid cases must carry the script payload through untouched: the
// delivery layer is a wrapper, not a transform. If this drifts, the app
// would hand the runtime bytes that are no longer the authored script.
for (const testCase of expectations.cases.filter((c) => c.outcome === "ok")) {
  check(`${testCase.envelope}: payload region is the script fixture byte-for-byte`, () => {
    const bytes = built.get(testCase.envelope);
    const payloadStart = bytes.indexOf("\n\n", 0, "latin1") + 2;
    assert.deepEqual(bytes.subarray(payloadStart, payloadStart + payload.length), payload);
  });
}

// Guard the header contract itself, so a "harmless" reordering or an
// extra field cannot land without updating the C# parser in lockstep.
check("envelope header is the pinned field order", () => {
  const lines = built.get("valid-rsa.envelope").toString("latin1").split("\n");
  assert.deepEqual(lines.slice(0, 9), [
    "sqlite-host-delivery/1",
    "alg=rsa-sha256",
    "kid=dev-rsa-1",
    "scriptId=daily-quest-rules",
    `issuedAt=${expectations.cases[0].build.issuedAt}`,
    `expiresAt=${expectations.cases[0].build.expiresAt}`,
    "minApiLevel=1",
    `payloadLength=${payload.length}`,
    "",
  ]);
});

// Absent optional fields are empty strings, never "0" or "null": the C#
// verifier distinguishes them, and a signer that emitted "0" would turn
// "never expires" into "expired in 1970".
check("absent expiresAt/minApiLevel serialize as empty values", () => {
  const lines = built.get("valid-hmac.envelope").toString("latin1").split("\n");
  assert.equal(lines[5], "expiresAt=");
  assert.equal(lines[6], "minApiLevel=");
});

// dev-rsa-2 signs wrong-key.envelope, so it must NOT be trusted, or that
// fixture would silently start passing and stop testing anything.
check("dev-rsa-2 is not in the trusted key set", () => {
  assert.deepEqual(
    keys.trusted.map((k) => k.keyId).sort(),
    ["dev-hmac-1", "dev-rsa-1"],
  );
});

console.log(`\nDELIVERY GOLDENS GREEN (${checks} checks)`);
