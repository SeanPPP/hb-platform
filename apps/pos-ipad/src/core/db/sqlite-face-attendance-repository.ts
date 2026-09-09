import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

import type {
  FaceAttendanceEmployee,
  FaceAttendanceEntry,
  FaceLocalState,
  FacePunchType,
  FaceServerStatus,
} from "@/features/attendance-face/face-attendance-contract";

export type FaceAttendanceScope = Readonly<{
  storeCode: string; deviceCode: string; hardwareId: string;
}>;
export type FaceAttendancePersistInput = Readonly<{
  eventGuid: string; userGuid: string; employeeName: string; punchType: FacePunchType;
  occurredAtUtc: string; deviceObservedAtUtc: string; rosterVersion: string;
  enrollmentVersion: string; timeAnchorId: string | null; timeTrusted: boolean;
  photoSha256: string; keyId: string; photoBase64: string;
}>;
export type FaceAttendanceSignedEvent = FaceAttendancePersistInput & Readonly<{
  localSequence: number; signature: string; scope: FaceAttendanceScope;
}>;
export type FaceAttendanceServerResult = Readonly<{
  status: FaceServerStatus; reasonCode: string | null; punchGuid: string | null;
  receivedAtUtc: string; updatedAtUtc: string;
}>;
export type ClaimedFaceEvent = FaceAttendanceSignedEvent & Readonly<{
  localState: FaceLocalState; attemptCount: number;
}>;

type ScopeRow = Readonly<{ roster_version: string | null; roster_synced_at_utc: string | null; server_time_utc: string | null; store_time_zone: string | null; can_manage: number; can_view_photos: number; can_review: number; time_anchor_id: string | null; trusted_server_epoch_ms: number | null; trusted_uptime_ms: number | null; trusted_boot_epoch_ms: number | null; face_hmac_key_id: string | null }>;
type EmployeeRow = Readonly<{ user_guid: string; display_name: string; employee_code: string | null; enrollment_version: string; enrollment_status: "active" | "revoked" | "none"; last_punch_type: FacePunchType | null; last_punch_time_utc: string | null }>;
type EventRow = Readonly<{ event_guid: string; user_guid: string; employee_name: string; punch_type: FacePunchType; occurred_at_utc: string; device_observed_at_utc: string; local_sequence: number; time_trusted: number; local_state: FaceLocalState; server_status: FaceServerStatus | null; reason_code: string | null }>;

/** SQLCipher 由 PosDatabase.open 验证；人脸 feature 从不取得裸 connection。 */
export class SqliteFaceAttendanceRepository {
  private readonly scopeKey: string;

  public constructor(private readonly db: SqliteConnectionPort, private readonly scope: FaceAttendanceScope, private readonly nowIso: () => string) {
    this.scopeKey = `${scope.storeCode}\n${scope.deviceCode}\n${scope.hardwareId}`;
  }

  public async snapshot(): Promise<Readonly<{ employees: readonly FaceAttendanceEmployee[]; entries: readonly FaceAttendanceEntry[]; rosterVersion: string | null; serverTimeUtc: string | null; storeTimeZone: string | null; lastSyncedAtUtc: string | null; canManage: boolean; canViewPhotos: boolean; canReview: boolean; hasSyncedRoster: boolean }>> {
    const [scope, employees, events] = await Promise.all([
      this.db.getFirst<ScopeRow>("SELECT roster_version, roster_synced_at_utc, server_time_utc, store_time_zone, can_manage, can_view_photos, can_review, time_anchor_id, trusted_server_epoch_ms, trusted_uptime_ms, trusted_boot_epoch_ms, face_hmac_key_id FROM face_attendance_scope_state WHERE scope_key = ?", [this.scopeKey]),
      this.db.getAll<EmployeeRow>("SELECT user_guid, display_name, employee_code, enrollment_version, enrollment_status, last_punch_type, last_punch_time_utc FROM face_attendance_roster WHERE scope_key = ? ORDER BY display_name, user_guid", [this.scopeKey]),
      this.db.getAll<EventRow>("SELECT event_guid, user_guid, employee_name, punch_type, occurred_at_utc, device_observed_at_utc, local_sequence, time_trusted, local_state, server_status, reason_code FROM face_attendance_events WHERE scope_key = ? ORDER BY local_sequence DESC LIMIT 100", [this.scopeKey]),
    ]);
    return Object.freeze({
      employees: Object.freeze(employees.map((row) => Object.freeze({ userGuid: row.user_guid, displayName: row.display_name, employeeCode: row.employee_code, enrollmentVersion: row.enrollment_version, enrollmentStatus: row.enrollment_status, lastPunchType: row.last_punch_type, lastPunchTimeUtc: row.last_punch_time_utc }))),
      entries: Object.freeze(events.map(toEntry)), rosterVersion: scope?.roster_version ?? null,
      serverTimeUtc: scope?.server_time_utc ?? null, storeTimeZone: scope?.store_time_zone ?? null,
      lastSyncedAtUtc: scope?.roster_synced_at_utc ?? null,
      canManage: scope?.can_manage === 1, canViewPhotos: scope?.can_view_photos === 1, canReview: scope?.can_review === 1,
      hasSyncedRoster: scope !== null && scope.roster_synced_at_utc !== null,
    });
  }

  public async signingContext(): Promise<Readonly<{ keyId: string | null; timeAnchorId: string | null; serverEpochMs: number | null; uptimeMs: number | null; bootEpochMs: number | null }>> {
    const scope = await this.db.getFirst<ScopeRow>("SELECT face_hmac_key_id, time_anchor_id, trusted_server_epoch_ms, trusted_uptime_ms, trusted_boot_epoch_ms, roster_version, roster_synced_at_utc, server_time_utc, store_time_zone, can_manage, can_view_photos, can_review FROM face_attendance_scope_state WHERE scope_key = ?", [this.scopeKey]);
    return Object.freeze({ keyId: scope?.face_hmac_key_id ?? null, timeAnchorId: scope?.time_anchor_id ?? null, serverEpochMs: scope?.trusted_server_epoch_ms ?? null, uptimeMs: scope?.trusted_uptime_ms ?? null, bootEpochMs: scope?.trusted_boot_epoch_ms ?? null });
  }

  public async saveDeviceSession(input: Readonly<{ keyId: string; timeAnchorId: string; serverTimeUtc: string; serverEpochMs: number; uptimeMs: number; bootEpochMs: number }>): Promise<void> {
    const now = this.nowIso();
    await this.db.withExclusiveTransaction(async (tx) => {
      await this.ensureScope(tx, now);
      await tx.run("UPDATE face_attendance_scope_state SET face_hmac_key_id = ?, time_anchor_id = ?, server_time_utc = ?, trusted_server_epoch_ms = ?, trusted_uptime_ms = ?, trusted_boot_epoch_ms = ?, updated_at_utc = ? WHERE scope_key = ?", [input.keyId, input.timeAnchorId, input.serverTimeUtc, input.serverEpochMs, input.uptimeMs, input.bootEpochMs, now, this.scopeKey]);
    });
  }

  public async replaceRoster(input: Readonly<{ rosterVersion: string; serverTimeUtc: string | null; storeTimeZone: string; canManage: boolean; canViewPhotos?: boolean; canReview?: boolean; employees: readonly FaceAttendanceEmployee[] }>): Promise<void> {
    const now = this.nowIso();
    await this.db.withExclusiveTransaction(async (tx) => {
      await this.ensureScope(tx, now);
      await tx.run("DELETE FROM face_attendance_roster WHERE scope_key = ?", [this.scopeKey]);
      for (const employee of input.employees) {
        await tx.run("INSERT INTO face_attendance_roster (scope_key, user_guid, display_name, employee_code, enrollment_version, enrollment_status, last_punch_type, last_punch_time_utc, roster_version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)", [this.scopeKey, employee.userGuid, employee.displayName, employee.employeeCode, employee.enrollmentVersion, employee.enrollmentStatus, employee.lastPunchType, employee.lastPunchTimeUtc, input.rosterVersion]);
      }
      await tx.run("UPDATE face_attendance_scope_state SET roster_version = ?, roster_synced_at_utc = ?, server_time_utc = ?, store_time_zone = ?, can_manage = ?, can_view_photos = ?, can_review = ?, updated_at_utc = ? WHERE scope_key = ?", [input.rosterVersion, now, input.serverTimeUtc, input.storeTimeZone, input.canManage ? 1 : 0, input.canViewPhotos === true ? 1 : 0, input.canReview === true ? 1 : 0, now, this.scopeKey]);
    });
  }

  /** 离线或刷新失败不得沿用店长权限；下一次在线 roster 才能重新授予。 */
  public async clearManagerPermissions(): Promise<void> {
    const now = this.nowIso();
    await this.db.withExclusiveTransaction(async (tx) => {
      await this.ensureScope(tx, now);
      await tx.run("UPDATE face_attendance_scope_state SET can_manage = 0, can_view_photos = 0, can_review = 0, updated_at_utc = ? WHERE scope_key = ?", [now, this.scopeKey]);
    });
  }

  /** sequence、签名与照片同一 SQLCipher 事务落盘；失败即不算“已记录”。 */
  public async persist(input: FaceAttendancePersistInput, sign: (sequence: number) => Promise<string>): Promise<FaceAttendanceSignedEvent> {
    validatePersistInput(input);
    return this.db.withExclusiveTransaction(async (tx) => {
      const now = this.nowIso();
      await this.ensureScope(tx, now);
      const sequence = await this.nextSequence(tx);
      const signature = await sign(sequence);
      if (!isStandardBase64(signature) || signature.length > 512) throw new Error("FACE_SIGNATURE_INVALID");
      await tx.run("INSERT INTO face_attendance_events (event_guid, scope_key, user_guid, employee_name, punch_type, occurred_at_utc, device_observed_at_utc, local_sequence, roster_version, enrollment_version, time_anchor_id, time_trusted, photo_sha256, key_id, signature, photo_base64, local_state, server_status, reason_code, punch_guid, received_at_utc, server_updated_at_utc, attempt_count, next_attempt_at_utc, lease_id, lease_expires_at_utc, created_at_utc, updated_at_utc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'pending-upload', NULL, NULL, NULL, NULL, NULL, 0, ?, NULL, NULL, ?, ?)", [input.eventGuid, this.scopeKey, input.userGuid, input.employeeName, input.punchType, input.occurredAtUtc, input.deviceObservedAtUtc, sequence, input.rosterVersion, input.enrollmentVersion, input.timeAnchorId, input.timeTrusted ? 1 : 0, input.photoSha256, input.keyId, signature, input.photoBase64, now, now, now]);
      return Object.freeze({ ...input, localSequence: sequence, signature, scope: this.scope });
    });
  }

  /** 先发送未持久确认的照片，绝不让轮询项挡住后续打卡。 */
  public claimNextUpload(leaseId: string, expiresAtUtc: string): Promise<ClaimedFaceEvent | null> {
    return this.claim(leaseId, expiresAtUtc, "local_state = 'pending-upload' AND photo_base64 IS NOT NULL");
  }

  /** queued/verifying 已得到耐久回执，只查询状态；needsReview 仅由手动 refresh 再开放。 */
  public claimNextReview(leaseId: string, expiresAtUtc: string, manual: boolean): Promise<ClaimedFaceEvent | null> {
    return this.claim(leaseId, expiresAtUtc, manual
      ? "local_state = 'pending-review'"
      : "local_state = 'pending-review' AND server_status IN ('queued', 'verifying')");
  }

  public async nextDueAtUtc(): Promise<string | null> {
    const row = await this.db.getFirst<Readonly<{ next_attempt_at_utc: string }>>("SELECT next_attempt_at_utc FROM face_attendance_events WHERE scope_key = ? AND ((local_state = 'pending-upload' AND photo_base64 IS NOT NULL) OR (local_state = 'pending-review' AND server_status IN ('queued', 'verifying'))) ORDER BY next_attempt_at_utc, local_sequence LIMIT 1", [this.scopeKey]);
    return row?.next_attempt_at_utc ?? null;
  }

  public reopenNeedsReview(): Promise<void> {
    return this.db.run("UPDATE face_attendance_events SET next_attempt_at_utc = ?, updated_at_utc = ? WHERE scope_key = ? AND local_state = 'pending-review' AND server_status = 'needsReview'", [this.nowIso(), this.nowIso(), this.scopeKey]).then(() => undefined);
  }

  private async claim(leaseId: string, expiresAtUtc: string, predicate: string): Promise<ClaimedFaceEvent | null> {
    const now = this.nowIso();
    return this.db.withExclusiveTransaction(async (tx) => {
      const row = await tx.getFirst<FaceAttendanceSignedEvent & Readonly<{ photo_base64: string | null; local_state: FaceLocalState; attemptCount: number }>>(`SELECT event_guid AS eventGuid, user_guid AS userGuid, employee_name AS employeeName, punch_type AS punchType, occurred_at_utc AS occurredAtUtc, device_observed_at_utc AS deviceObservedAtUtc, local_sequence AS localSequence, roster_version AS rosterVersion, enrollment_version AS enrollmentVersion, time_anchor_id AS timeAnchorId, time_trusted AS timeTrusted, photo_sha256 AS photoSha256, key_id AS keyId, signature, photo_base64, local_state, attempt_count AS attemptCount FROM face_attendance_events WHERE scope_key = ? AND (lease_id IS NULL OR lease_expires_at_utc <= ?) AND next_attempt_at_utc <= ? AND ${predicate} ORDER BY local_sequence LIMIT 1`, [this.scopeKey, now, now]);
      if (!row) return null;
      const update = await tx.run("UPDATE face_attendance_events SET lease_id = ?, lease_expires_at_utc = ?, updated_at_utc = ? WHERE event_guid = ? AND scope_key = ? AND (lease_id IS NULL OR lease_expires_at_utc <= ?)", [leaseId, expiresAtUtc, now, row.eventGuid, this.scopeKey, now]);
      if (update.changes !== 1) return null;
      return Object.freeze({ eventGuid: row.eventGuid, userGuid: row.userGuid, employeeName: row.employeeName, punchType: row.punchType, occurredAtUtc: row.occurredAtUtc, deviceObservedAtUtc: row.deviceObservedAtUtc, localSequence: Number(row.localSequence), rosterVersion: row.rosterVersion, enrollmentVersion: row.enrollmentVersion, timeAnchorId: row.timeAnchorId, timeTrusted: Number(row.timeTrusted) === 1, photoSha256: row.photoSha256, keyId: row.keyId, signature: row.signature, photoBase64: row.photo_base64 ?? "", localState: row.local_state, attemptCount: Number(row.attemptCount), scope: this.scope });
    });
  }

  public async applyServerResult(eventGuid: string, leaseId: string, result: FaceAttendanceServerResult): Promise<void> {
    assertDurableServerResult(result);
    const now = this.nowIso();
    const state: FaceLocalState = result.status === "verified" ? "confirmed" : result.status === "rejected" ? "rejected" : "pending-review";
    // worker 尚在核验时至少隔 30 秒再查，避免单个 queued 事件占满本轮 100 个 lease 或 250ms 定时器。
    const next = result.status === "needsReview" ? "9999-12-31T23:59:59.999Z" : state === "pending-review" ? plusMilliseconds(now, 30_000) : "9999-12-31T23:59:59.999Z";
    await this.db.run("UPDATE face_attendance_events SET local_state = ?, server_status = ?, reason_code = ?, punch_guid = ?, received_at_utc = ?, server_updated_at_utc = ?, photo_base64 = NULL, lease_id = NULL, lease_expires_at_utc = NULL, next_attempt_at_utc = ?, updated_at_utc = ? WHERE event_guid = ? AND scope_key = ? AND lease_id = ?", [state, result.status, result.reasonCode, result.punchGuid, result.receivedAtUtc, result.updatedAtUtc, next, now, eventGuid, this.scopeKey, leaseId]);
  }

  public releaseForRetry(eventGuid: string, leaseId: string, errorCode: string, retryAtUtc: string): Promise<void> {
    return this.db.run("UPDATE face_attendance_events SET attempt_count = attempt_count + 1, reason_code = ?, next_attempt_at_utc = ?, lease_id = NULL, lease_expires_at_utc = NULL, updated_at_utc = ? WHERE event_guid = ? AND scope_key = ? AND lease_id = ?", [errorCode, retryAtUtc, this.nowIso(), eventGuid, this.scopeKey, leaseId]).then(() => undefined);
  }

  private async ensureScope(tx: SqliteConnectionPort, now: string): Promise<void> {
    await tx.run("INSERT INTO face_attendance_scope_state (scope_key, store_code, device_code, hardware_id, updated_at_utc) VALUES (?, ?, ?, ?, ?) ON CONFLICT(scope_key) DO NOTHING", [this.scopeKey, this.scope.storeCode, this.scope.deviceCode, this.scope.hardwareId, now]);
  }
  private async nextSequence(tx: SqliteConnectionPort): Promise<number> {
    const row = await tx.getFirst<Readonly<{ next_local_sequence: number | string }>>("UPDATE face_attendance_scope_state SET next_local_sequence = next_local_sequence + 1, updated_at_utc = ? WHERE scope_key = ? RETURNING next_local_sequence", [this.nowIso(), this.scopeKey]);
    const value = Number(row?.next_local_sequence);
    if (!Number.isSafeInteger(value) || value < 1) throw new Error("FACE_ATTENDANCE_SEQUENCE_INVALID");
    return value;
  }
}

function toEntry(row: EventRow): FaceAttendanceEntry {
  return Object.freeze({ eventGuid: row.event_guid, userGuid: row.user_guid, employeeName: row.employee_name, punchType: row.punch_type, occurredAtUtc: row.occurred_at_utc, deviceObservedAtUtc: row.device_observed_at_utc, localSequence: Number(row.local_sequence), timeTrusted: row.time_trusted === 1, localState: row.local_state, serverStatus: row.server_status, reasonCode: row.reason_code });
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
const ISO_MILLIS = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/u;
const SHA256 = /^[a-f0-9]{64}$/u;
const REVISION = /^(0|[1-9]\d*)$/u;

/** 数据库保存前拒绝伪 base64、非 JPEG 和过大 payload，事务失败绝不产生“已记录”。 */
function validatePersistInput(input: FaceAttendancePersistInput): void {
  if (!UUID.test(input.eventGuid) || !input.userGuid || !input.employeeName || !input.keyId) throw new Error("FACE_EVENT_FIELDS_INVALID");
  if (input.punchType !== "clockIn" && input.punchType !== "clockOut") throw new Error("FACE_PUNCH_TYPE_INVALID");
  if (!ISO_MILLIS.test(input.occurredAtUtc) || !ISO_MILLIS.test(input.deviceObservedAtUtc)) throw new Error("FACE_EVENT_TIME_INVALID");
  if (!REVISION.test(input.rosterVersion) || !REVISION.test(input.enrollmentVersion)) throw new Error("FACE_EVENT_VERSION_INVALID");
  if (!SHA256.test(input.photoSha256) || !isJpegBase64(input.photoBase64)) throw new Error("FACE_PHOTO_INVALID");
}

function isJpegBase64(value: string): boolean {
  // 与 face worker 的 2MiB JPEG 上限一致；不可只比较 base64 字符数（无 padding 会多解出 1 byte）。
  if (value.length < 8 || value.length > 2_796_204 || value.length % 4 !== 0 || !isStandardBase64(value) || decodedBase64ByteLength(value) > 2 * 1024 * 1024) return false;
  // JPEG 允许 APP0、APP1/Exif 等多个 marker；这里只验证 SOI 后的 JPEG magic，不擅自限制合法 metadata。
  return value.startsWith("/9j/");
}

/** 删除照片前必须拿到完整 durable ACK；弱 200 只能保留原图并走 lease retry。 */
function assertDurableServerResult(result: FaceAttendanceServerResult): void {
  if (!ISO_MILLIS.test(result.receivedAtUtc) || !ISO_MILLIS.test(result.updatedAtUtc)) throw new Error("FACE_SERVER_ACK_INVALID");
  if (result.status === "verified" && !result.punchGuid) throw new Error("FACE_SERVER_ACK_INVALID");
}

function isStandardBase64(value: string): boolean {
  return /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/u.test(value);
}
function decodedBase64ByteLength(value: string): number {
  const padding = value.endsWith("==") ? 2 : value.endsWith("=") ? 1 : 0;
  return value.length / 4 * 3 - padding;
}
function plusMilliseconds(iso: string, milliseconds: number): string {
  const value = Date.parse(iso);
  if (!Number.isFinite(value)) throw new Error("FACE_QUEUE_TIME_INVALID");
  return new Date(value + milliseconds).toISOString();
}
