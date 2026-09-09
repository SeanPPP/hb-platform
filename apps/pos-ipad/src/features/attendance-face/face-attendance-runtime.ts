import type { FaceAttendanceEntry, FaceAttendanceRecordInput, FaceAttendanceRuntime, FaceAttendanceState } from "./face-attendance-contract";
import { HbposFaceAttendanceApi } from "./hbpos-face-attendance-api";

import type { SqliteFaceAttendanceRepository, FaceAttendanceServerResult } from "@/core/db/sqlite-face-attendance-repository";


export type FaceAttendanceRuntimeDependencies = Readonly<{
  api: HbposFaceAttendanceApi;
  repository: SqliteFaceAttendanceRepository;
  scope: Readonly<{ storeCode: string; deviceCode: string; hardwareId: string }>;
  now(): Date;
  createGuid(): string;
  sha256JpegBytes(photoBase64: string): Promise<string>;
  isOnline(): Promise<boolean>;
  /** 原生 Keychain HMAC Port；secret 永不进入 JS state 或 SQLite。 */
  sign(keyId: string, canonicalMetadata: string): Promise<string>;
  hasKey(keyId: string): Promise<boolean>;
  saveKey(keyId: string, secretBase64: string): Promise<void>;
  systemUptimeMilliseconds(): number;
}>;

const EMPTY: FaceAttendanceState = Object.freeze({ employees: Object.freeze([]), entries: Object.freeze([]), rosterVersion: null, serverTimeUtc: null, storeTimeZone: null, lastSyncedAtUtc: null, canManage: false, canViewPhotos: false, canReview: false, online: false, hasSyncedRoster: false, syncing: false, lastErrorCode: null });

export class FaceAttendanceRuntimeService implements FaceAttendanceRuntime {
  private readonly listeners = new Set<() => void>();
  private current: FaceAttendanceState = EMPTY;
  private syncFlight: Promise<void> | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly recordFlights = new Set<Promise<FaceAttendanceEntry>>();
  /** refresh、管理动作也会碰 SQLCipher；shutdown 必须等它们全部停止。 */
  private readonly operationFlights = new Set<Promise<unknown>>();
  private disposed = false;
  /** 权限只在本次在线 roster 成功后有效，重启和离线都不能复用旧授权。 */
  private permissionsAuthenticated = false;
  /** 每次 actor 撤销或 refresh 起点递增，迟到 roster 不能恢复旧人的权限。 */
  private permissionGeneration = 0;

  public constructor(private readonly input: FaceAttendanceRuntimeDependencies) {}
  // 作为 useSyncExternalStore 的裸回调传递时不能丢失 this。
  public readonly state = (): FaceAttendanceState => this.current;
  public readonly subscribe = (listener: () => void): (() => void) => { this.listeners.add(listener); return () => this.listeners.delete(listener); };
  public readonly captureTime = (): Readonly<{ capturedAtUtc: string; capturedUptimeMilliseconds: number }> => {
    this.assertActive();
    // 这里连续读取同一 runtime clock；相机 helper 必须在 takePictureAsync 前直接调用。
    const capturedAtUtc = this.iso(this.input.now());
    const capturedUptimeMilliseconds = this.input.systemUptimeMilliseconds();
    if (!Number.isFinite(capturedUptimeMilliseconds) || capturedUptimeMilliseconds < 0) throw new Error("FACE_CAPTURE_UPTIME_INVALID");
    return Object.freeze({ capturedAtUtc, capturedUptimeMilliseconds });
  };
  /** shutdown 屏障：停止新任务，并等待已领租约的 sync 收尾后才允许关闭 SQLCipher。 */
  public async dispose(): Promise<void> {
    this.disposed = true;
    this.clearRetryTimer();
    // 已失败业务不能打断资源收尾；这里只等待，不向 shutdown 传播原业务错误。
    await Promise.allSettled([this.syncFlight, ...this.recordFlights, ...this.operationFlights]);
  }

  public refresh(): Promise<void> {
    this.assertActive();
    // 在第一个 await 前同步撤销，避免 cashier 切换期间的旧 capability 出现在 UI。
    const generation = this.invalidateManagementInternal();
    return this.trackOperation(this.refreshInternal(generation));
  }
  /** 可由 cashier invalidation bus 同步调用，不等待网络或 SQLite。 */
  public readonly invalidateManagement = (): void => { this.invalidateManagementInternal(); };
  private invalidateManagementInternal(): number {
    this.permissionGeneration += 1;
    this.permissionsAuthenticated = false;
    this.patch({ canManage: false, canViewPhotos: false, canReview: false });
    return this.permissionGeneration;
  }
  private async refreshInternal(generation: number): Promise<void> {
    await this.loadLocal();
    if (!(await this.input.isOnline())) { await this.clearManagerPermissions(); return; }
    this.assertActive();
    try {
      await this.ensureDeviceSession(true);
      const roster = await this.input.api.roster(this.input.scope.storeCode);
      this.assertActive();
      // 旧 cashier 的 roster 即使迟到，也不得覆盖新 actor 的 roster 或恢复管理能力。
      if (generation !== this.permissionGeneration) return;
      await this.input.repository.replaceRoster(roster);
      if (generation !== this.permissionGeneration) return;
      this.permissionsAuthenticated = true;
      await this.loadLocal();
      this.patch({ online: true, canManage: roster.canManage, canViewPhotos: roster.canViewPhotos, canReview: roster.canReview, lastSyncedAtUtc: this.iso(this.input.now()) });
    } catch (error) {
      // 被更新 actor 取消的旧 refresh 不应清掉新 actor 已证明的权限或覆盖其错误状态。
      if (generation === this.permissionGeneration && !this.disposed) {
        await this.clearManagerPermissions();
        // session 已保存也不能伪装 roster 刚同步；读回会保留真实 roster_synced_at_utc。
        await this.loadLocal().catch(() => undefined);
        this.patch({ online: true, lastErrorCode: errorCode(error) });
      }
      throw error;
    }
  }

  public record(input: FaceAttendanceRecordInput): Promise<FaceAttendanceEntry> {
    this.assertActive();
    const operation = this.recordInternal(input);
    this.recordFlights.add(operation);
    void operation.then(() => this.recordFlights.delete(operation), () => this.recordFlights.delete(operation));
    return operation;
  }

  private async recordInternal(input: FaceAttendanceRecordInput): Promise<FaceAttendanceEntry> {
    await this.loadLocal();
    if (!this.current.hasSyncedRoster) throw new Error("FACE_ROSTER_NOT_SYNCED");
    if (input.employee.enrollmentStatus !== "active") throw new Error("FACE_EMPLOYEE_NOT_ENROLLED");
    // 同一事件的持久化字段与验签字段必须来自同一 roster 快照；refresh 可在 await 期间更新 UI state。
    const rosterVersion = this.current.rosterVersion ?? "0";
    const signing = await this.input.repository.signingContext();
    if (!signing.keyId) throw new Error("FACE_DEVICE_SESSION_REQUIRED");
    const capturedAt = strictIso(input.capturedAtUtc);
    // observedAt 与快门墙钟冻结为同一事实；编码/哈希/SQLCipher 耗时不能改写签名元数据。
    const observedAt = capturedAt;
    const photoSha256 = (await this.input.sha256JpegBytes(input.photoBase64)).toLowerCase();
    const trusted = this.trustedTime(signing, capturedAt, input.capturedUptimeMilliseconds);
    const eventGuid = this.input.createGuid();
    let persisted;
    try {
      this.assertActive();
      persisted = await this.input.repository.persist({
      eventGuid, userGuid: input.employee.userGuid,
      employeeName: input.employee.displayName, punchType: input.punchType,
      occurredAtUtc: trusted.occurredAtUtc, deviceObservedAtUtc: observedAt,
      rosterVersion, enrollmentVersion: input.employee.enrollmentVersion,
      timeAnchorId: trusted.timeAnchorId, timeTrusted: trusted.timeTrusted, photoSha256,
      keyId: signing.keyId, photoBase64: input.photoBase64,
      }, (localSequence) => this.input.sign(signing.keyId!, canonicalEvent({
      userGuid: input.employee.userGuid, eventGuid, punchType: input.punchType, localSequence, occurredAtUtc: trusted.occurredAtUtc,
      deviceObservedAtUtc: observedAt, rosterVersion,
      enrollmentVersion: input.employee.enrollmentVersion, timeAnchorId: trusted.timeAnchorId,
      timeTrusted: trusted.timeTrusted, photoSha256, keyId: signing.keyId!, ...this.input.scope,
      })));
    } catch (error) {
      this.patch({ lastErrorCode: errorCode(error) });
      await this.loadLocal().catch(() => undefined);
      throw error;
    }
    await this.loadLocal();
    const entry = this.current.entries.find((item) => item.eventGuid === persisted.eventGuid);
    if (!entry) throw new Error("FACE_ATTENDANCE_PERSIST_READBACK_FAILED");
    // 记录成功的定义是 SQLCipher 已 readback；网络失败留给耐久队列退避。
    void this.sync().catch(() => undefined);
    return entry;
  }

  public sync(manual = false): Promise<void> {
    if (this.disposed) return Promise.reject(new Error("FACE_ATTENDANCE_DISPOSED"));
    if (this.syncFlight) return this.syncFlight;
    const run = this.runSync(manual).finally(() => { if (this.syncFlight === run) this.syncFlight = null; });
    this.syncFlight = run; return run;
  }
  public getPhoto(eventGuid: string): Promise<string> { this.assertActive(); return this.trackOperation(this.getPhotoInternal(eventGuid)); }
  private async getPhotoInternal(eventGuid: string): Promise<string> {
    if (!(await this.input.isOnline()) || !this.permissionsAuthenticated || !this.current.online || !this.current.canViewPhotos) throw new Error("FACE_MANAGER_ONLINE_AUTH_REQUIRED");
    this.assertActive(); return this.input.api.getPhoto(eventGuid);
  }
  public review(input: Readonly<{ eventGuid: string; decision: "approve" | "reject"; reason: string }>): Promise<void> { this.assertActive(); return this.trackOperation(this.reviewInternal(input)); }
  private async reviewInternal(input: Readonly<{ eventGuid: string; decision: "approve" | "reject"; reason: string }>): Promise<void> {
    if (!(await this.input.isOnline()) || !this.permissionsAuthenticated || !this.current.online || !this.current.canReview) throw new Error("FACE_MANAGER_ONLINE_AUTH_REQUIRED");
    await this.input.api.review(input); this.assertActive(); await this.sync(true);
  }
  public enroll(input: Readonly<{ userGuid: string; photosBase64: readonly string[] }>): Promise<void> { this.assertActive(); return this.trackOperation(this.enrollInternal(input)); }
  private async enrollInternal(input: Readonly<{ userGuid: string; photosBase64: readonly string[] }>): Promise<void> {
    if (!(await this.input.isOnline()) || !this.permissionsAuthenticated || !this.current.online || !this.current.canManage) throw new Error("FACE_MANAGER_ONLINE_AUTH_REQUIRED");
    await this.input.api.enroll({ userGuid: input.userGuid, photosBase64: input.photosBase64, ...this.input.scope });
    this.assertActive(); await this.refresh();
  }
  public revoke(input: Readonly<{ userGuid: string; reason: string }>): Promise<void> { this.assertActive(); return this.trackOperation(this.revokeInternal(input)); }
  private async revokeInternal(input: Readonly<{ userGuid: string; reason: string }>): Promise<void> {
    if (!(await this.input.isOnline()) || !this.permissionsAuthenticated || !this.current.online || !this.current.canManage) throw new Error("FACE_MANAGER_ONLINE_AUTH_REQUIRED");
    await this.input.api.revoke({ userGuid: input.userGuid, storeCode: this.input.scope.storeCode, reason: input.reason });
    this.assertActive(); await this.refresh();
  }

  private trackOperation<T>(operation: Promise<T>): Promise<T> {
    this.operationFlights.add(operation);
    void operation.then(() => this.operationFlights.delete(operation), () => this.operationFlights.delete(operation));
    return operation;
  }

  private async runSync(manual: boolean): Promise<void> {
    this.assertActive();
    if (!(await this.input.isOnline())) { await this.clearManagerPermissions(); return; }
    this.patch({ online: true, syncing: true, lastErrorCode: null });
    try {
      await this.ensureDeviceSession(false);
      if (manual) await this.input.repository.reopenNeedsReview();
      // 先 query 同 GUID，lost ACK 安全重放；每项独立处理，不能卡住 17 点之后的打卡。
      await this.drain("upload", false);
      await this.drain("review", manual);
      await this.loadLocal();
    } catch (error) {
      this.patch({ lastErrorCode: errorCode(error) });
      await this.loadLocal().catch(() => undefined);
      throw error;
    } finally { this.patch({ syncing: false }); if (!this.disposed) await this.scheduleRetry(); }
  }

  private async loadLocal(): Promise<void> {
    const value = await this.input.repository.snapshot();
    // roster 可离线保存供打卡选人，但管理权限必须由本次在线 roster 重新证明。
    this.current = Object.freeze({ ...this.current, ...value,
      canManage: this.permissionsAuthenticated && value.canManage,
      canViewPhotos: this.permissionsAuthenticated && value.canViewPhotos,
      canReview: this.permissionsAuthenticated && value.canReview,
      // 设备 session 可刷新 serverTimeUtc；只有 roster 成功替换后才算员工名单同步。
      lastSyncedAtUtc: value.lastSyncedAtUtc,
    }); this.emit();
  }
  private async clearManagerPermissions(): Promise<void> {
    this.permissionsAuthenticated = false;
    await this.input.repository.clearManagerPermissions?.().catch(() => undefined);
    this.patch({ online: false, canManage: false, canViewPhotos: false, canReview: false });
  }
  /** 同步复用已有锚，只有 refresh 才重新向服务端取 session，避免轮询期间移动拍摄锚点。 */
  private async ensureDeviceSession(force: boolean): Promise<void> {
    const current = await this.input.repository.signingContext();
    if (!force && current.keyId !== null && await this.input.hasKey(current.keyId)) return;
    const observedAt = this.iso(this.input.now());
    const nonce = this.input.createGuid();
    const hasKey = current.keyId !== null && await this.input.hasKey(current.keyId);
    const signature = hasKey
      ? await this.input.sign(current.keyId!, deviceSessionCanonical({ ...this.input.scope, observedAt, nonce, keyId: current.keyId! }))
      : null;
    const session = await this.input.api.deviceSession({ ...this.input.scope, deviceObservedAtUtc: observedAt, keyId: hasKey ? current.keyId : null, nonce, signature });
    this.assertActive();
    if (session.secretBase64) await this.input.saveKey(session.keyId, session.secretBase64);
    if (!(await this.input.hasKey(session.keyId))) throw new Error("FACE_DEVICE_KEY_UNAVAILABLE");
    const uptimeMs = this.input.systemUptimeMilliseconds();
    await this.input.repository.saveDeviceSession({ keyId: session.keyId, timeAnchorId: session.timeAnchorId, serverTimeUtc: session.serverTimeUtc, serverEpochMs: Date.parse(session.serverTimeUtc), uptimeMs, bootEpochMs: this.input.now().getTime() - uptimeMs });
  }
  private trustedTime(signing: Readonly<{ timeAnchorId: string | null; serverEpochMs: number | null; uptimeMs: number | null; bootEpochMs: number | null }>, capturedAtUtc: string, capturedUptimeMs: number | undefined): Readonly<{ occurredAtUtc: string; timeAnchorId: string | null; timeTrusted: boolean }> {
    const uptime = this.input.systemUptimeMilliseconds();
    const bootEpoch = this.input.now().getTime() - uptime;
    // 单调时钟和系统启动锚必须连续；任一墙钟回拨、重启或拍摄时钟不在锚之后均降级。
    if (signing.timeAnchorId && signing.serverEpochMs !== null && signing.uptimeMs !== null && signing.bootEpochMs !== null && typeof capturedUptimeMs === "number" && Number.isFinite(capturedUptimeMs) && capturedUptimeMs >= signing.uptimeMs && capturedUptimeMs <= uptime && Math.abs(bootEpoch - signing.bootEpochMs) < 2_000) return Object.freeze({ occurredAtUtc: this.iso(new Date(signing.serverEpochMs + capturedUptimeMs - signing.uptimeMs)), timeAnchorId: signing.timeAnchorId, timeTrusted: true });
    return Object.freeze({ occurredAtUtc: capturedAtUtc, timeAnchorId: signing.timeAnchorId, timeTrusted: false });
  }
  private async drain(kind: "upload" | "review", manual: boolean): Promise<void> {
    for (let count = 0; count < 100; count += 1) {
      if (this.disposed) return;
      const leaseId = this.input.createGuid();
      const claimed = kind === "upload"
        ? await this.input.repository.claimNextUpload(leaseId, this.iso(new Date(this.input.now().getTime() + 60_000)))
        : await this.input.repository.claimNextReview(leaseId, this.iso(new Date(this.input.now().getTime() + 60_000)), manual);
      if (!claimed) return;
      try {
        const existing = await this.input.api.getEvent(claimed.eventGuid);
        if (this.disposed) return;
        const result = existing ?? await this.input.api.postEvent({ metadata: eventMetadata(claimed), photoBase64: claimed.photoBase64 });
        if (this.disposed) return;
        if (result.eventGuid !== claimed.eventGuid) throw new Error("FACE_EVENT_GUID_MISMATCH");
        await this.input.repository.applyServerResult(claimed.eventGuid, leaseId, toServerResult(result));
      } catch (error) {
        const delay = Math.min(3_600_000, 1_000 * 2 ** Math.min(12, claimed.attemptCount));
        await this.input.repository.releaseForRetry(claimed.eventGuid, leaseId, errorCode(error), this.iso(new Date(this.input.now().getTime() + delay)));
        this.patch({ lastErrorCode: errorCode(error) });
      }
    }
  }
  private async scheduleRetry(): Promise<void> {
    if (this.disposed) return;
    this.clearRetryTimer();
    const due = await this.input.repository.nextDueAtUtc();
    if (!due) return;
    const delay = Math.max(250, Math.min(3_600_000, Date.parse(due) - this.input.now().getTime()));
    this.retryTimer = setTimeout(() => { this.retryTimer = null; if (!this.disposed) void this.sync().catch(() => undefined); }, delay);
  }
  private clearRetryTimer(): void { if (this.retryTimer) clearTimeout(this.retryTimer); this.retryTimer = null; }
  private assertActive(): void { if (this.disposed) throw new Error("FACE_ATTENDANCE_DISPOSED"); }
  private iso(date: Date): string { return date.toISOString(); }
  private patch(value: Partial<FaceAttendanceState>): void { this.current = Object.freeze({ ...this.current, ...value }); this.emit(); }
  private emit(): void { for (const listener of this.listeners) listener(); }
}

function eventMetadata(value: Readonly<Record<string, unknown>>): Record<string, unknown> {
  return Object.freeze({ eventGuid: value.eventGuid, userGuid: value.userGuid,
    storeCode: value.storeCode, deviceCode: value.deviceCode, hardwareId: value.hardwareId,
    punchType: value.punchType, occurredAtUtc: value.occurredAtUtc,
    deviceObservedAtUtc: value.deviceObservedAtUtc, localSequence: numericWire(value.localSequence),
    rosterVersion: numericWire(value.rosterVersion), enrollmentVersion: numericWire(value.enrollmentVersion),
    timeAnchorId: value.timeAnchorId, timeTrusted: value.timeTrusted,
    photoSha256: value.photoSha256, keyId: value.keyId, signature: value.signature });
}
function numericWire(value: unknown): number {
  const parsed = typeof value === "number" ? value : typeof value === "string" && /^(0|[1-9]\d*)$/u.test(value) ? Number(value) : NaN;
  if (!Number.isSafeInteger(parsed) || parsed < 0) throw new Error("FACE_WIRE_VERSION_INVALID");
  return parsed;
}
function canonicalEvent(value: Readonly<{
  eventGuid: string; userGuid: string; storeCode: string; deviceCode: string; hardwareId: string; punchType: string; localSequence: number;
  occurredAtUtc: string; deviceObservedAtUtc: string; rosterVersion: string; enrollmentVersion: string;
  timeAnchorId: string | null; timeTrusted: boolean; photoSha256: string; keyId: string;
}>): string {
  // 后端以该数组的序列化字节验签；不可替换为 object 或 pretty JSON。
  return JSON.stringify(["v1", value.eventGuid, value.userGuid, value.storeCode, value.deviceCode, value.hardwareId, value.punchType,
    value.occurredAtUtc, value.deviceObservedAtUtc, String(value.localSequence),
    String(value.rosterVersion), String(value.enrollmentVersion), value.timeAnchorId,
    String(value.timeTrusted), value.photoSha256.toLowerCase(), value.keyId]);
}
function toServerResult(value: Readonly<{ status: FaceAttendanceServerResult["status"]; reasonCode: string | null; punchGuid: string | null; receivedAtUtc: string; updatedAtUtc: string }>): FaceAttendanceServerResult { return value; }
function strictIso(value: string): string { if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/u.test(value)) throw new Error("FACE_CAPTURE_TIME_INVALID"); return value; }
function errorCode(error: unknown): string {
  if (error && typeof error === "object" && "code" in error) {
    const code = (error as Readonly<{ code?: unknown }>).code;
    // 优先 gateway 的稳定错误码，避免把可变的网络 message 误显示给收银员。
    if (typeof code === "string" && code.length > 0 && code.length <= 128 && code.trim() === code) return code;
  }
  return error instanceof Error && error.message ? error.message.slice(0, 128) : "FACE_SYNC_FAILED";
}
function deviceSessionCanonical(value: Readonly<{ storeCode: string; deviceCode: string; hardwareId: string; observedAt: string; nonce: string; keyId: string }>): string {
  return JSON.stringify(["v1", "device-session", value.storeCode, value.deviceCode, value.hardwareId, value.observedAt, value.nonce, value.keyId]);
}
