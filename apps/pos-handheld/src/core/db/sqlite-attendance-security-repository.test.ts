import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";

import { applyMigrations, POS_DATABASE_MIGRATIONS } from "./migrations";
import { PosDatabase } from "./pos-database";
import {
  SqliteAttendanceSecurityFacade,
  type AttendanceQrProvisioning,
  type AttendanceSecurityTerminalScope,
  type EmergencyPublicKeyPackage,
  type EmergencyTrustedTimeAnchor,
} from "./sqlite-attendance-security-repository";
import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type {
  SqliteConnectionPort,
  SqliteDriverPort,
  SqlRunResult,
  SqlValue,
} from "@hb/pos-db/core/db/types";

import {
  EmergencyPublicKeySyncService,
  type EmergencyLoginCryptoPort,
} from "@/features/attendance-audit/emergency-login-security";

const NOW = "2026-07-29T00:00:00.000Z";
const PUBLIC_KEY_PEM = [
  "-----BEGIN PUBLIC KEY-----",
  "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEAAAAAAAAAAAAAAAAAAAAAAAA",
  "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
  "-----END PUBLIC KEY-----",
].join("\n");

const TERMINAL: AttendanceSecurityTerminalScope = Object.freeze({
  apiPartition: "https://hbpos.example.test",
  storeCode: "STORE-1",
  deviceCode: "IPAD-1",
  hardwareId: "HW-1",
  authorizationMarker: "AUTH-1",
});

test("M19 独立新增考勤安全表，升级保留 M1 legacy 数据且失败不推进版本", async () => {
  await withDatabase(async (connection) => {
    const throughM18 = POS_DATABASE_MIGRATIONS.filter(
      (migration) => migration.version <= 18,
    );
    await applyMigrations(connection, () => NOW, throughM18);
    await connection.run(
      `INSERT INTO emergency_login_key_bundles (
        kid, store_code, public_key_pem, not_before_iso, expires_at_iso,
        fetched_at_iso
      ) VALUES ('legacy-kid', 'STORE-1', 'legacy-pem', ?, ?, ?)`,
      [NOW, "2027-07-29T00:00:00.000Z", NOW],
    );
    await connection.run(
      `INSERT INTO trusted_time_anchor (
        anchor_id, trusted_at_iso, monotonic_elapsed_ms, updated_at_iso
      ) VALUES (1, ?, 10, ?)`,
      [NOW, NOW],
    );

    const m19 = POS_DATABASE_MIGRATIONS.find(
      (migration) => migration.version === 19,
    );
    assert.ok(m19);
    const failingMigrations = [
      ...throughM18,
      {
        ...m19,
        sql: `${m19.sql}\nCREATE TABL intentionally_invalid_m19;`,
      },
    ];
    await assert.rejects(
      () => applyMigrations(connection, () => NOW, failingMigrations),
      /syntax|near/i,
    );
    assert.equal(await schemaVersion(connection), 18);
    assert.equal(
      await tableExists(connection, "attendance_qr_provisioning_cache"),
      false,
    );

    await applyMigrations(
      connection,
      () => NOW,
      POS_DATABASE_MIGRATIONS.filter(
        (migration) => migration.version <= 19,
      ),
    );

    assert.equal(await schemaVersion(connection), 19);
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM emergency_login_key_bundles",
      ),
      1,
    );
    assert.equal(
      await scalar(
        connection,
        "SELECT COUNT(*) AS count FROM trusted_time_anchor",
      ),
      1,
    );
    assert.equal(
      await tableExists(connection, "attendance_qr_provisioning_cache"),
      true,
    );
    assert.equal(
      await tableExists(connection, "emergency_public_key_package_cache"),
      true,
    );
    assert.equal(
      await tableExists(connection, "emergency_trusted_time_cache"),
      true,
    );
  });
});

test("真实 SQLite：QR provisioning 整包加密、scope hash 隔离并可原子 load/replace/clear", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new RecordingEncryptor();
    const facade = attendanceFacade(connection, encryptor);
    const original = provisioning();

    await facade.attendanceQrCache.replace(original);
    assert.deepEqual(await facade.attendanceQrCache.load(), original);

    const row = await connection.getFirst<Record<string, unknown>>(
      "SELECT * FROM attendance_qr_provisioning_cache",
    );
    assert.ok(row);
    assert.deepEqual(Object.keys(row), [
      "scope_hash",
      "api_partition",
      "store_code",
      "device_code",
      "payload_revision",
      "provisioning_ciphertext",
      "updated_at_iso",
    ]);
    assert.equal(row.scope_hash, expectedScopeHash(TERMINAL));
    assert.equal(row.api_partition, TERMINAL.apiPartition);
    assert.equal(row.store_code, TERMINAL.storeCode);
    assert.equal(row.device_code, TERMINAL.deviceCode);
    assert.ok(row.provisioning_ciphertext instanceof Uint8Array);
    assert.equal(
      JSON.stringify(row).includes(TERMINAL.hardwareId),
      false,
    );
    assert.equal(
      JSON.stringify(row).includes(TERMINAL.authorizationMarker),
      false,
    );
    assert.match(
      encryptor.encryptedPlaintexts[0] ?? "",
      /authorizationMarker.*AUTH-1/,
    );
    assert.match(
      encryptor.encryptedPlaintexts[0] ?? "",
      /hardwareId.*HW-1/,
    );
    assert.match(
      encryptor.encryptedPlaintexts[0] ?? "",
      /keyHandle.*key-handle-1/,
    );

    const otherScope = attendanceFacade(connection, encryptor, {
      ...TERMINAL,
      hardwareId: "HW-2",
      authorizationMarker: "AUTH-2",
    });
    assert.equal(await otherScope.attendanceQrCache.load(), null);
    await otherScope.attendanceQrCache.clear();
    assert.deepEqual(await facade.attendanceQrCache.load(), original);

    await connection.exec(`
      CREATE TRIGGER fail_qr_cache_update
      BEFORE UPDATE ON attendance_qr_provisioning_cache
      BEGIN
        SELECT RAISE(ABORT, 'QR_CACHE_UPDATE_FAILED');
      END;
    `);
    await assert.rejects(
      () =>
        facade.attendanceQrCache.replace(
          provisioning({ registeredAtEpochMs: 200 }),
        ),
      /QR_CACHE_UPDATE_FAILED/,
    );
    assert.deepEqual(await facade.attendanceQrCache.load(), original);
    await connection.exec("DROP TRIGGER fail_qr_cache_update;");

    await connection.exec(`
      CREATE TRIGGER fail_qr_cache_clear
      BEFORE DELETE ON attendance_qr_provisioning_cache
      BEGIN
        SELECT RAISE(ABORT, 'QR_CACHE_CLEAR_FAILED');
      END;
    `);
    await assert.rejects(
      () => facade.attendanceQrCache.clear(),
      /QR_CACHE_CLEAR_FAILED/,
    );
    assert.deepEqual(await facade.attendanceQrCache.load(), original);
    await connection.exec("DROP TRIGGER fail_qr_cache_clear;");

    await connection.run(
      `UPDATE attendance_qr_provisioning_cache
       SET provisioning_ciphertext = ?`,
      [new Uint8Array([255])],
    );
    await assert.rejects(
      () => facade.attendanceQrCache.load(),
      /ciphertext|attendance|cache/i,
    );
  });
});

test("真实 SQLite：QR replace 拒绝 scope 错绑且加密失败不污染旧缓存", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new RecordingEncryptor();
    const facade = attendanceFacade(connection, encryptor);
    const original = provisioning();
    await facade.attendanceQrCache.replace(original);

    await assert.rejects(
      () =>
        facade.attendanceQrCache.replace({
          ...original,
          identity: {
            ...original.identity,
            authorizationMarker: "OTHER-AUTH",
          },
        }),
      /scope|attendance/i,
    );

    encryptor.failEncryption = true;
    await assert.rejects(
      () =>
        facade.attendanceQrCache.replace(
          provisioning({ registeredAtEpochMs: 300 }),
        ),
      /TEST_ENCRYPTION_FAILURE/,
    );
    encryptor.failEncryption = false;
    assert.deepEqual(await facade.attendanceQrCache.load(), original);
  });
});

test("真实 SQLite：紧急登录公钥包严格整包保存、scope hash 隔离且禁止降级或同版本换包", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new RecordingEncryptor();
    const facade = attendanceFacade(connection, encryptor);
    const versionOne = publicKeyPackage();

    await facade.emergencyPublicKeyCache.replace(versionOne);
    assert.deepEqual(await facade.emergencyPublicKeyCache.read(), {
      ...versionOne,
      keys: [
        {
          ...versionOne.keys[0],
          fingerprintHex: "A".repeat(64),
        },
      ],
    });

    const row = await connection.getFirst<Record<string, unknown>>(
      "SELECT * FROM emergency_public_key_package_cache",
    );
    assert.ok(row);
    assert.equal(row.scope_hash, expectedScopeHash(TERMINAL));
    assert.equal(row.package_version, 1);
    assert.equal(row.generated_at_epoch_ms, 100);
    assert.equal(row.active_key_id, "KEY1");
    assert.match(String(row.keys_json), /BEGIN PUBLIC KEY/);
    assert.match(String(row.keys_json), new RegExp("A".repeat(64)));
    assert.equal(String(row.keys_json).includes(TERMINAL.hardwareId), false);
    assert.equal(
      String(row.keys_json).includes(TERMINAL.authorizationMarker),
      false,
    );

    const otherScope = attendanceFacade(connection, encryptor, {
      ...TERMINAL,
      authorizationMarker: "AUTH-OTHER",
    });
    assert.equal(await otherScope.emergencyPublicKeyCache.read(), null);

    const versionTwo = publicKeyPackage({
      version: 2,
      generatedAtEpochMs: 200,
    });
    await facade.emergencyPublicKeyCache.replace(versionTwo);
    await assert.rejects(
      () => facade.emergencyPublicKeyCache.replace(versionOne),
      /version|rollback|downgrade/i,
    );
    await assert.rejects(
      () =>
        facade.emergencyPublicKeyCache.replace({
          ...versionTwo,
          generatedAtEpochMs: 201,
        }),
      /version|conflict/i,
    );
    assert.deepEqual(
      await facade.emergencyPublicKeyCache.read(),
      normalizedPublicKeyPackage(versionTwo),
    );

    await connection.exec(`
      CREATE TRIGGER fail_public_key_package_update
      BEFORE UPDATE ON emergency_public_key_package_cache
      BEGIN
        SELECT RAISE(ABORT, 'PUBLIC_KEY_PACKAGE_UPDATE_FAILED');
      END;
    `);
    await assert.rejects(
      () =>
        facade.emergencyPublicKeyCache.replace(
          publicKeyPackage({ version: 3, generatedAtEpochMs: 300 }),
        ),
      /PUBLIC_KEY_PACKAGE_UPDATE_FAILED/,
    );
    assert.deepEqual(
      await facade.emergencyPublicKeyCache.read(),
      normalizedPublicKeyPackage(versionTwo),
    );
    await connection.exec("DROP TRIGGER fail_public_key_package_update;");

    await connection.run(
      `UPDATE emergency_public_key_package_cache
       SET keys_json = '{"partial":true}'`,
    );
    await assert.rejects(
      () => facade.emergencyPublicKeyCache.read(),
      /public key|package|json/i,
    );
  });
});

test("真实 SQLite：非法公钥输入或 SQL 写失败完整保留原包", async () => {
  await withMigratedDatabase(async (connection) => {
    const facade = attendanceFacade(
      connection,
      new RecordingEncryptor(),
    );
    const original = publicKeyPackage();
    await facade.emergencyPublicKeyCache.replace(original);

    const invalidPackages: EmergencyPublicKeyPackage[] = [
      { ...original, keys: [] },
      {
        ...original,
        activeKeyId: "MISSING",
      },
      {
        ...original,
        keys: [
          {
            ...original.keys[0]!,
            algorithm: "RS256" as "ES256",
          },
        ],
      },
      {
        ...original,
        keys: [
          {
            ...original.keys[0]!,
            publicKeyPem: "-----BEGIN PRIVATE KEY-----secret",
          },
        ],
      },
      {
        ...original,
        keys: [
          original.keys[0]!,
          original.keys[0]!,
        ],
      },
    ];
    for (const invalid of invalidPackages) {
      await assert.rejects(
        () => facade.emergencyPublicKeyCache.replace(invalid),
        /public key|package/i,
      );
    }
    assert.deepEqual(
      await facade.emergencyPublicKeyCache.read(),
      normalizedPublicKeyPackage(original),
    );
  });
});

test("真实 SQLite：v2 可信时间锚点严格加密、terminal-scoped 且事务失败保留旧值", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new RecordingEncryptor();
    const facade = attendanceFacade(connection, encryptor);
    assert.equal(
      await facade.emergencyTrustedTime.readAnchor(),
      null,
    );

    const original: EmergencyTrustedTimeAnchor = Object.freeze({
      serverEpochMs: 1_000,
      systemUptimeMs: 100,
    });
    await facade.emergencyTrustedTime.replaceAnchor(original);
    assert.deepEqual(
      await facade.emergencyTrustedTime.readAnchor(),
      original,
    );

    const row = await connection.getFirst<Record<string, unknown>>(
      "SELECT * FROM emergency_trusted_time_cache",
    );
    assert.ok(row);
    assert.deepEqual(Object.keys(row), [
      "scope_hash",
      "api_partition",
      "store_code",
      "device_code",
      "payload_revision",
      "trusted_time_ciphertext",
      "updated_at_iso",
    ]);
    assert.equal(row.payload_revision, 1);
    assert.ok(row.trusted_time_ciphertext instanceof Uint8Array);
    assert.equal(Object.keys(row).some((key) => /epoch|high_water/u.test(key)), false);
    const persistedEnvelope = JSON.parse(
      encryptor.encryptedPlaintexts.at(-1) ?? "null",
    ) as unknown;
    assert.deepEqual(persistedEnvelope, {
      format: "hb-pos-emergency-trusted-time-v2",
      scope: TERMINAL,
      serverEpochMs: 1_000,
      systemUptimeMs: 100,
    });
    assert.equal(
      await attendanceFacade(connection, encryptor, {
        ...TERMINAL,
        hardwareId: "HW-OTHER",
      }).emergencyTrustedTime.readAnchor(),
      null,
    );

    await connection.exec(`
      CREATE TRIGGER fail_trusted_time_update
      BEFORE UPDATE ON emergency_trusted_time_cache
      BEGIN
        SELECT RAISE(ABORT, 'TRUSTED_TIME_UPDATE_FAILED');
      END;
    `);
    await assert.rejects(
      () =>
        facade.emergencyTrustedTime.replaceAnchor({
          serverEpochMs: 1_100,
          systemUptimeMs: 200,
        }),
      /TRUSTED_TIME_UPDATE_FAILED/,
    );
    assert.deepEqual(
      await facade.emergencyTrustedTime.readAnchor(),
      original,
    );
    await connection.exec("DROP TRIGGER fail_trusted_time_update;");

    await connection.run(
      `UPDATE emergency_trusted_time_cache
       SET trusted_time_ciphertext = ?`,
      [new Uint8Array([255])],
    );
    await assert.rejects(
      () => facade.emergencyTrustedTime.readAnchor(),
      /ciphertext|trusted time|cache/i,
    );
  });
});

test("真实 SQLite：v2 锚点禁止服务端时间回退，严格校验输入并允许设备重启后在线重锚", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new RecordingEncryptor();
    const trustedTime = attendanceFacade(
      connection,
      encryptor,
    ).emergencyTrustedTime;
    await trustedTime.replaceAnchor({
      serverEpochMs: 1_000,
      systemUptimeMs: 100,
    });

    await assert.rejects(
      () =>
        trustedTime.replaceAnchor({
          serverEpochMs: 999,
          systemUptimeMs: 101,
        }),
      /rollback|server|trusted time/i,
    );
    await assert.rejects(
      () =>
        trustedTime.replaceAnchor({
          serverEpochMs: 1_005,
          systemUptimeMs: 110,
        }),
      /rollback|server|trusted time/i,
    );
    for (const invalid of [
      { serverEpochMs: -1, systemUptimeMs: 100 },
      { serverEpochMs: 1_001.5, systemUptimeMs: 101 },
      { serverEpochMs: 1_001, systemUptimeMs: -1 },
      { serverEpochMs: 1_001, systemUptimeMs: 101.5 },
      {
        serverEpochMs: 1_001,
        systemUptimeMs: 101,
        unexpected: true,
      },
    ]) {
      await assert.rejects(
        () =>
          trustedTime.replaceAnchor(
            invalid as unknown as EmergencyTrustedTimeAnchor,
          ),
        /anchor|trusted time|invalid/i,
      );
    }

    await trustedTime.replaceAnchor({
      serverEpochMs: 1_010,
      systemUptimeMs: 110,
    });
    await assert.rejects(
      () =>
        trustedTime.replaceAnchor({
          serverEpochMs: 1_010,
          systemUptimeMs: 109,
        }),
      /rollback|uptime|trusted time/i,
    );
    // uptime 下降代表 iOS boot 已变化；只有不回退的在线 server time 才能重建锚点。
    await trustedTime.replaceAnchor({
      serverEpochMs: 1_020,
      systemUptimeMs: 5,
    });
    assert.deepEqual(await trustedTime.readAnchor(), {
      serverEpochMs: 1_020,
      systemUptimeMs: 5,
    });
  });
});

test("真实 SQLite：ACK 响应锚点保留旧锚点推导到响应 uptime 的可信下界", async () => {
  await withMigratedDatabase(async (connection) => {
    const facade = attendanceFacade(
      connection,
      new RecordingEncryptor(),
    );
    await facade.emergencyPublicKeyCache.replace(
      publicKeyPackage(),
    );
    await facade.emergencyTrustedTime.replaceAnchor({
      serverEpochMs: 1_000,
      systemUptimeMs: 100,
    });
    let systemUptimeMs = 108;
    const crypto: EmergencyLoginCryptoPort = {
      validateEs256P256PublicKey: async () => true,
      verifyEs256P256Token: async () => {
        throw new Error("not used");
      },
    };
    const sync = new EmergencyPublicKeySyncService({
      cache: facade.emergencyPublicKeyCache,
      crypto,
      remote: {
        registerAttendanceKey: async () => {
          throw new Error("not used");
        },
        fetchEmergencyPublicKeys: async () => ({
          kind: "not-modified",
        }),
        acknowledgeEmergencyPublicKeys: async () => {
          systemUptimeMs = 110;
          return {
            acknowledged: true,
            serverTimeEpochMs: 1_008,
            serverVersion: 1,
          };
        },
      },
      systemUptime: {
        getSystemUptimeMilliseconds: () => systemUptimeMs,
      },
      trustedTime: facade.emergencyTrustedTime,
    });

    assert.equal(await sync.sync(), true);
    assert.deepEqual(
      await facade.emergencyTrustedTime.readAnchor(),
      {
        serverEpochMs: 1_010,
        systemUptimeMs: 110,
      },
    );
  });
});

test("真实 SQLite：旧 v1 高水位不返回业务锚点，只允许不低于旧值的 ACK 原位升级 v2", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new RecordingEncryptor();
    const legacyCiphertext = await encryptor.encrypt(
      JSON.stringify({
        format: "hb-pos-emergency-trusted-time-v1",
        scope: TERMINAL,
        highWaterEpochMs: 1_000,
      }),
    );
    await connection.run(
      `INSERT INTO emergency_trusted_time_cache (
        scope_hash, api_partition, store_code, device_code,
        payload_revision, trusted_time_ciphertext, updated_at_iso
      ) VALUES (?, ?, ?, ?, ?, ?, ?)`,
      [
        expectedScopeHash(TERMINAL),
        TERMINAL.apiPartition,
        TERMINAL.storeCode,
        TERMINAL.deviceCode,
        1,
        legacyCiphertext,
        NOW,
      ],
    );
    const trustedTime = attendanceFacade(
      connection,
      encryptor,
    ).emergencyTrustedTime;

    assert.equal(await trustedTime.readAnchor(), null);
    await assert.rejects(
      () =>
        trustedTime.replaceAnchor({
          serverEpochMs: 999,
          systemUptimeMs: 10,
        }),
      /rollback|server|trusted time/i,
    );
    assert.equal(await trustedTime.readAnchor(), null);

    await trustedTime.replaceAnchor({
      serverEpochMs: 1_000,
      systemUptimeMs: 10,
    });
    assert.deepEqual(await trustedTime.readAnchor(), {
      serverEpochMs: 1_000,
      systemUptimeMs: 10,
    });
    const row = await connection.getFirst<{
      payload_revision: unknown;
    }>(
      `SELECT payload_revision
       FROM emergency_trusted_time_cache
       WHERE scope_hash = ?`,
      [expectedScopeHash(TERMINAL)],
    );
    assert.equal(row?.payload_revision, 1);
  });
});

test("真实 SQLite：v2 密文必须 exact keys 且内层 terminal scope 完全匹配", async () => {
  await withMigratedDatabase(async (connection) => {
    const encryptor = new RecordingEncryptor();
    const facade = attendanceFacade(connection, encryptor);
    await facade.emergencyTrustedTime.replaceAnchor({
      serverEpochMs: 1_000,
      systemUptimeMs: 100,
    });
    const invalidEnvelopes = [
      {
        format: "hb-pos-emergency-trusted-time-v2",
        scope: TERMINAL,
        serverEpochMs: 1_000,
        systemUptimeMs: 100,
        unexpected: true,
      },
      {
        format: "hb-pos-emergency-trusted-time-v2",
        scope: { ...TERMINAL, hardwareId: "OTHER-HARDWARE" },
        serverEpochMs: 1_000,
        systemUptimeMs: 100,
      },
    ];

    for (const invalid of invalidEnvelopes) {
      const ciphertext = await encryptor.encrypt(
        JSON.stringify(invalid),
      );
      await connection.run(
        `UPDATE emergency_trusted_time_cache
         SET payload_revision = 1, trusted_time_ciphertext = ?
         WHERE scope_hash = ?`,
        [ciphertext, expectedScopeHash(TERMINAL)],
      );
      await assert.rejects(
        () => facade.emergencyTrustedTime.readAnchor(),
        /ciphertext|scope|trusted time/i,
      );
    }
  });
});

test("PosDatabase.attendanceSecurity 只暴露三个 terminal-scoped 窄 Port", async () => {
  const database = await PosDatabase.open({
    databaseName: ":memory:",
    driver: new SystemSqliteDriver(),
    keyProvider: {
      getOrCreateDatabaseKey: async () => "a".repeat(64),
    },
    nowIso: () => NOW,
  });
  try {
    const encryptor = new RecordingEncryptor();
    const facade = database.attendanceSecurity(encryptor, TERMINAL);
    assert.ok(facade instanceof SqliteAttendanceSecurityFacade);
    assert.deepEqual(Object.keys(facade).sort(), [
      "attendanceQrCache",
      "emergencyPublicKeyCache",
      "emergencyTrustedTime",
    ]);

    const value = provisioning();
    await facade.attendanceQrCache.replace(value);
    assert.deepEqual(await facade.attendanceQrCache.load(), value);
    await facade.emergencyPublicKeyCache.replace(publicKeyPackage());
    assert.equal(
      (await facade.emergencyPublicKeyCache.read())?.version,
      1,
    );
    await facade.emergencyTrustedTime.replaceAnchor({
      serverEpochMs: 100,
      systemUptimeMs: 10,
    });
    assert.deepEqual(
      await facade.emergencyTrustedTime.readAnchor(),
      {
        serverEpochMs: 100,
        systemUptimeMs: 10,
      },
    );
  } finally {
    await database.close();
  }
});

function attendanceFacade(
  connection: SqliteConnectionPort,
  encryptor: SensitivePayloadEncryptor,
  terminal: AttendanceSecurityTerminalScope = TERMINAL,
): SqliteAttendanceSecurityFacade {
  return new SqliteAttendanceSecurityFacade(
    connection,
    encryptor,
    terminal,
    () => NOW,
  );
}

function provisioning(
  overrides: Partial<
    AttendanceQrProvisioning["identity"] &
      AttendanceQrProvisioning["trustedTime"]
  > = {},
): AttendanceQrProvisioning {
  return {
    identity: {
      authorizationMarker:
        overrides.authorizationMarker ?? TERMINAL.authorizationMarker,
      deviceCode: overrides.deviceCode ?? TERMINAL.deviceCode,
      hardwareId: overrides.hardwareId ?? TERMINAL.hardwareId,
      keyHandle: overrides.keyHandle ?? "key-handle-1",
      kid: overrides.kid ?? "attendance_kid_1",
      registeredAtEpochMs: overrides.registeredAtEpochMs ?? 100,
      storeCode: overrides.storeCode ?? TERMINAL.storeCode,
    },
    trustedTime: {
      localEpochMs: overrides.localEpochMs ?? 1_000,
      serverEpochMs: overrides.serverEpochMs ?? 2_000,
    },
  };
}

function publicKeyPackage(
  overrides: Partial<EmergencyPublicKeyPackage> = {},
): EmergencyPublicKeyPackage {
  return {
    version: 1,
    activeKeyId: "KEY1",
    generatedAtEpochMs: 100,
    keys: [
      {
        kid: "KEY1",
        algorithm: "ES256",
        publicKeyPem: PUBLIC_KEY_PEM,
        fingerprintHex: "a".repeat(64),
      },
    ],
    ...overrides,
  };
}

function normalizedPublicKeyPackage(
  value: EmergencyPublicKeyPackage,
): EmergencyPublicKeyPackage {
  return {
    ...value,
    keys: value.keys.map((key) => ({
      ...key,
      fingerprintHex: key.fingerprintHex.toUpperCase(),
    })),
  };
}

function expectedScopeHash(
  value: AttendanceSecurityTerminalScope,
): string {
  return createHash("sha256")
    .update(
      JSON.stringify({
        format: "hb-pos-attendance-security-scope-v1",
        apiPartition: value.apiPartition,
        storeCode: value.storeCode,
        deviceCode: value.deviceCode,
        hardwareId: value.hardwareId,
        authorizationMarker: value.authorizationMarker,
      }),
      "utf8",
    )
    .digest("hex");
}

class RecordingEncryptor implements SensitivePayloadEncryptor {
  public readonly encryptedPlaintexts: string[] = [];
  public failEncryption = false;
  private sequence = 0;
  private readonly plaintextByCiphertext = new Map<number, string>();

  public async encrypt(plaintext: string): Promise<Uint8Array> {
    if (this.failEncryption) throw new Error("TEST_ENCRYPTION_FAILURE");
    this.encryptedPlaintexts.push(plaintext);
    this.sequence += 1;
    this.plaintextByCiphertext.set(this.sequence, plaintext);
    return new Uint8Array([this.sequence]);
  }

  public async decrypt(ciphertext: Uint8Array): Promise<string> {
    const key = ciphertext.length === 1 ? ciphertext[0] : undefined;
    const plaintext =
      key === undefined ? undefined : this.plaintextByCiphertext.get(key);
    if (plaintext === undefined) {
      throw new Error("Sensitive payload ciphertext is invalid.");
    }
    return plaintext;
  }
}

async function schemaVersion(
  connection: SqliteConnectionPort,
): Promise<number> {
  return Number(
    (
      await connection.getFirst<{ version: unknown }>(
        "SELECT MAX(version) AS version FROM schema_migrations",
      )
    )?.version,
  );
}

async function tableExists(
  connection: SqliteConnectionPort,
  tableName: string,
): Promise<boolean> {
  return (
    Number(
      (
        await connection.getFirst<{ count: unknown }>(
          `SELECT COUNT(*) AS count
           FROM sqlite_master
           WHERE type = 'table' AND name = ?`,
          [tableName],
        )
      )?.count,
    ) === 1
  );
}

async function scalar(
  connection: SqliteConnectionPort,
  sql: string,
): Promise<number> {
  return Number(
    (await connection.getFirst<{ count: unknown }>(sql))?.count,
  );
}

class SystemSqliteDriver implements SqliteDriverPort {
  public async open(_databaseName: string): Promise<SqliteConnectionPort> {
    return new SystemSqliteConnection(new DatabaseSync(":memory:"));
  }
}

class SystemSqliteConnection implements SqliteConnectionPort {
  public constructor(private readonly database: DatabaseSync) {
    this.database.exec("PRAGMA foreign_keys = ON;");
  }

  public async exec(sql: string): Promise<void> {
    this.database.exec(sql);
  }

  public async run(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<SqlRunResult> {
    const result = this.database
      .prepare(sql)
      .run(...parameters.map(toSqlInputValue));
    return {
      changes: Number(result.changes),
      lastInsertRowId: Number(result.lastInsertRowid),
    };
  }

  public async getFirst<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<T | null> {
    // Node 内置 SQLite 不含 SQLCipher；仅为测试的精确探针提供有效版本。
    if (sql === "PRAGMA cipher_version;") {
      return { cipher_version: "4.6.1" } as unknown as T;
    }
    return (
      this.database
        .prepare(sql)
        .get(...parameters.map(toSqlInputValue)) as T | undefined
    ) ?? null;
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    return this.database
      .prepare(sql)
      .all(...parameters.map(toSqlInputValue)) as unknown as readonly T[];
  }

  public async withExclusiveTransaction<T>(
    operation: (transaction: SqliteConnectionPort) => Promise<T>,
  ): Promise<T> {
    this.database.exec("BEGIN IMMEDIATE;");
    const transaction = new TransactionConnection(this.database);
    try {
      const result = await operation(transaction);
      this.database.exec("COMMIT;");
      return result;
    } catch (error) {
      this.database.exec("ROLLBACK;");
      throw error;
    }
  }

  public async close(): Promise<void> {
    this.database.close();
  }
}

class TransactionConnection extends SystemSqliteConnection {
  public override withExclusiveTransaction<T>(): Promise<T> {
    return Promise.reject(new Error("Nested test transaction."));
  }

  public override close(): Promise<void> {
    return Promise.reject(new Error("Transaction cannot close database."));
  }
}

async function withMigratedDatabase(
  operation: (connection: SystemSqliteConnection) => Promise<void>,
): Promise<void> {
  await withDatabase(async (connection) => {
    await applyMigrations(connection, () => NOW);
    await operation(connection);
  });
}

async function withDatabase(
  operation: (connection: SystemSqliteConnection) => Promise<void>,
): Promise<void> {
  const connection = new SystemSqliteConnection(new DatabaseSync(":memory:"));
  try {
    await operation(connection);
  } finally {
    await connection.close();
  }
}

function toSqlInputValue(value: SqlValue): SQLInputValue {
  return value as SQLInputValue;
}
