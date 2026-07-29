import assert from "node:assert/strict";
import test from "node:test";

import { ALL_POS_TERMINAL_PERMISSIONS } from "../contracts/pos-terminal-permissions";

import { createEmergencyCashierRuntime } from "./emergency-cashier-runtime";

import type {
  EmergencyLoginCryptoPort,
  EmergencyPublicKeyCachePort,
  EmergencyTrustedTimeAnchor,
  EmergencyTrustedTimePort,
} from "@/features/attendance-audit/emergency-login-security";
import type {
  AttendanceSecurityRemotePort,
  EmergencyPublicKeyPackage,
} from "@/features/attendance-audit/hbpos-attendance-security-api";

const PACKAGE: EmergencyPublicKeyPackage = {
  version: 1,
  activeKeyId: "KEY01",
  generatedAtEpochMs: 1,
  keys: [
    {
      kid: "KEY01",
      algorithm: "ES256",
      publicKeyPem:
        `-----BEGIN PUBLIC KEY-----\n${"A".repeat(96)}\n-----END PUBLIC KEY-----`,
      fingerprintHex: "A".repeat(64),
    },
  ],
};

test("紧急收银运行时只公开完整 POS 权限快照并将 token 写入 Keychain Port", async () => {
  let authorization: unknown = null;
  let trustedTime: EmergencyTrustedTimeAnchor | null = null;
  let systemUptimeMs = 10_000;
  const cache: EmergencyPublicKeyCachePort = {
    read: async () => PACKAGE,
    replace: async () => undefined,
  };
  const crypto: EmergencyLoginCryptoPort = {
    validateEs256P256PublicKey: async () => true,
    verifyEs256P256Token: async () => ({
      ok: true,
      claims: {
        expiresAtEpochMs: 10_000,
        grantId: "10000000-0000-4000-8000-000000000001",
        notBeforeEpochMs: 500,
        storeCode: "S1",
      },
    }),
  };
  const trusted: EmergencyTrustedTimePort = {
    readAnchor: async () => trustedTime,
    replaceAnchor: async (value) => {
      trustedTime = value;
    },
  };
  const remote = synchronizingRemote();
  const runtime = createEmergencyCashierRuntime({
    authorization: {
      set: async (value) => {
        authorization = value;
      },
    },
    cache,
    crypto,
    remote,
    systemUptime: {
      getSystemUptimeMilliseconds: () => systemUptimeMs,
    },
    trustedTime: trusted,
  });

  assert.equal(await runtime.syncPublicKeys(), true);
  const result =
    await runtime.authentication.service.verifyAndActivate(
      "HBPOSE2-signed",
      { storeCode: "S1", deviceCode: "IPAD-1" },
    );

  assert.equal(result.ok, true);
  assert.deepEqual(authorization, {
    authorizationToken: "HBPOSE2-signed",
    expiresAtEpochMs: 10_000,
    source: "emergency-override",
    systemUptimeMs: 10_000,
    trustedNowEpochMs: 6_000,
  });
  assert.deepEqual(trustedTime, {
    serverEpochMs: 1_000,
    systemUptimeMs: 10_000,
  });
  assert.deepEqual(
    runtime.authentication.permissionCodes,
    ALL_POS_TERMINAL_PERMISSIONS,
  );
  assert.equal(
    Object.isFrozen(runtime.authentication.permissionCodes),
    true,
  );
});

function synchronizingRemote(): AttendanceSecurityRemotePort {
  return {
    registerAttendanceKey: async () => {
      throw new Error("not used");
    },
    fetchEmergencyPublicKeys: async () => ({
      kind: "not-modified",
    }),
    acknowledgeEmergencyPublicKeys: async () => ({
      acknowledged: true,
      serverTimeEpochMs: 1_000,
      serverVersion: 1,
    }),
  };
}
