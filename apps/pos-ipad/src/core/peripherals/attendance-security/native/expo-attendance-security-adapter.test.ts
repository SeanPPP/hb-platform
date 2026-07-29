import assert from "node:assert/strict";
import test from "node:test";

import { ExpoAttendanceSecurityAdapter } from "./expo-attendance-security-adapter";
import type {
  HbAttendanceSecurityNativeModule,
  NativeEmergencyVerificationInput,
} from "./types";

const HANDLE = "11111111-2222-4333-8444-555555555555";
const KID = "AQIDBAUGBwgJCg";
const KEY_MATERIAL = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
const PUBLIC_KEY_PEM =
  "-----BEGIN PUBLIC KEY-----\n" +
  "A".repeat(96) +
  "\n-----END PUBLIC KEY-----";
const FINGERPRINT = "A".repeat(64);

function nativeStub(
  overrides: Partial<HbAttendanceSecurityNativeModule> = {},
): HbAttendanceSecurityNativeModule {
  return {
    getSystemUptimeMilliseconds() {
      return 123_456;
    },
    async createA256Identity() {
      return { keyHandle: HANDLE, kid: KID };
    },
    async destroyA256Key() {},
    async hasA256Key() {
      return true;
    },
    async issueAttendanceQr() {
      return { imageUri: "data:image/png;base64,AQ==" };
    },
    async readRegistrationKeyMaterial() {
      return KEY_MATERIAL;
    },
    async validateEs256P256PublicKey() {
      return true;
    },
    async verifyEs256P256Token() {
      return {
        claims: {
          expiresAtEpochMs: 2_000,
          grantId: "11111111-2222-4333-8444-555555555555",
          notBeforeEpochMs: 1_000,
          storeCode: "S001",
        },
        ok: true,
      };
    },
    ...overrides,
  };
}

test("creates an opaque identity and rejects unexpected native fields", async () => {
  const adapter = new ExpoAttendanceSecurityAdapter(nativeStub());

  const identity = await adapter.createA256Identity();

  assert.deepEqual(identity, { keyHandle: HANDLE, kid: KID });
  assert.equal(Object.isFrozen(identity), true);

  const malformed = new ExpoAttendanceSecurityAdapter(
    nativeStub({
      async createA256Identity() {
        return { keyHandle: HANDLE, kid: KID, keyMaterial: "leak" };
      },
    }),
  );
  await assert.rejects(() => malformed.createA256Identity());
});

test("exposes registration material only to the callback and validates A256 length", async () => {
  const adapter = new ExpoAttendanceSecurityAdapter(nativeStub());
  const observed: string[] = [];

  const result = await adapter.withRegistrationKey(HANDLE, async (material) => {
    observed.push(material);
    return "registered";
  });

  assert.equal(result, "registered");
  assert.deepEqual(observed, [KEY_MATERIAL]);

  const malformed = new ExpoAttendanceSecurityAdapter(
    nativeStub({
      async readRegistrationKeyMaterial() {
        return "too-short";
      },
    }),
  );
  await assert.rejects(() =>
    malformed.withRegistrationKey(HANDLE, async () => undefined),
  );
});

test("validates attendance arguments and image-only native result", async () => {
  let received: unknown;
  const adapter = new ExpoAttendanceSecurityAdapter(
    nativeStub({
      async issueAttendanceQr(input) {
        received = input;
        return { imageUri: "data:image/png;base64,AQ==" };
      },
    }),
  );

  const result = await adapter.issueAttendanceQr({
    deviceCode: "POS01",
    issuedAtEpochMs: 1_753_660_800_000,
    keyHandle: HANDLE,
    kid: KID,
    storeCode: "S001",
  });

  assert.deepEqual(received, {
    deviceCode: "POS01",
    issuedAtEpochMs: 1_753_660_800_000,
    keyHandle: HANDLE,
    kid: KID,
    storeCode: "S001",
  });
  assert.deepEqual(result, { imageUri: "data:image/png;base64,AQ==" });
  assert.equal(Object.isFrozen(result), true);

  await assert.rejects(() =>
    adapter.issueAttendanceQr({
      deviceCode: " POS01",
      issuedAtEpochMs: 1,
      keyHandle: HANDLE,
      kid: KID,
      storeCode: "S001",
    }),
  );
});

test("validates opaque handles and native boolean results", async () => {
  const adapter = new ExpoAttendanceSecurityAdapter(nativeStub());

  assert.equal(await adapter.hasA256Key(HANDLE), true);
  await adapter.destroyKey(HANDLE);
  await assert.rejects(() => adapter.hasA256Key("not-a-handle"));

  const malformed = new ExpoAttendanceSecurityAdapter(
    nativeStub({
      async hasA256Key() {
        return "yes";
      },
    }),
  );
  await assert.rejects(() => malformed.hasA256Key(HANDLE));
});

test("同步读取系统 uptime 并严格拒绝非负 safe integer 之外的原生结果", () => {
  const values: unknown[] = [
    123_456,
    0,
    -1,
    1.5,
    Number.POSITIVE_INFINITY,
    Number.MAX_SAFE_INTEGER + 1,
    "123456",
  ];
  const native = nativeStub();
  native.getSystemUptimeMilliseconds = () => values.shift();
  const adapter = new ExpoAttendanceSecurityAdapter(native);
  const readUptime =
    adapter.getSystemUptimeMilliseconds.bind(adapter);

  assert.equal(readUptime(), 123_456);
  assert.equal(readUptime(), 0);
  for (let index = 0; index < 5; index += 1) {
    assert.throws(
      () => readUptime(),
      /system uptime|native|bridge/i,
    );
  }
});

test("validates P-256 key input before crossing the bridge", async () => {
  let calls = 0;
  const adapter = new ExpoAttendanceSecurityAdapter(
    nativeStub({
      async validateEs256P256PublicKey() {
        calls += 1;
        return true;
      },
    }),
  );

  assert.equal(
    await adapter.validateEs256P256PublicKey({
      algorithm: "ES256",
      fingerprintHex: FINGERPRINT,
      kid: "K1",
      publicKeyPem: PUBLIC_KEY_PEM,
    }),
    true,
  );
  assert.equal(calls, 1);
  assert.equal(
    await adapter.validateEs256P256PublicKey({
      algorithm: "ES256",
      fingerprintHex: "00",
      kid: "K1",
      publicKeyPem: PUBLIC_KEY_PEM,
    }),
    false,
  );
  assert.equal(calls, 1);
});

test("returns strictly validated immutable emergency claims", async () => {
  let received: NativeEmergencyVerificationInput | undefined;
  const adapter = new ExpoAttendanceSecurityAdapter(
    nativeStub({
      async verifyEs256P256Token(input) {
        received = input;
        return {
          claims: {
            expiresAtEpochMs: 2_000,
            grantId: "11111111-2222-4333-8444-555555555555",
            notBeforeEpochMs: 1_000,
            storeCode: "S001",
          },
          ok: true,
        };
      },
    }),
  );
  const key = {
    algorithm: "ES256" as const,
    fingerprintHex: FINGERPRINT,
    kid: "K1",
    publicKeyPem: PUBLIC_KEY_PEM,
  };

  const result = await adapter.verifyEs256P256Token({
    expectedStoreCode: "S001",
    nowEpochMs: 1_500,
    publicKeys: [key],
    token: `HBPOSE2-${"A".repeat(150)}`,
  });

  assert.equal(result.ok, true);
  assert.equal(Object.isFrozen(result), true);
  if (result.ok) {
    assert.equal(Object.isFrozen(result.claims), true);
    assert.equal(result.claims.grantId, HANDLE);
  }
  assert.deepEqual(received?.publicKeys, [key]);
});

test("accepts only stable failure codes and rejects malformed native responses", async () => {
  const failed = new ExpoAttendanceSecurityAdapter(
    nativeStub({
      async verifyEs256P256Token() {
        return {
          errorCode: "EMERGENCY_TOKEN_SIGNATURE_INVALID",
          ok: false,
        };
      },
    }),
  );
  const input = {
    expectedStoreCode: "S001",
    nowEpochMs: 1_500,
    publicKeys: [],
    token: "HBPOSE1-K1-00-00",
  };

  assert.deepEqual(await failed.verifyEs256P256Token(input), {
    errorCode: "EMERGENCY_TOKEN_SIGNATURE_INVALID",
    ok: false,
  });

  const malformed = new ExpoAttendanceSecurityAdapter(
    nativeStub({
      async verifyEs256P256Token() {
        return { errorCode: "NATIVE_SECRET_ERROR", ok: false };
      },
    }),
  );
  await assert.rejects(() => malformed.verifyEs256P256Token(input));
});
