export type FacePunchType = "clockIn" | "clockOut";
export type FaceServerStatus = "queued" | "verifying" | "verified" | "needsReview" | "rejected";
export type FaceLocalState = "pending-upload" | "pending-review" | "confirmed" | "rejected";

export type FaceAttendanceEmployee = Readonly<{
  userGuid: string; displayName: string; employeeCode: string | null;
  enrollmentVersion: string; enrollmentStatus: "active" | "revoked" | "none";
  lastPunchType: FacePunchType | null; lastPunchTimeUtc: string | null;
}>;
export type FaceAttendanceEntry = Readonly<{
  eventGuid: string; userGuid: string; employeeName: string; punchType: FacePunchType;
  occurredAtUtc: string; deviceObservedAtUtc: string; localSequence: number;
  timeTrusted: boolean; localState: FaceLocalState; serverStatus: FaceServerStatus | null;
  reasonCode: string | null;
}>;
export type FaceAttendanceState = Readonly<{
  employees: readonly FaceAttendanceEmployee[]; entries: readonly FaceAttendanceEntry[];
  rosterVersion: string | null; serverTimeUtc: string | null; storeTimeZone: string | null;
  lastSyncedAtUtc: string | null; canManage: boolean; canViewPhotos: boolean; canReview: boolean; online: boolean;
  hasSyncedRoster: boolean; syncing: boolean; lastErrorCode: string | null;
}>;
export type FaceAttendanceRecordInput = Readonly<{
  employee: FaceAttendanceEmployee; punchType: FacePunchType; photoBase64: string;
  /** 相机快门时冻结，不能在确认页提交时重新取本机时间。 */
  capturedAtUtc: string;
  /** 与快门同时读取的单调时钟；缺失或跨系统启动时必须降级为未可信时间。 */
  capturedUptimeMilliseconds?: number;
}>;
export type FaceAttendanceRuntime = Readonly<{
  state(): FaceAttendanceState; subscribe(listener: () => void): () => void;
  /** 快门前同一调用冻结墙钟与单调时钟，不暴露 Keychain 或签名材料。 */
  captureTime(): Readonly<{ capturedAtUtc: string; capturedUptimeMilliseconds: number }>;
  /** 收银员切换时同步撤销管理能力；下一次当前 actor roster 成功后才可恢复。 */
  invalidateManagement?(): void;
  refresh(): Promise<void>; record(input: FaceAttendanceRecordInput): Promise<FaceAttendanceEntry>;
  /** manual 会重新轮询需要人工复核的已回执事件。 */
  sync(manual?: boolean): Promise<void>;
  /** 仅在线店长查看，照片只驻留调用方内存，禁止写回 SQLite/cache。 */
  getPhoto?(eventGuid: string): Promise<string>;
  review?(input: Readonly<{ eventGuid: string; decision: "approve" | "reject"; reason: string }>): Promise<void>;
  enroll(input: Readonly<{ userGuid: string; photosBase64: readonly string[] }>): Promise<void>;
  revoke(input: Readonly<{ userGuid: string; reason: string }>): Promise<void>;
}>;
