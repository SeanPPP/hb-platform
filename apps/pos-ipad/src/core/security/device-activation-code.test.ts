import assert from "node:assert/strict";
import test from "node:test";

import {
  normalizeDeviceActivationCode,
  parseDeviceActivationCode,
} from "./device-activation-code";

const validCode =
  "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";

test("HBDEV1 开通码统一去除 ASCII 空白并转为大写", () => {
  assert.equal(
    normalizeDeviceActivationCode(` \t${validCode.toLowerCase()}\r\n `),
    validCode,
  );
  assert.equal(parseDeviceActivationCode(validCode.toLowerCase()), validCode);
});

test("只接受完整的 HBDEV1 Crockford 双段原文", () => {
  for (const invalid of [
    "",
    "HBDEV1-TOO-SHORT",
    validCode.replace("HBDEV1", "HBDEV2"),
    validCode.replace("0123456789", "I123456789"),
    validCode.replace("STVWXYZ", "UTVWXYZ"),
    `${validCode}\u00a0`,
    validCode.replace("S", "ſ"),
    `${validCode}\u0000`,
  ]) {
    assert.equal(parseDeviceActivationCode(invalid), null, invalid);
  }
});
