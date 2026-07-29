/**
 * Script Delivery v1 — the authoring (signing) half.
 * See docs/proposals/script-delivery.md for the normative format and
 * docs/guides/script-delivery.md for the end-to-end walkthrough.
 *
 * This module builds the signed envelope a backend serves to clients;
 * `SqliteHost.Delivery` (C#) verifies it. It deliberately does no
 * transport and no storage — it turns (payload bytes + header fields +
 * key) into envelope bytes, and nothing else.
 *
 * The envelope is line-framed ASCII with a verbatim payload precisely
 * so that neither side ever re-serializes anything: the signer signs a
 * contiguous prefix of the bytes it emits, and the verifier verifies a
 * contiguous prefix of the bytes it received.
 */

import { createHmac, createPublicKey, createSign, generateKeyPairSync } from "node:crypto";

/** `deliveryVersion` this module emits (the only version defined). */
export const DELIVERY_VERSION = 1;

/** Line 0 is this prefix followed by the decimal delivery version. */
export const DELIVERY_MAGIC = "sqlite-host-delivery/";

export type DeliveryAlgorithm = "rsa-sha256" | "hmac-sha256";

/**
 * Signing material. `rsa-sha256` signs with RSASSA-PKCS#1 v1.5 (the
 * broadly IL2CPP-compatible choice); `hmac-sha256` is a *shared*
 * secret — the client holds the same bytes, so anyone who unpacks the
 * app can mint envelopes it accepts. Dev and server-to-server only.
 */
export type DeliverySigningKey =
  | { alg: "rsa-sha256"; privateKeyPem: string }
  | { alg: "hmac-sha256"; secret: Uint8Array | string };

export interface SignScriptEnvelopeOptions {
  /** Stable identity of the script being delivered; the app's replay cache is keyed on it. */
  scriptId: string;
  /** The script JSON, delivered verbatim — this module never parses or rewrites it. */
  payload: Uint8Array | string;
  /** Unix milliseconds. MUST increase per scriptId: it is the app's rollback defence. */
  issuedAt: number;
  /** Unix milliseconds, inclusive. `null`/omitted means the envelope never expires. */
  expiresAt?: number | null;
  /** Reported to the app, never enforced by the verifier (docs/api-levels.md). */
  minApiLevel?: number | null;
  /** Which trusted key the client should verify with; inside the signed region. */
  keyId: string;
  key: DeliverySigningKey;
}

/** `kid` and `scriptId` charset — chosen so no header value can ever contain a line break. */
const ID_PATTERN = /^[A-Za-z0-9._:-]{1,128}$/;

const NEWLINE = 0x0a;

export interface GeneratedDeliveryKeyPair {
  keyId: string;
  alg: "rsa-sha256";
  /** PKCS#8 PEM — keep on the signing backend, never ship it to clients. */
  privateKeyPem: string;
  /**
   * Public key as raw modulus/exponent, standard base64. netstandard2.0
   * has no `ImportSubjectPublicKeyInfo`, so the C# side takes these two
   * strings rather than SPKI/PEM (docs/proposals/script-delivery.md).
   */
  publicKey: { modulusBase64: string; exponentBase64: string };
}

/**
 * Dev helper: mint an RSA-2048 delivery key pair. Production keys
 * belong in a KMS/HSM — this exists so a quickstart does not need one.
 */
export function generateDeliveryKeyPair(keyId: string): GeneratedDeliveryKeyPair {
  requireId("keyId", keyId);
  const { privateKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
  const jwk = createPublicKey(privateKey).export({ format: "jwk" }) as { n: string; e: string };
  return {
    keyId,
    alg: "rsa-sha256",
    privateKeyPem: privateKey.export({ type: "pkcs8", format: "pem" }).toString(),
    publicKey: {
      modulusBase64: base64UrlToBase64(jwk.n),
      exponentBase64: base64UrlToBase64(jwk.e),
    },
  };
}

/**
 * Builds and signs a v1 envelope. Throws on anything the format
 * cannot represent — authoring-time input is the author's own data, so
 * it fails loud here rather than producing bytes a client will reject.
 */
export function signScriptEnvelope(options: SignScriptEnvelopeOptions): Uint8Array {
  requireId("scriptId", options.scriptId);
  requireId("keyId", options.keyId);
  const issuedAt = requireTimestamp("issuedAt", options.issuedAt);
  const expiresAt =
    options.expiresAt === undefined || options.expiresAt === null
      ? ""
      : String(requireTimestamp("expiresAt", options.expiresAt));
  const minApiLevel =
    options.minApiLevel === undefined || options.minApiLevel === null
      ? ""
      : String(requireNonNegativeInt32("minApiLevel", options.minApiLevel));
  const payload = Buffer.isBuffer(options.payload)
    ? options.payload
    : typeof options.payload === "string"
      ? Buffer.from(options.payload, "utf8")
      : Buffer.from(options.payload);

  const header =
    `${DELIVERY_MAGIC}${DELIVERY_VERSION}\n` +
    `alg=${options.key.alg}\n` +
    `kid=${options.keyId}\n` +
    `scriptId=${options.scriptId}\n` +
    `issuedAt=${issuedAt}\n` +
    `expiresAt=${expiresAt}\n` +
    `minApiLevel=${minApiLevel}\n` +
    `payloadLength=${payload.length}\n` +
    "\n";

  // The signed region is a contiguous prefix: header + payload + the
  // newline that terminates the payload. Everything except the
  // signature itself is covered, including alg and kid — that is what
  // makes algorithm substitution and key repointing detectable.
  const signedBytes = Buffer.concat([Buffer.from(header, "ascii"), payload, Buffer.from([NEWLINE])]);
  const signature = signBytes(signedBytes, options.key);
  return Buffer.concat([signedBytes, Buffer.from(`sig=${signature}\n`, "ascii")]);
}

function signBytes(signedBytes: Buffer, key: DeliverySigningKey): string {
  if (key.alg === "rsa-sha256") {
    // Node's default RSA padding for sign() is PKCS#1 v1.5, which is
    // what the format specifies (PSS is a future alg, not a default).
    return createSign("sha256").update(signedBytes).sign(key.privateKeyPem).toString("base64");
  }
  if (key.alg === "hmac-sha256") {
    const secret = typeof key.secret === "string" ? Buffer.from(key.secret, "utf8") : Buffer.from(key.secret);
    return createHmac("sha256", secret).update(signedBytes).digest("base64");
  }
  throw new TypeError(`unsupported delivery algorithm: ${JSON.stringify((key as { alg: string }).alg)}`);
}

function requireId(field: string, value: string): void {
  if (typeof value !== "string" || !ID_PATTERN.test(value)) {
    throw new TypeError(`${field} must match ${ID_PATTERN.source} (got ${JSON.stringify(value)})`);
  }
}

function requireTimestamp(field: string, value: number): number {
  return requireNonNegativeInt(field, value);
}

function requireNonNegativeInt(field: string, value: number): number {
  // Beyond Number.MAX_SAFE_INTEGER the decimal rendering stops being
  // exact, and an inexact issuedAt breaks the app's monotonicity rule.
  if (!Number.isSafeInteger(value) || value < 0) {
    throw new TypeError(`${field} must be a non-negative safe integer (got ${JSON.stringify(value)})`);
  }
  return value;
}

function requireNonNegativeInt32(field: string, value: number): number {
  // minApiLevel is an int32 on the wire; the C# verifier rejects anything
  // above 2^31-1 as Malformed. Cap it at signing time so the signer never
  // emits a well-signed envelope that every client rejects.
  const bounded = requireNonNegativeInt(field, value);
  if (bounded > 2147483647) {
    throw new TypeError(`${field} must be at most 2147483647 (int32), got ${bounded}`);
  }
  return bounded;
}

function base64UrlToBase64(value: string): string {
  const padded = value.replace(/-/g, "+").replace(/_/g, "/");
  return padded + "=".repeat((4 - (padded.length % 4)) % 4);
}
