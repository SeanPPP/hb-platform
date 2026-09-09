import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { DatabaseSync, type SQLInputValue } from "node:sqlite";
import test from "node:test";
import { tmpdir } from "node:os";
import { join } from "node:path";

import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "@hb/pos-db/core/db/types";

import { POS_DATABASE_MIGRATIONS } from "./migrations";
import { SqliteFaceAttendanceRepository } from "./sqlite-face-attendance-repository";

class NodeSqliteConnection implements SqliteConnectionPort {
  private active = false;
  public constructor(private readonly db: DatabaseSync) {}
  public async exec(sql: string): Promise<void> { this.db.exec(sql); }
  public async run(sql: string, parameters: readonly SqlValue[] = []): Promise<SqlRunResult> { const result = this.db.prepare(sql).run(...parameters as readonly SQLInputValue[]); return { changes: Number(result.changes), lastInsertRowId: Number(result.lastInsertRowid) }; }
  public async getFirst<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<T | null> { const row = this.db.prepare(sql).get(...parameters as readonly SQLInputValue[]); return row === undefined ? null : row as T; }
  public async getAll<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<readonly T[]> { return this.db.prepare(sql).all(...parameters as readonly SQLInputValue[]) as unknown as readonly T[]; }
  public async withExclusiveTransaction<T>(operation: (tx: SqliteConnectionPort) => Promise<T>): Promise<T> { if (this.active) throw new Error("nested"); this.active = true; this.db.exec("BEGIN IMMEDIATE"); try { const result = await operation(this); this.db.exec("COMMIT"); return result; } catch (error) { this.db.exec("ROLLBACK"); throw error; } finally { this.active = false; } }
  public async close(): Promise<void> { this.db.close(); }
}

const NOW = "2026-09-09T18:00:00.000Z";
const JPEG = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAF//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABBQL/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/Aaf/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/Aaf/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAY/Aqf/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/If/aAAwDAQACAAMAAAAQ/wD/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/EH//xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/EH//xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/EP/aAAwDAQACAAMAAAAQ/wD/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/EH//xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/EH//xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/EP/Z";
function open(scope = { storeCode: "S1", deviceCode: "D1", hardwareId: "H1" }) { const raw = new DatabaseSync(":memory:"); raw.exec(POS_DATABASE_MIGRATIONS.find((m) => m.version === 44)!.sql); const db = new NodeSqliteConnection(raw); return new SqliteFaceAttendanceRepository(db, scope, () => NOW); }
function event(guid: string, minute: string) { return { eventGuid: guid, userGuid: "u", employeeName: "A", punchType: "clockIn" as const, occurredAtUtc: `2026-09-09T${minute}:00:00.000Z`, deviceObservedAtUtc: `2026-09-09T${minute}:00:00.000Z`, rosterVersion: "4", enrollmentVersion: "2", timeAnchorId: "t", timeTrusted: false, photoSha256: "a".repeat(64), keyId: "key", photoBase64: JPEG }; }

test("face queue persists 09:00/17:00 in sequence, survives recreation, skips review and reclaims an expired lease", async () => {
  const repository = open();
  await repository.persist(event("11111111-1111-4111-8111-111111111111", "09"), async () => "aGVsbG8=");
  await repository.persist({ ...event("22222222-2222-4222-8222-222222222222", "17"), punchType: "clockOut" }, async () => "aGVsbG8=");
  const first = await repository.claimNextUpload("lease-1", "2026-09-09T18:01:00.000Z");
  assert.equal(first?.occurredAtUtc, "2026-09-09T09:00:00.000Z");
  await repository.applyServerResult(first!.eventGuid, "lease-1", { status: "queued", reasonCode: null, punchGuid: null, receivedAtUtc: NOW, updatedAtUtc: NOW });
  assert.equal(await repository.claimNextReview("poll-1", "2026-09-09T18:01:00.000Z", false), null);
  const second = await repository.claimNextUpload("lease-2", "2026-09-09T17:59:00.000Z");
  assert.equal(second?.occurredAtUtc, "2026-09-09T17:00:00.000Z");
  const restarted = await repository.snapshot();
  assert.equal(restarted.entries.length, 2);
  const reclaimed = await repository.claimNextUpload("lease-3", "2026-09-09T18:03:00.000Z");
  assert.equal(reclaimed?.eventGuid, second?.eventGuid);
});

test("face queue persists durable event facts across a real SQLite reopen", async () => {
  const directory = await mkdtemp(join(tmpdir(), "hb-face-attendance-restart-"));
  const filename = join(directory, "pos.db");
  try {
    const firstRaw = new DatabaseSync(filename); firstRaw.exec(POS_DATABASE_MIGRATIONS.find((migration) => migration.version === 44)!.sql);
    const first = new SqliteFaceAttendanceRepository(new NodeSqliteConnection(firstRaw), { storeCode: "S1", deviceCode: "D1", hardwareId: "H1" }, () => NOW);
    const saved = await first.persist(event("99999999-9999-4999-8999-999999999999", "09"), async () => "aGVsbG8=");
    await firstRaw.close();

    const secondRaw = new DatabaseSync(filename);
    const restarted = new SqliteFaceAttendanceRepository(new NodeSqliteConnection(secondRaw), { storeCode: "S1", deviceCode: "D1", hardwareId: "H1" }, () => NOW);
    const claimed = await restarted.claimNextUpload("after-restart", "2026-09-09T18:01:00.000Z");
    assert.deepEqual(claimed && {
      eventGuid: claimed.eventGuid, signature: claimed.signature, photoBase64: claimed.photoBase64,
      punchType: claimed.punchType, occurredAtUtc: claimed.occurredAtUtc, localSequence: claimed.localSequence,
    }, {
      eventGuid: saved.eventGuid, signature: saved.signature, photoBase64: JPEG,
      punchType: "clockIn", occurredAtUtc: "2026-09-09T09:00:00.000Z", localSequence: 1,
    });
    await secondRaw.close();
  } finally { await rm(directory, { recursive: true, force: true }); }
});

test("face queue isolates device scope and rejects invalid photo before a success is recorded", async () => {
  const left = open(); const right = open({ storeCode: "S1", deviceCode: "D2", hardwareId: "H2" });
  await left.persist(event("33333333-3333-4333-8333-333333333333", "09"), async () => "aGVsbG8=");
  assert.equal(await right.claimNextUpload("other", "2026-09-09T18:01:00.000Z"), null);
  await assert.rejects(() => left.persist({ ...event("44444444-4444-4444-8444-444444444444", "18"), photoBase64: "not-base64" }, async () => "aGVsbG8="), /FACE_PHOTO_INVALID/);
  await assert.rejects(() => left.persist({ ...event("44444444-4444-4444-8444-444444444444", "18"), photoBase64: "/9j/4AAQ" + "A".repeat(2_796_196) }, async () => "aGVsbG8="), /FACE_PHOTO_INVALID/);
  assert.equal((await left.snapshot()).entries.length, 1);
});

test("face queue accepts APP1 JPEGs but still rejects invalid and over-limit payloads", async () => {
  const repository = open();
  // FF D8 FF E1 是 APP1/Exif JPEG marker；不能仅因不是固定 APP0 前缀而拒绝。
  await repository.persist({ ...event("55555555-5555-4555-8555-555555555555", "18"), photoBase64: "/9j/4QAA" }, async () => "aGVsbG8=");
  await assert.rejects(() => repository.persist({ ...event("66666666-6666-4666-8666-666666666666", "18"), photoBase64: "QUJDRA==" }, async () => "aGVsbG8="), /FACE_PHOTO_INVALID/);
  await assert.rejects(() => repository.persist({ ...event("77777777-7777-4777-8777-777777777777", "18"), photoBase64: "/9j/4AAQ" + "A".repeat(2_796_196) }, async () => "aGVsbG8="), /FACE_PHOTO_INVALID/);
  assert.equal((await repository.snapshot()).entries.length, 1);
});

test("face queue retains photo when a server response is not a complete durable acknowledgement", async () => {
  const repository = open();
  await repository.persist(event("88888888-8888-4888-8888-888888888888", "18"), async () => "aGVsbG8=");
  const claimed = await repository.claimNextUpload("weak-ack", NOW);
  await assert.rejects(() => repository.applyServerResult(claimed!.eventGuid, "weak-ack", {
    status: "verified", reasonCode: null, punchGuid: null, receivedAtUtc: null, updatedAtUtc: NOW,
  } as never), /FACE_SERVER_ACK_INVALID/);
  const reclaimed = await repository.claimNextUpload("photo-must-remain", "2026-09-09T18:01:00.000Z");
  assert.equal(reclaimed?.photoBase64, JPEG);
});

test("face queue storage-full write rolls back scope and sequence; record is never reported as saved", async () => {
  const raw = new DatabaseSync(":memory:"); raw.exec(POS_DATABASE_MIGRATIONS.find((m) => m.version === 44)!.sql);
  const base = new NodeSqliteConnection(raw);
  const failing: SqliteConnectionPort = {
    exec: (sql) => base.exec(sql), getFirst: (sql, p) => base.getFirst(sql, p), getAll: (sql, p) => base.getAll(sql, p), close: () => base.close(),
    run: async (sql, p) => { if (sql.startsWith("INSERT INTO face_attendance_events")) throw new Error("SQLITE_FULL"); return base.run(sql, p); },
    withExclusiveTransaction: (operation) => base.withExclusiveTransaction(async () => operation(failing)),
  };
  const repository = new SqliteFaceAttendanceRepository(failing, { storeCode: "S1", deviceCode: "D1", hardwareId: "H1" }, () => NOW);
  await assert.rejects(() => repository.persist(event("55555555-5555-4555-8555-555555555555", "18"), async () => "aGVsbG8="), /SQLITE_FULL/);
  assert.equal((await repository.snapshot()).entries.length, 0);
  const scope = await base.getFirst<{ next_local_sequence: number }>("SELECT next_local_sequence FROM face_attendance_scope_state");
  assert.equal(scope, null);
});


test("face roster stores each manager capability and clears all capabilities when offline", async () => {
  const repository = open();
  await repository.replaceRoster({ rosterVersion: "4", serverTimeUtc: NOW, storeTimeZone: "Australia/Brisbane", canManage: true, canViewPhotos: true, canReview: false, employees: [] });
  assert.deepEqual((await repository.snapshot()), { employees: [], entries: [], rosterVersion: "4", serverTimeUtc: NOW, storeTimeZone: "Australia/Brisbane", lastSyncedAtUtc: NOW, canManage: true, canViewPhotos: true, canReview: false, hasSyncedRoster: true });
  await repository.clearManagerPermissions();
  const snapshot = await repository.snapshot();
  assert.equal(snapshot.canManage, false); assert.equal(snapshot.canViewPhotos, false); assert.equal(snapshot.canReview, false);
});

test("device session time does not impersonate a successful roster refresh", async () => {
  let now = "2026-09-09T17:00:00.000Z";
  const raw = new DatabaseSync(":memory:"); raw.exec(POS_DATABASE_MIGRATIONS.find((migration) => migration.version === 44)!.sql);
  const repository = new SqliteFaceAttendanceRepository(new NodeSqliteConnection(raw), { storeCode: "S1", deviceCode: "D1", hardwareId: "H1" }, () => now);
  await repository.replaceRoster({ rosterVersion: "4", serverTimeUtc: "2026-09-09T17:00:00.000Z", storeTimeZone: "Australia/Brisbane", canManage: false, employees: [] });
  now = NOW;
  await repository.saveDeviceSession({ keyId: "key", timeAnchorId: "anchor", serverTimeUtc: "2026-09-09T18:00:00.000Z", serverEpochMs: Date.parse(NOW), uptimeMs: 100, bootEpochMs: Date.parse(NOW) - 100 });
  const snapshot = await repository.snapshot();
  assert.equal(snapshot.serverTimeUtc, NOW);
  assert.equal(snapshot.lastSyncedAtUtc, "2026-09-09T17:00:00.000Z");
});
