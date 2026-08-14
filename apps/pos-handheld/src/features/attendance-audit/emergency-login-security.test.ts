import assert from "node:assert/strict";
import test from "node:test";

import {
  EmergencyLoginSecurityService,
  EmergencyPublicKeySyncService,
  type EmergencyLoginCryptoPort,
  type EmergencyLoginSessionPort,
  type EmergencyPublicKeyCachePort,
  type EmergencySystemUptimePort,
  type EmergencyTrustedTimeAnchor,
  type EmergencyTrustedTimePort,
} from "./emergency-login-security";
import type {
  AttendanceSecurityRemotePort,
  EmergencyPublicKey,
  EmergencyPublicKeyAckResult,
  EmergencyPublicKeyFetchResult,
  EmergencyPublicKeyPackage,
  RegisteredAttendanceSigningKey,
} from "./hbpos-attendance-security-api";

class FakeRemote implements AttendanceSecurityRemotePort {
  public readonly fetchVersions: (number | null)[] = [];
  public readonly acknowledgements: number[] = [];
  public ackResponseEffects: (() => void)[] = [];
  public fetches: EmergencyPublicKeyFetchResult[] = [];
  public acks: EmergencyPublicKeyAckResult[] = [];

  public async registerAttendanceKey(): Promise<RegisteredAttendanceSigningKey> {
    throw new Error("not used");
  }

  public async fetchEmergencyPublicKeys(
    currentVersion: number | null,
  ): Promise<EmergencyPublicKeyFetchResult> {
    this.fetchVersions.push(currentVersion);
    const result = this.fetches.shift();
    if (!result) throw new Error("missing fetch");
    return result;
  }

  public async acknowledgeEmergencyPublicKeys(
    version: number,
  ): Promise<EmergencyPublicKeyAckResult> {
    this.acknowledgements.push(version);
    const result = this.acks.shift();
    if (!result) throw new Error("missing ack");
    this.ackResponseEffects.shift()?.();
    return result;
  }
}

class FakeKeyCache implements EmergencyPublicKeyCachePort {
  public value: EmergencyPublicKeyPackage | null = null;
  public readonly replacements: EmergencyPublicKeyPackage[] = [];

  public async read(): Promise<EmergencyPublicKeyPackage | null> {
    return this.value;
  }

  public async replace(value: EmergencyPublicKeyPackage): Promise<void> {
    this.value = value;
    this.replacements.push(value);
  }
}

class FakeCrypto implements EmergencyLoginCryptoPort {
  public invalidKids = new Set<string>();
  public readonly validated: string[] = [];
  public readonly verifies: string[] = [];
  public readonly verifyNowEpochMs: number[] = [];
  public verifyResults: {
    errorCode: string;
    ok: boolean;
    claims?: {
      expiresAtEpochMs: number;
      grantId: string;
      notBeforeEpochMs: number;
      storeCode: string;
    };
  }[] = [];

  public async validateEs256P256PublicKey(
    key: EmergencyPublicKey,
  ): Promise<boolean> {
    this.validated.push(key.kid);
    return !this.invalidKids.has(key.kid);
  }

  public async verifyEs256P256Token(input: {
    nowEpochMs: number;
    token: string;
  }) {
    this.verifies.push(input.token);
    this.verifyNowEpochMs.push(input.nowEpochMs);
    const result = this.verifyResults.shift();
    if (!result) throw new Error("missing verify result");
    return result.ok && result.claims
      ? { ok: true as const, claims: result.claims }
      : {
          ok: false as const,
          errorCode: result.errorCode,
        };
  }
}

class FakeTrustedTime implements EmergencyTrustedTimePort {
  public value: EmergencyTrustedTimeAnchor | null = null;
  public readonly replacements: EmergencyTrustedTimeAnchor[] = [];

  public async readAnchor(): Promise<EmergencyTrustedTimeAnchor | null> {
    return this.value;
  }

  public async replaceAnchor(
    value: EmergencyTrustedTimeAnchor,
  ): Promise<void> {
    this.value = value;
    this.replacements.push(value);
  }
}

class FakeSystemUptime implements EmergencySystemUptimePort {
  public value = 10_000;

  public getSystemUptimeMilliseconds(): number {
    return this.value;
  }
}

class FakeSession implements EmergencyLoginSessionPort {
  public readonly activations: {
    authorizationToken: string;
    deviceCode: string;
    emergencyGrantId: string;
    expiresAtEpochMs: number;
    storeCode: string;
    systemUptimeMs: number;
    trustedNowEpochMs: number;
  }[] = [];

  public async activateEmergencyOverride(
    input: (typeof this.activations)[number],
  ): Promise<void> {
    this.activations.push(input);
  }
}

test("公钥同步先完整验证 P-256 指纹，再原子替换并 ACK", async () => {
  const fixture = createSyncFixture();
  fixture.cache.value = corruptPackage();
  fixture.remote.fetches.push({
    kind: "changed",
    package: keyPackage(2),
  });
  fixture.remote.acks.push({
    acknowledged: true,
    serverVersion: 2,
    serverTimeEpochMs: Date.parse("2026-07-28T01:00:00.000Z"),
  });

  const synced = await fixture.sync.sync();

  assert.equal(synced, true);
  assert.deepEqual(fixture.remote.fetchVersions, [null]);
  assert.deepEqual(fixture.crypto.validated, ["KEY01"]);
  assert.equal(fixture.cache.replacements[0]?.version, 2);
  assert.deepEqual(fixture.remote.acknowledgements, [2]);
  assert.deepEqual(fixture.trusted.replacements, [
    {
      serverEpochMs: Date.parse("2026-07-28T01:00:00.000Z"),
      systemUptimeMs: 10_000,
    },
  ]);
});

test("延迟 ACK 以响应 uptime 建立下界，临近到期 token 不会多活一个 RTT", async () => {
  const fixture = createSyncFixture();
  const serverTimeEpochMs = Date.parse(
    "2026-07-28T01:00:00.000Z",
  );
  fixture.cache.value = keyPackage(1);
  fixture.remote.fetches.push({ kind: "not-modified" });
  fixture.remote.acks.push({
    acknowledged: true,
    serverVersion: 1,
    serverTimeEpochMs,
  });
  fixture.remote.ackResponseEffects.push(() => {
    fixture.systemUptime.value += 1_200;
  });

  assert.equal(await fixture.sync.sync(), true);
  assert.deepEqual(fixture.trusted.value, {
    serverEpochMs: serverTimeEpochMs,
    systemUptimeMs: 11_200,
  });

  fixture.crypto.verifyResults.push({
    errorCode: "",
    ok: true,
    claims: {
      expiresAtEpochMs: serverTimeEpochMs + 1_000,
      grantId: "10000000-0000-4000-8000-000000000001",
      notBeforeEpochMs: serverTimeEpochMs - 1_000,
      storeCode: "S1",
    },
  });
  const session = new FakeSession();
  const login = new EmergencyLoginSecurityService({
    cache: fixture.cache,
    crypto: fixture.crypto,
    session,
    sync: fixture.sync,
    systemUptime: fixture.systemUptime,
    trustedTime: fixture.trusted,
  });

  assert.deepEqual(
    await login.verifyAndActivate("HBPOSE2-near-expiry", {
      deviceCode: "IPAD-1",
      storeCode: "S1",
    }),
    {
      errorCode: "EMERGENCY_TOKEN_INVALID",
      ok: false,
    },
  );
  assert.deepEqual(fixture.crypto.verifyNowEpochMs, [
    serverTimeEpochMs,
  ]);
  assert.equal(session.activations.length, 0);
});

test("同一 boot 的 ACK 锚点推进到响应 uptime 时保留旧锚点推导下界", async () => {
  const fixture = createSyncFixture();
  fixture.cache.value = keyPackage(1);
  fixture.trusted.value = {
    serverEpochMs: 1_000,
    systemUptimeMs: 100,
  };
  fixture.systemUptime.value = 108;
  fixture.remote.fetches.push({ kind: "not-modified" });
  fixture.remote.acks.push({
    acknowledged: true,
    serverVersion: 1,
    serverTimeEpochMs: 1_008,
  });
  fixture.remote.ackResponseEffects.push(() => {
    fixture.systemUptime.value = 110;
  });

  assert.equal(await fixture.sync.sync(), true);
  assert.deepEqual(fixture.trusted.value, {
    serverEpochMs: 1_010,
    systemUptimeMs: 110,
  });
});

test("旧锚点推导到响应 uptime 溢出时失败关闭且不覆盖锚点", async () => {
  const fixture = createSyncFixture();
  const original = {
    serverEpochMs: Number.MAX_SAFE_INTEGER - 1,
    systemUptimeMs: 100,
  };
  fixture.cache.value = keyPackage(1);
  fixture.trusted.value = original;
  fixture.systemUptime.value = 101;
  fixture.remote.fetches.push({ kind: "not-modified" });
  fixture.remote.acks.push({
    acknowledged: true,
    serverVersion: 1,
    serverTimeEpochMs: Number.MAX_SAFE_INTEGER,
  });
  fixture.remote.ackResponseEffects.push(() => {
    fixture.systemUptime.value = 102;
  });

  assert.equal(await fixture.sync.sync(), false);
  assert.deepEqual(fixture.trusted.value, original);
  assert.deepEqual(fixture.trusted.replacements, []);
});

test("ACK 往返内 uptime 倒退按异常重启失败关闭", async () => {
  const fixture = createSyncFixture();
  fixture.cache.value = keyPackage(1);
  fixture.remote.fetches.push({ kind: "not-modified" });
  fixture.remote.acks.push({
    acknowledged: true,
    serverVersion: 1,
    serverTimeEpochMs: 1_000,
  });
  fixture.remote.ackResponseEffects.push(() => {
    fixture.systemUptime.value -= 1;
  });

  assert.equal(await fixture.sync.sync(), false);
  assert.equal(fixture.trusted.value, null);
  assert.deepEqual(fixture.trusted.replacements, []);
});

test("ACK 期间挂起恢复造成过大 RTT 时失败关闭且不建立锚点", async () => {
  const fixture = createSyncFixture();
  fixture.cache.value = keyPackage(1);
  fixture.remote.fetches.push({ kind: "not-modified" });
  fixture.remote.acks.push({
    acknowledged: true,
    serverVersion: 1,
    serverTimeEpochMs: Date.parse(
      "2026-07-28T01:00:00.000Z",
    ),
  });
  fixture.remote.ackResponseEffects.push(() => {
    fixture.systemUptime.value += 5_001;
  });

  assert.equal(await fixture.sync.sync(), false);
  assert.equal(fixture.trusted.value, null);
  assert.deepEqual(fixture.trusted.replacements, []);
});

test("服务端降级包不会覆盖较新缓存，坏缓存也不能参与条件请求", async () => {
  const fixture = createSyncFixture();
  fixture.cache.value = keyPackage(5);
  fixture.remote.fetches.push({
    kind: "changed",
    package: keyPackage(4),
  });

  assert.equal(await fixture.sync.sync(), false);
  assert.deepEqual(fixture.remote.fetchVersions, [5]);
  assert.equal(fixture.cache.replacements.length, 0);
  assert.equal(fixture.remote.acknowledgements.length, 0);

  const bad = createSyncFixture();
  bad.cache.value = corruptPackage();
  bad.remote.fetches.push({ kind: "not-modified" });
  assert.equal(await bad.sync.sync(), false);
  assert.deepEqual(bad.remote.fetchVersions, [null]);
});

test("ACK 冲突只允许一次无 ETag 重拉、替换与重试", async () => {
  const fixture = createSyncFixture();
  fixture.cache.value = keyPackage(7);
  fixture.remote.fetches.push(
    { kind: "not-modified" },
    { kind: "changed", package: keyPackage(8) },
  );
  fixture.remote.acks.push(
    {
      acknowledged: false,
      serverVersion: 8,
      serverTimeEpochMs: Date.parse("2026-07-28T01:00:00.000Z"),
    },
    {
      acknowledged: true,
      serverVersion: 8,
      serverTimeEpochMs: Date.parse("2026-07-28T01:00:01.000Z"),
    },
  );

  assert.equal(await fixture.sync.sync(), true);
  assert.deepEqual(fixture.remote.fetchVersions, [7, null]);
  assert.deepEqual(fixture.remote.acknowledgements, [7, 8]);
  assert.equal(fixture.cache.value?.version, 8);
  assert.equal(
    fixture.trusted.value?.serverEpochMs,
    Date.parse("2026-07-28T01:00:01.000Z"),
  );
});

test("未成功匹配版本的 ACK 不得建立可信时间锚点", async () => {
  const fixture = createSyncFixture();
  fixture.cache.value = keyPackage(7);
  fixture.remote.fetches.push(
    { kind: "not-modified" },
    { kind: "not-modified" },
  );
  fixture.remote.acks.push({
    acknowledged: false,
    serverVersion: 8,
    serverTimeEpochMs: Date.parse("2026-07-28T01:00:00.000Z"),
  });

  assert.equal(await fixture.sync.sync(), false);
  assert.equal(fixture.trusted.value, null);
  assert.deepEqual(fixture.trusted.replacements, []);
});

test("uptime 下降时仅严格前进的服务端时间可作为重启重锚", async () => {
  const rejected = createSyncFixture();
  rejected.cache.value = keyPackage(1);
  rejected.trusted.value = {
    serverEpochMs: Date.parse("2026-07-28T01:00:00.000Z"),
    systemUptimeMs: 20_000,
  };
  rejected.systemUptime.value = 1_000;
  rejected.remote.fetches.push({ kind: "not-modified" });
  rejected.remote.acks.push({
    acknowledged: true,
    serverVersion: 1,
    serverTimeEpochMs: Date.parse("2026-07-28T01:00:00.000Z"),
  });
  assert.equal(await rejected.sync.sync(), false);

  const accepted = createSyncFixture();
  accepted.cache.value = keyPackage(1);
  accepted.trusted.value = {
    serverEpochMs: Date.parse("2026-07-28T01:00:00.000Z"),
    systemUptimeMs: 20_000,
  };
  accepted.systemUptime.value = 1_000;
  accepted.remote.fetches.push({ kind: "not-modified" });
  accepted.remote.acks.push({
    acknowledged: true,
    serverVersion: 1,
    serverTimeEpochMs: Date.parse("2026-07-28T01:00:00.001Z"),
  });
  accepted.remote.ackResponseEffects.push(() => {
    accepted.systemUptime.value = 1_002;
  });
  assert.equal(await accepted.sync.sync(), true);
  assert.deepEqual(accepted.trusted.value, {
    serverEpochMs: Date.parse("2026-07-28T01:00:00.001Z"),
    systemUptimeMs: 1_002,
  });
});

test("首次没有在线服务端时间锚点时紧急登录失败关闭", async () => {
  const fixture = createLoginFixture();
  fixture.trusted.value = null;

  const result = await fixture.service.verifyAndActivate(
    "HBPOSE2-valid",
    {
      deviceCode: "IPAD-1",
      storeCode: "S1",
    },
  );

  assert.deepEqual(result, {
    errorCode: "EMERGENCY_TRUSTED_TIME_UNAVAILABLE",
    ok: false,
  });
  assert.equal(fixture.crypto.verifies.length, 0);
});

test("可信时间回拨在解析 token 前即拒绝，不能同步公钥或创建会话", async () => {
  const fixture = createLoginFixture();
  fixture.trusted.value = {
    serverEpochMs: fixture.nowMs,
    systemUptimeMs: fixture.systemUptime.value + 1,
  };

  const result = await fixture.service.verifyAndActivate(
    "HBPOSE2-valid",
    {
      deviceCode: "IPAD-1",
      storeCode: "S1",
    },
  );

  assert.deepEqual(result, {
    errorCode: "EMERGENCY_CLOCK_ROLLBACK",
    ok: false,
  });
  assert.equal(fixture.crypto.verifies.length, 0);
  assert.equal(fixture.syncCalls(), 1);
  assert.equal(fixture.session.activations.length, 0);
});

test("notBefore 位于可信时间下界与上界之间时仍拒绝，鉴签只使用下界", async () => {
  const fixture = createLoginFixture();
  fixture.cache.value = keyPackage(1);
  fixture.crypto.verifyResults.push({
    errorCode: "",
    ok: true,
    claims: {
      expiresAtEpochMs: fixture.nowMs + 60_000,
      grantId: "10000000-0000-4000-8000-000000000001",
      notBeforeEpochMs: fixture.nowMs + 1_000,
      storeCode: "S1",
    },
  });

  assert.deepEqual(
    await fixture.service.verifyAndActivate("HBPOSE2-not-before", {
      deviceCode: "IPAD-1",
      storeCode: "S1",
    }),
    {
      errorCode: "EMERGENCY_TOKEN_INVALID",
      ok: false,
    },
  );
  assert.deepEqual(fixture.crypto.verifyNowEpochMs, [
    fixture.nowMs,
  ]);
  assert.equal(fixture.session.activations.length, 0);
});

test("expires 位于可信时间下界与上界之间时按上界拒绝", async () => {
  const fixture = createLoginFixture();
  fixture.cache.value = keyPackage(1);
  fixture.crypto.verifyResults.push({
    errorCode: "",
    ok: true,
    claims: {
      expiresAtEpochMs: fixture.nowMs + 1_000,
      grantId: "10000000-0000-4000-8000-000000000001",
      notBeforeEpochMs: fixture.nowMs - 1_000,
      storeCode: "S1",
    },
  });

  assert.deepEqual(
    await fixture.service.verifyAndActivate("HBPOSE2-near-expiry", {
      deviceCode: "IPAD-1",
      storeCode: "S1",
    }),
    {
      errorCode: "EMERGENCY_TOKEN_INVALID",
      ok: false,
    },
  );
  assert.equal(fixture.session.activations.length, 0);
});

test("ES256/P-256 验证成功后先推进可信时间，再激活紧急会话；返回值不含 token", async () => {
  const fixture = createLoginFixture();
  fixture.cache.value = keyPackage(1);
  fixture.crypto.verifyResults.push({
    errorCode: "",
    ok: true,
    claims: validClaims(fixture.nowMs),
  });

  const result = await fixture.service.verifyAndActivate(
    "HBPOSE2-valid",
    {
      deviceCode: "IPAD-1",
      storeCode: "S1",
    },
  );

  assert.equal(result.ok, true);
  assert.equal("authorizationToken" in result, false);
  assert.deepEqual(fixture.trusted.replacements, [
    {
      serverEpochMs: fixture.nowMs,
      systemUptimeMs: fixture.systemUptime.value,
    },
  ]);
  assert.deepEqual(fixture.session.activations, [
    {
      authorizationToken: "HBPOSE2-valid",
      deviceCode: "IPAD-1",
      emergencyGrantId: "10000000-0000-4000-8000-000000000001",
      expiresAtEpochMs: fixture.nowMs + 60_000,
      storeCode: "S1",
      systemUptimeMs: fixture.systemUptime.value,
      trustedNowEpochMs: fixture.nowMs + 5_000,
    },
  ]);
  assert.deepEqual(result, {
    emergencyGrantId: "10000000-0000-4000-8000-000000000001",
    expiresAtEpochMs: fixture.nowMs + 60_000,
    ok: true,
    systemUptimeMs: fixture.systemUptime.value,
    trustedNowEpochMs: fixture.nowMs + 5_000,
  });
});

test("未知 kid 仅同步一次再用新包复验，持续未知时安全失败", async () => {
  const fixture = createLoginFixture();
  fixture.cache.value = keyPackage(1);
  fixture.crypto.verifyResults.push(
    { errorCode: "EMERGENCY_TOKEN_KEY_UNKNOWN", ok: false },
    { errorCode: "EMERGENCY_TOKEN_KEY_UNKNOWN", ok: false },
  );
  fixture.remote.fetches.push({
    kind: "changed",
    package: keyPackage(2),
  });
  fixture.remote.acks.push({
    acknowledged: true,
    serverVersion: 2,
    serverTimeEpochMs: fixture.nowMs,
  });

  const result = await fixture.service.verifyAndActivate(
    "HBPOSE2-unknown",
    {
      deviceCode: "IPAD-1",
      storeCode: "S1",
    },
  );

  assert.deepEqual(result, {
    errorCode: "EMERGENCY_TOKEN_KEY_UNKNOWN",
    ok: false,
  });
  assert.equal(fixture.syncCalls(), 1);
  assert.equal(fixture.crypto.verifies.length, 2);
  assert.equal(fixture.session.activations.length, 0);
});

test("过长或不支持前缀的 token 失败关闭且不进入 crypto", async () => {
  const fixture = createLoginFixture();

  assert.deepEqual(
    await fixture.service.verifyAndActivate("X".repeat(2_049), {
      deviceCode: "IPAD-1",
      storeCode: "S1",
    }),
    { errorCode: "EMERGENCY_TOKEN_TOO_LONG", ok: false },
  );
  assert.deepEqual(
    await fixture.service.verifyAndActivate("OTHER-token", {
      deviceCode: "IPAD-1",
      storeCode: "S1",
    }),
    { errorCode: "EMERGENCY_TOKEN_FORMAT_INVALID", ok: false },
  );
  assert.equal(fixture.crypto.verifies.length, 0);
});

function createSyncFixture() {
  const cache = new FakeKeyCache();
  const crypto = new FakeCrypto();
  const remote = new FakeRemote();
  const systemUptime = new FakeSystemUptime();
  const trusted = new FakeTrustedTime();
  const sync = new EmergencyPublicKeySyncService({
    cache,
    crypto,
    remote,
    systemUptime,
    trustedTime: trusted,
  });
  return { cache, crypto, remote, sync, systemUptime, trusted };
}

function createLoginFixture() {
  const nowMs = Date.parse("2026-07-28T01:00:00.000Z");
  const cache = new FakeKeyCache();
  const crypto = new FakeCrypto();
  const remote = new FakeRemote();
  const systemUptime = new FakeSystemUptime();
  const trusted = new FakeTrustedTime();
  let syncCalls = 0;
  const realSync = new EmergencyPublicKeySyncService({
    cache,
    crypto,
    remote,
    systemUptime,
    trustedTime: trusted,
  });
  const sync = {
    async sync() {
      syncCalls += 1;
      return realSync.sync();
    },
  };
  trusted.value = {
    serverEpochMs: nowMs,
    systemUptimeMs: systemUptime.value,
  };
  const session = new FakeSession();
  const service = new EmergencyLoginSecurityService({
    cache,
    crypto,
    session,
    sync,
    systemUptime,
    trustedTime: trusted,
  });
  return {
    cache,
    crypto,
    nowMs,
    remote,
    service,
    session,
    syncCalls: () => syncCalls,
    systemUptime,
    trusted,
  };
}

function keyPackage(version: number): EmergencyPublicKeyPackage {
  return {
    activeKeyId: "KEY01",
    generatedAtEpochMs: Date.parse("2026-07-28T00:00:00.000Z"),
    keys: [
      {
        algorithm: "ES256",
        fingerprintHex: "A".repeat(64),
        kid: "KEY01",
        publicKeyPem: `-----BEGIN PUBLIC KEY-----\n${"A".repeat(96)}\n-----END PUBLIC KEY-----`,
      },
    ],
    version,
  };
}

function corruptPackage(): EmergencyPublicKeyPackage {
  return {
    ...keyPackage(1),
    keys: [
      {
        ...keyPackage(1).keys[0]!,
        algorithm: "ES256",
        kid: "BAD-ID!",
      },
    ],
  };
}

function validClaims(nowMs: number) {
  return {
    expiresAtEpochMs: nowMs + 60_000,
    grantId: "10000000-0000-4000-8000-000000000001",
    notBeforeEpochMs: nowMs - 1_000,
    storeCode: "S1",
  };
}
