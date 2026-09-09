
import type { FaceAttendanceEmployee, FaceServerStatus } from "./face-attendance-contract";
import type { FaceMultipartFilePort } from "./face-multipart-file";
import { assertFaceJpegSize } from "./face-photo-validation";

import { HbposApiError, type HbposTransport } from "@/core/api/hbpos-api";

export type FaceDeviceSession = Readonly<{
  keyId: string; secretBase64: string | null; timeAnchorId: string; serverTimeUtc: string;
}>;
export type FaceEventResult = Readonly<{
  eventGuid: string; status: FaceServerStatus; reasonCode: string | null; punchGuid: string | null;
  receivedAtUtc: string; updatedAtUtc: string;
}>;
export type FaceRoster = Readonly<{
  rosterVersion: string; serverTimeUtc: string; storeTimeZone: string; canManage: boolean; canViewPhotos: boolean; canReview: boolean; employees: readonly FaceAttendanceEmployee[];
}>;

/** 所有请求都经 HbposTransport，因此认证 headers 与撤销门禁保持既有实现。 */
export class HbposFaceAttendanceApi {
  public constructor(private readonly transport: HbposTransport, private readonly files: FaceMultipartFilePort) {}

  public async roster(storeCode: string): Promise<FaceRoster> {
    const response = await this.transport.request<unknown>({ method: "GET", url: `/api/v1/attendance/face/employees?storeCode=${encodeURIComponent(storeCode)}` });
    return parseRoster(response.data);
  }

  public async deviceSession(input: Readonly<{ storeCode: string; deviceCode: string; hardwareId: string; deviceObservedAtUtc: string; keyId: string | null; nonce: string; signature: string | null; registrationMode?: "recover" }>): Promise<FaceDeviceSession> {
    const response = await this.transport.request<unknown>({ method: "POST", url: "/api/v1/attendance/face/device-session", data: input });
    return parseDeviceSession(response.data);
  }

  public async getEvent(eventGuid: string): Promise<FaceEventResult | null> {
    try {
      const response = await this.transport.request<unknown>({ method: "GET", url: `/api/v1/attendance/face/events/${encodeURIComponent(eventGuid)}` });
      return parseEvent(response.data);
    } catch (error) {
      if (error instanceof HbposApiError && error.status === 404) return null;
      throw error;
    }
  }

  public async postEvent(input: Readonly<{ metadata: Record<string, unknown>; photoBase64: string }>): Promise<FaceEventResult> {
    assertFaceJpegSize(input.photoBase64);
    const photo = await this.files.create(input.photoBase64);
    try {
      const form = new FormData();
      // 普通 form field 才能绑定 ASP.NET [FromForm] string metadata；不得 append Blob/file part。
      form.append("metadata", JSON.stringify(input.metadata));
      form.append("photo", photo.file as unknown as Blob);
      const response = await this.transport.request<unknown>({ method: "POST", url: "/api/v1/attendance/face/events", data: form });
      return parseEvent(response.data);
    } finally { await photo.release(); }
  }

  public async getPhoto(eventGuid: string): Promise<string> {
    const response = await this.transport.request<unknown>({ method: "GET", url: `/api/v1/attendance/face/events/${encodeURIComponent(eventGuid)}/photo?encoding=base64` });
    const imageBase64 = text(record(response.data).imageBase64);
    assertFaceJpegSize(imageBase64);
    return imageBase64;
  }

  public async review(input: Readonly<{ eventGuid: string; decision: "approve" | "reject"; reason: string }>): Promise<void> {
    if (!input.eventGuid || !input.reason.trim()) throw new Error("FACE_REVIEW_INVALID");
    await this.transport.request<unknown>({ method: "POST", url: `/api/v1/attendance/face/events/${encodeURIComponent(input.eventGuid)}/review`, data: { decision: input.decision, reason: input.reason.trim() } });
  }

  public async enroll(input: Readonly<{ userGuid: string; storeCode: string; deviceCode: string; hardwareId: string; photosBase64: readonly string[] }>): Promise<void> {
    if (input.photosBase64.length !== 3) throw new Error("FACE_ENROLLMENT_REQUIRES_THREE_PHOTOS");
    for (const value of input.photosBase64) assertFaceJpegSize(value);
    const photos: Awaited<ReturnType<FaceMultipartFilePort["create"]>>[] = [];
    try {
      for (const value of input.photosBase64) photos.push(await this.files.create(value));
      const form = new FormData();
      form.append("userGuid", input.userGuid); form.append("storeCode", input.storeCode);
      form.append("deviceCode", input.deviceCode); form.append("hardwareId", input.hardwareId);
      for (const photo of photos) form.append("photos", photo.file as unknown as Blob);
      await this.transport.request<unknown>({ method: "POST", url: "/api/v1/attendance/face/enrollments", data: form });
    } finally { await Promise.all(photos.map((photo) => photo.release())); }
  }

  public async revoke(input: Readonly<{ userGuid: string; storeCode: string; reason: string }>): Promise<void> {
    await this.transport.request<unknown>({ method: "POST", url: `/api/v1/attendance/face/enrollments/${encodeURIComponent(input.userGuid)}/revoke`, data: { storeCode: input.storeCode, reason: input.reason } });
  }
}

function parseRoster(value: unknown): FaceRoster {
  const object = record(value); const employees = Array.isArray(object.employees) ? object.employees : [];
  return Object.freeze({ rosterVersion: revision(object.rosterVersion), serverTimeUtc: iso(object.serverTimeUtc), storeTimeZone: text(object.storeTimeZone), canManage: object.canManage === true, canViewPhotos: object.canViewPhotos === true, canReview: object.canReview === true,
    employees: Object.freeze(employees.map((item) => { const row = record(item); return Object.freeze({ userGuid: text(row.userGuid), employeeCode: optionalText(row.employeeCode), displayName: text(row.displayName), enrollmentVersion: revision(row.enrollmentVersion), enrollmentStatus: row.enrollmentStatus === "active" || row.enrollmentStatus === "revoked" ? row.enrollmentStatus : "none", lastPunchType: row.lastPunchType === "clockIn" || row.lastPunchType === "clockOut" ? row.lastPunchType : null, lastPunchTimeUtc: row.lastPunchTimeUtc === null || row.lastPunchTimeUtc === undefined ? null : iso(row.lastPunchTimeUtc) }); })) });
}
function parseDeviceSession(value: unknown): FaceDeviceSession {
  const row = record(value); return Object.freeze({ keyId: text(row.keyId), secretBase64: row.deviceKeySecret === null || row.deviceKeySecret === undefined ? null : base64(row.deviceKeySecret), timeAnchorId: text(row.timeAnchorId), serverTimeUtc: iso(row.serverObservedAtUtc) });
}
function parseEvent(value: unknown): FaceEventResult {
  const row = record(value); const status = row.status;
  if (status !== "queued" && status !== "verifying" && status !== "verified" && status !== "needsReview" && status !== "rejected") throw new Error("FACE_EVENT_RESPONSE_INVALID");
  const punchGuid = optionalText(row.punchGuid);
  // 本地只有收到中心 durable ACK 后才能销毁原图；verified 还必须能定位生成的 punch。
  if (status === "verified" && !punchGuid) throw new Error("FACE_EVENT_RESPONSE_INVALID");
  return Object.freeze({ eventGuid: text(row.eventGuid), status, reasonCode: optionalText(row.reasonCode), punchGuid, receivedAtUtc: iso(row.receivedAtUtc), updatedAtUtc: iso(row.updatedAtUtc) });
}
function record(value: unknown): Record<string, unknown> { if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error("FACE_RESPONSE_INVALID"); return value as Record<string, unknown>; }
function text(value: unknown): string { if (typeof value !== "string" || value.trim() !== value || !value) throw new Error("FACE_RESPONSE_INVALID"); return value; }
function optionalText(value: unknown): string | null { return value === null || value === undefined ? null : text(value); }
function iso(value: unknown): string {
  const raw = text(value);
  // .NET DateTime JSON 可为无小数或 1–7 位小数 UTC；读取后统一成签名使用的 .fffZ。
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$/u.test(raw)) throw new Error("FACE_RESPONSE_INVALID");
  const milliseconds = Date.parse(raw);
  if (!Number.isFinite(milliseconds)) throw new Error("FACE_RESPONSE_INVALID");
  return new Date(milliseconds).toISOString();
}
function base64(value: unknown): string { const raw = text(value); if (!/^[A-Za-z0-9+/]{43}=$/u.test(raw)) throw new Error("FACE_RESPONSE_INVALID"); return raw; }
function revision(value: unknown): string { const number = typeof value === "number" ? value : typeof value === "string" && /^(0|[1-9]\d*)$/u.test(value) ? Number(value) : NaN; if (!Number.isSafeInteger(number) || number < 0) throw new Error("FACE_RESPONSE_INVALID"); return String(number); }
