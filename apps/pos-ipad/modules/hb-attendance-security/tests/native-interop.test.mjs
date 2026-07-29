import assert from "node:assert/strict";
import { Buffer } from "node:buffer";
import { spawnSync } from "node:child_process";
import {
  createHash,
  createDecipheriv,
  generateKeyPairSync,
  sign,
} from "node:crypto";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const testsDirectory = path.dirname(fileURLToPath(import.meta.url));
const moduleRoot = path.resolve(testsDirectory, "..");

function sha256(value) {
  return createHash("sha256").update(value).digest();
}

function uuidBytes(value) {
  return Buffer.from(value.replaceAll("-", ""), "hex");
}

function base64Url(value) {
  return Buffer.from(value).toString("base64url");
}

function dotnetUtcDate(value) {
  return new Date(value)
    .toISOString()
    .replace(/(\.\d{3})Z$/u, "$10000Z");
}

test("Swift verifier accepts WPF-compatible HBPOSE1 and HBPOSE2 ES256 payloads", async () => {
  const temporaryDirectory = await mkdtemp(
    path.join(tmpdir(), "hb-attendance-security-"),
  );
  try {
    const binary = path.join(temporaryDirectory, "interop");
    const compile = spawnSync(
      "xcrun",
      [
        "swiftc",
        path.join(
          moduleRoot,
          "ios/HBEmergencyLoginVerifier.swift",
        ),
        path.join(testsDirectory, "native-interop-harness.swift"),
        "-o",
        binary,
      ],
      { encoding: "utf8" },
    );
    assert.equal(
      compile.status,
      0,
      `Swift interoperability harness failed to compile: ${compile.stderr}`,
    );

    const kid = "K20260728";
    const storeCode = "S001";
    const grantId = "11111111-2222-4333-8444-555555555555";
    const nowEpochMs = 1_753_660_800_000;
    const notBeforeEpochMs = nowEpochMs - 60_000;
    const expiresAtEpochMs = nowEpochMs + 300_000;
    const { privateKey, publicKey } = generateKeyPairSync("ec", {
      namedCurve: "prime256v1",
    });
    const publicKeyDer = publicKey.export({
      format: "der",
      type: "spki",
    });
    const publicKeyPem = publicKey.export({
      format: "pem",
      type: "spki",
    });
    const fingerprintHex = sha256(publicKeyDer)
      .toString("hex")
      .toUpperCase();

    const legacyPayload = Buffer.from(
      JSON.stringify({
        grantId,
        storeCode,
        businessDate: "2025-07-28",
        permissionProfile: "AllPosTerminal",
        issuer: "HB POS emergency access",
        audience: "Hbpos.Wpf",
        issuedAtUtc: dotnetUtcDate(notBeforeEpochMs),
        notBeforeUtc: dotnetUtcDate(notBeforeEpochMs),
        expiresAtUtc: dotnetUtcDate(expiresAtEpochMs),
      }),
      "utf8",
    );
    const legacyHeader = Buffer.from(`HBPOSE1-${kid}-`, "ascii");
    const legacySignature = sign(
      "sha256",
      Buffer.concat([legacyHeader, legacyPayload]),
      { dsaEncoding: "ieee-p1363", key: privateKey },
    );
    const legacyToken = [
      "HBPOSE1",
      kid,
      legacyPayload.toString("hex").toUpperCase(),
      legacySignature.toString("hex").toUpperCase(),
    ].join("-");

    const compactClaims = Buffer.alloc(48);
    sha256(Buffer.from(kid, "ascii")).copy(compactClaims, 0, 0, 8);
    uuidBytes(grantId).copy(compactClaims, 8);
    sha256(Buffer.from(storeCode, "utf8")).copy(
      compactClaims,
      24,
      0,
      16,
    );
    compactClaims.writeUInt32BE(notBeforeEpochMs / 1_000, 40);
    compactClaims.writeUInt32BE(expiresAtEpochMs / 1_000, 44);
    const compactSignature = sign(
      "sha256",
      Buffer.concat([
        Buffer.from("HBPOSE2-", "ascii"),
        compactClaims,
      ]),
      { dsaEncoding: "ieee-p1363", key: privateKey },
    );
    const v2Token =
      "HBPOSE2-" +
      base64Url(Buffer.concat([compactClaims, compactSignature]));
    assert.equal(v2Token.length, 158);

    const execute = spawnSync(binary, {
      encoding: "utf8",
      input: JSON.stringify({
        expectedStoreCode: storeCode,
        fingerprintHex,
        kid,
        legacyToken,
        nowEpochMs,
        publicKeyPem,
        v2Token,
      }),
    });
    assert.equal(
      execute.status,
      0,
      `Swift interoperability harness failed: ${execute.stderr}`,
    );
    const result = JSON.parse(execute.stdout);
    assert.equal(result.keyValid, true);
    assert.deepEqual(result.legacy, {
      expiresAtEpochMs: String(expiresAtEpochMs),
      grantId,
      notBeforeEpochMs: String(notBeforeEpochMs),
      ok: "true",
      storeCode,
    });
    assert.deepEqual(result.v2, {
      expiresAtEpochMs: String(expiresAtEpochMs),
      grantId,
      notBeforeEpochMs: String(notBeforeEpochMs),
      ok: "true",
      storeCode,
    });
  } finally {
    await rm(temporaryDirectory, { force: true, recursive: true });
  }
});

test("Swift attendance issuer produces the WPF HBATE1 AES-GCM payload layout", async () => {
  const temporaryDirectory = await mkdtemp(
    path.join(tmpdir(), "hb-attendance-token-"),
  );
  try {
    const binary = path.join(temporaryDirectory, "attendance-token");
    const compile = spawnSync(
      "xcrun",
      [
        "swiftc",
        path.join(moduleRoot, "ios/HBAttendanceTokenCodec.swift"),
        path.join(
          testsDirectory,
          "attendance-token-interop-harness.swift",
        ),
        "-o",
        binary,
      ],
      { encoding: "utf8" },
    );
    assert.equal(
      compile.status,
      0,
      `Swift attendance token harness failed to compile: ${compile.stderr}`,
    );

    const key = Buffer.from(
      "000102030405060708090a0b0c0d0e0f" +
        "101112131415161718191a1b1c1d1e1f",
      "hex",
    );
    const nonce = Buffer.from("202122232425262728292a2b", "hex");
    const kid = "AQIDBAUGBwgJCg";
    const storeCode = "S001";
    const deviceCode = "POS01";
    const tokenId = "00112233-4455-4677-8899-aabbccddeeff";
    const issuedAtEpochMs = 1_753_660_800_000;
    const execute = spawnSync(binary, {
      encoding: "utf8",
      input: JSON.stringify({
        deviceCode,
        issuedAtEpochMs,
        keyBase64Url: base64Url(key),
        kid,
        nonceBase64Url: base64Url(nonce),
        storeCode,
        tokenId,
      }),
    });
    assert.equal(
      execute.status,
      0,
      `Swift attendance token harness failed: ${execute.stderr}`,
    );

    const parts = execute.stdout.split(".");
    assert.equal(parts.length, 5);
    assert.equal(parts[0], "HBATE1");
    assert.equal(parts[1], kid);
    assert.deepEqual(Buffer.from(parts[2], "base64url"), nonce);
    const ciphertext = Buffer.from(parts[3], "base64url");
    const tag = Buffer.from(parts[4], "base64url");
    const decipher = createDecipheriv("aes-256-gcm", key, nonce);
    decipher.setAAD(Buffer.from(`HBATE1.${kid}`, "ascii"));
    decipher.setAuthTag(tag);
    const plaintext = Buffer.concat([
      decipher.update(ciphertext),
      decipher.final(),
    ]);

    const expected = Buffer.concat([
      Buffer.from([1]),
      Buffer.from("33221100554477468899aabbccddeeff", "hex"),
      Buffer.from("003c544e98010000", "hex"),
      Buffer.from([storeCode.length]),
      Buffer.from(storeCode, "utf8"),
      Buffer.from([deviceCode.length]),
      Buffer.from(deviceCode, "utf8"),
    ]);
    assert.deepEqual(plaintext, expected);
  } finally {
    await rm(temporaryDirectory, { force: true, recursive: true });
  }
});
