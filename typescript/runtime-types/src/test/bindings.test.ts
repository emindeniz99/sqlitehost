import assert from "node:assert/strict";
import { test } from "node:test";

import {
  blob,
  bool,
  encodeBase64,
  int32,
  int64,
  int64FromBigInt,
  int64ToBigInt,
  isValidBase64,
  nullValue,
  text,
} from "../index.js";

test("int64ToBigInt accepts the envelope's number-or-string forms", () => {
  assert.equal(int64ToBigInt(42), 42n);
  assert.equal(int64ToBigInt("-7"), -7n);
  assert.equal(int64ToBigInt("9223372036854775807"), 9223372036854775807n);
  assert.equal(int64ToBigInt("-9223372036854775808"), -9223372036854775808n);
});

test("int64ToBigInt rejects unsafe numbers, garbage, and overflow", () => {
  assert.throws(() => int64ToBigInt(1.5), RangeError);
  assert.throws(() => int64ToBigInt(2 ** 53), RangeError);
  assert.throws(() => int64ToBigInt("1e3"), RangeError);
  assert.throws(() => int64ToBigInt("9223372036854775808"), RangeError);
});

test("int64FromBigInt emits number below 2^53 and string above", () => {
  assert.equal(int64FromBigInt(42n), 42);
  assert.equal(int64FromBigInt(-9007199254740991n), -9007199254740991);
  assert.equal(int64FromBigInt(9007199254740992n), "9007199254740992");
  assert.equal(int64FromBigInt(-9223372036854775808n), "-9223372036854775808");
  assert.throws(() => int64FromBigInt(2n ** 63n), RangeError);
});

test("base64 validation follows the envelope contract", () => {
  assert.ok(isValidBase64("3q2+7w=="));
  assert.ok(isValidBase64(""));
  assert.ok(isValidBase64("AAECAw=="));
  assert.ok(!isValidBase64("3q2+7w")); // missing padding
  assert.ok(!isValidBase64("3q2+\n7w==")); // line break
  assert.ok(!isValidBase64("3q2-7w==")); // url-safe alphabet
  assert.ok(!isValidBase64("=AAA"));
});

test("encodeBase64 produces standard base64", () => {
  assert.equal(encodeBase64(new Uint8Array([0xde, 0xad, 0xbe, 0xef])), "3q2+7w==");
  assert.equal(encodeBase64(new Uint8Array([])), "");
  assert.equal(encodeBase64(new Uint8Array([0, 1, 2, 3])), "AAECAw==");
});

test("binding constructors emit canonical BindingValue shapes", () => {
  assert.deepEqual(nullValue(), { type: "null" });
  assert.deepEqual(text("read-1"), { type: "text", value: "read-1" });
  assert.deepEqual(bool(true), { type: "bool", value: true });
  assert.deepEqual(int32("5"), { type: "int32", value: 5 });
  assert.deepEqual(int64(7), { type: "int64", value: 7 });
  assert.deepEqual(int64(9007199254740992n), {
    type: "int64",
    value: "9007199254740992",
  });
  assert.deepEqual(blob("3q2+7w=="), { type: "blob", value: "3q2+7w==" });
  assert.deepEqual(blob(new Uint8Array([0xde, 0xad, 0xbe, 0xef])), {
    type: "blob",
    value: "3q2+7w==",
  });
  assert.throws(() => blob("not base64!"), RangeError);
  assert.throws(() => int32(2147483648), RangeError);
});
