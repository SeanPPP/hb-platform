import assert from "node:assert/strict";
import test from "node:test";

import type { FaceMultipartFilePort } from "./face-multipart-file";
import { HbposFaceAttendanceApi } from "./hbpos-face-attendance-api";

import type { HbposTransport, HbposTransportRequest } from "@/core/api/hbpos-api";

class RecordingFormData {
  public readonly entries: (readonly [string, unknown])[] = [];
  public append(name: string, value: unknown): void { this.entries.push([name, value]); }
}
class RecordingTransport implements HbposTransport {
  public requestValue: HbposTransportRequest | null = null;
  public async request<T>(request: HbposTransportRequest): Promise<{ status: number; data: T }> {
    this.requestValue = request;
    return { status: 200, data: { eventGuid: "11111111-1111-4111-8111-111111111111", status: "queued", reasonCode: null, punchGuid: null, receivedAtUtc: "2026-09-09T18:00:00.000Z", updatedAtUtc: "2026-09-09T18:00:00.000Z" } as T };
  }
}

test("event multipart has a plain metadata field and a React Native JPEG uri part", async () => {
  const original = globalThis.FormData; const forms: RecordingFormData[] = [];
  globalThis.FormData = class extends RecordingFormData { public constructor() { super(); forms.push(this); } } as unknown as typeof FormData;
  try {
    let released = 0;
    const files: FaceMultipartFilePort = { async create() { return { file: { uri: "file:///cache/face.jpg", name: "face.jpg", type: "image/jpeg" }, release: async () => { released += 1; } }; } };
    const transport = new RecordingTransport(); const api = new HbposFaceAttendanceApi(transport, files);
    await api.postEvent({ metadata: { eventGuid: "11111111-1111-4111-8111-111111111111", rosterVersion: 4 }, photoBase64: "/9j/4AAQ" });
    assert.deepEqual(forms[0]?.entries, [["metadata", JSON.stringify({ eventGuid: "11111111-1111-4111-8111-111111111111", rosterVersion: 4 })], ["photo", { uri: "file:///cache/face.jpg", name: "face.jpg", type: "image/jpeg" }]]);
    assert.equal(released, 1); assert.equal(transport.requestValue?.url, "/api/v1/attendance/face/events");
  } finally { globalThis.FormData = original; }
});

test("enrollment sends three React Native file parts and releases every temporary file", async () => {
  const original = globalThis.FormData; const forms: RecordingFormData[] = [];
  globalThis.FormData = class extends RecordingFormData { public constructor() { super(); forms.push(this); } } as unknown as typeof FormData;
  try {
    let released = 0; let serial = 0;
    const files: FaceMultipartFilePort = { async create() { serial += 1; return { file: { uri: `file:///cache/${serial}.jpg`, name: `${serial}.jpg`, type: "image/jpeg" }, release: async () => { released += 1; } }; } };
    const api = new HbposFaceAttendanceApi(new RecordingTransport(), files);
    await api.enroll({ userGuid: "u", storeCode: "S", deviceCode: "D", hardwareId: "H", photosBase64: ["/9j/4AAQ", "/9j/4AAQ", "/9j/4AAQ"] });
    assert.deepEqual(forms[0]?.entries.map(([name]) => name), ["userGuid", "storeCode", "deviceCode", "hardwareId", "photos", "photos", "photos"]);
    assert.equal(released, 3);
  } finally { globalThis.FormData = original; }
});

test("normalizes .NET UTC timestamps with zero to seven fractional digits and rejects invalid dates", async () => {
  const files: FaceMultipartFilePort = { async create() { throw new Error("unused"); } };
  const transport: HbposTransport = { async request<T>(request: HbposTransportRequest) {
    const data = request.url.includes("employees")
      ? { rosterVersion: 4, serverTimeUtc: "2026-09-09T09:00:00Z", storeTimeZone: "Australia/Brisbane", canManage: false, canViewPhotos: true, canReview: true, employees: [{ userGuid: "u", employeeCode: "1", displayName: "Ada", enrollmentVersion: 2, enrollmentStatus: "active", lastPunchType: "clockIn", lastPunchTimeUtc: "2026-09-09T09:00:00.1234567Z" }] }
      : { keyId: "key", deviceKeySecret: null, timeAnchorId: "anchor", serverObservedAtUtc: "2026-09-09T09:00:00.12Z" };
    return { status: 200, data: data as T };
  } };
  const api = new HbposFaceAttendanceApi(transport, files);
  const roster = await api.roster("S"); const session = await api.deviceSession({ storeCode: "S", deviceCode: "D", hardwareId: "H", deviceObservedAtUtc: "2026-09-09T09:00:00.000Z", keyId: null, nonce: "n", signature: null });
  assert.equal(roster.serverTimeUtc, "2026-09-09T09:00:00.000Z");
  assert.equal(roster.canViewPhotos, true); assert.equal(roster.canReview, true);
  assert.equal(roster.employees[0]?.lastPunchTimeUtc, "2026-09-09T09:00:00.123Z");
  assert.equal(session.serverTimeUtc, "2026-09-09T09:00:00.120Z");
  const invalid = new HbposFaceAttendanceApi({ async request<T>() { return { status: 200, data: { rosterVersion: 4, serverTimeUtc: "2026-99-99T09:00:00Z", storeTimeZone: "Australia/Brisbane", canManage: false, employees: [] } as T }; } }, files);
  await assert.rejects(() => invalid.roster("S"), /FACE_RESPONSE_INVALID/);
});

test("event rejects incomplete durable acknowledgements before a runtime can remove its photo", async () => {
  const files: FaceMultipartFilePort = { async create() { throw new Error("unused"); } };
  const weak = { eventGuid: "11111111-1111-4111-8111-111111111111", status: "queued", reasonCode: null, punchGuid: null, receivedAtUtc: null, updatedAtUtc: "2026-09-09T18:00:00.000Z" };
  const verifiedWithoutPunch = { ...weak, status: "verified", receivedAtUtc: "2026-09-09T18:00:00.000Z" };
  for (const data of [weak, verifiedWithoutPunch]) {
    const api = new HbposFaceAttendanceApi({ async request<T>() { return { status: 200, data: data as T }; } }, files);
    await assert.rejects(() => api.getEvent("11111111-1111-4111-8111-111111111111"), /FACE_(?:EVENT_)?RESPONSE_INVALID/);
  }
});

test("manager photo endpoint returns only an in-memory validated JPEG base64", async () => {
  let url = ""; const files: FaceMultipartFilePort = { async create() { throw new Error("unused"); } };
  const api = new HbposFaceAttendanceApi({ async request<T>(request: HbposTransportRequest) { url = request.url; return { status: 200, data: { imageBase64: "/9j/4AAQ" } as T }; } }, files);
  assert.equal(await api.getPhoto("11111111-1111-4111-8111-111111111111"), "/9j/4AAQ");
  assert.equal(url, "/api/v1/attendance/face/events/11111111-1111-4111-8111-111111111111/photo?encoding=base64");
});
