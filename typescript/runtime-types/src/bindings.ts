/**
 * Handwritten binding value helpers: typed constructors for the
 * generated BindingValue union, int64 normalization (number | string
 * <-> bigint), and base64 validation per docs/script-envelope.md.
 */

import type {
  BlobBindingValue,
  BoolBindingValue,
  Int32BindingValue,
  Int64BindingValue,
  NullBindingValue,
  TextBindingValue,
} from "./generated/envelope.js";

export const INT32_MIN = -2147483648;
export const INT32_MAX = 2147483647;
export const INT64_MIN = -(2n ** 63n);
export const INT64_MAX = 2n ** 63n - 1n;

/** Largest int64 magnitude that the JSON number form may carry (2^53 - 1). */
export const INT64_SAFE_NUMBER_MAX = 9007199254740991n;

const DECIMAL_STRING = /^-?[0-9]+$/;

/**
 * Normalize an int64 JSON value (number when |v| <= 2^53 - 1, else
 * decimal string) into a bigint. Throws RangeError for non-integers,
 * malformed strings, or values outside int64 range.
 */
export function int64ToBigInt(value: number | string): bigint {
  let result: bigint;
  if (typeof value === "number") {
    if (!Number.isSafeInteger(value)) {
      throw new RangeError(
        `int64 number values must be safe integers (|v| <= 2^53 - 1), got ${value}`,
      );
    }
    result = BigInt(value);
  } else {
    if (!DECIMAL_STRING.test(value)) {
      throw new RangeError(`int64 string values must be decimal, got ${JSON.stringify(value)}`);
    }
    result = BigInt(value);
  }
  if (result < INT64_MIN || result > INT64_MAX) {
    throw new RangeError(`value ${result} is outside int64 range`);
  }
  return result;
}

/**
 * Convert a bigint to the canonical int64 JSON representation: a number
 * when |v| <= 2^53 - 1, else a decimal string. Throws RangeError when
 * the value is outside int64 range.
 */
export function int64FromBigInt(value: bigint): number | string {
  if (value < INT64_MIN || value > INT64_MAX) {
    throw new RangeError(`value ${value} is outside int64 range`);
  }
  if (value >= -INT64_SAFE_NUMBER_MAX && value <= INT64_SAFE_NUMBER_MAX) {
    return Number(value);
  }
  return value.toString();
}

/**
 * Normalize an int32 JSON value (number or decimal string in int32
 * range) into a number. Throws RangeError otherwise.
 */
export function int32ToNumber(value: number | string): number {
  let result: number;
  if (typeof value === "number") {
    if (!Number.isInteger(value)) {
      throw new RangeError(`int32 values must be integers, got ${value}`);
    }
    result = value;
  } else {
    if (!DECIMAL_STRING.test(value)) {
      throw new RangeError(`int32 string values must be decimal, got ${JSON.stringify(value)}`);
    }
    result = Number(value);
  }
  if (result < INT32_MIN || result > INT32_MAX) {
    throw new RangeError(`value ${value} is outside int32 range`);
  }
  return result;
}

const BASE64_SHAPE = /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/;

/**
 * True when the string is valid standard base64: standard alphabet,
 * correct `=` padding, no line breaks or whitespace.
 */
export function isValidBase64(value: string): boolean {
  return BASE64_SHAPE.test(value);
}

const BASE64_ALPHABET =
  "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

/** Encode bytes as standard base64 (with padding, no line breaks). */
export function encodeBase64(bytes: Uint8Array): string {
  let out = "";
  for (let i = 0; i < bytes.length; i += 3) {
    const b0 = bytes[i];
    const b1 = i + 1 < bytes.length ? bytes[i + 1] : 0;
    const b2 = i + 2 < bytes.length ? bytes[i + 2] : 0;
    out += BASE64_ALPHABET[b0 >> 2];
    out += BASE64_ALPHABET[((b0 & 0x03) << 4) | (b1 >> 4)];
    out += i + 1 < bytes.length ? BASE64_ALPHABET[((b1 & 0x0f) << 2) | (b2 >> 6)] : "=";
    out += i + 2 < bytes.length ? BASE64_ALPHABET[b2 & 0x3f] : "=";
  }
  return out;
}

/** `{ type: "null" }` — SQLite NULL. */
export function nullValue(): NullBindingValue {
  return { type: "null" };
}

/** Typed int32 binding; validates range, normalizes to a number. */
export function int32(value: number | string): Int32BindingValue {
  return { type: "int32", value: int32ToNumber(value) };
}

/**
 * Typed int64 binding; validates range, normalizes to the canonical
 * JSON representation (number when |v| <= 2^53 - 1, else string).
 */
export function int64(value: number | string | bigint): Int64BindingValue {
  const big = typeof value === "bigint" ? value : int64ToBigInt(value);
  return { type: "int64", value: int64FromBigInt(big) };
}

/** Typed bool binding. */
export function bool(value: boolean): BoolBindingValue {
  return { type: "bool", value };
}

/** Typed text binding. */
export function text(value: string): TextBindingValue {
  return { type: "text", value };
}

/**
 * Typed blob binding. Accepts a base64 string (validated) or raw bytes
 * (encoded to base64). Throws RangeError on malformed base64.
 */
export function blob(value: string | Uint8Array): BlobBindingValue {
  if (typeof value === "string") {
    if (!isValidBase64(value)) {
      throw new RangeError(`blob values must be valid standard base64, got ${JSON.stringify(value)}`);
    }
    return { type: "blob", value };
  }
  return { type: "blob", value: encodeBase64(value) };
}
