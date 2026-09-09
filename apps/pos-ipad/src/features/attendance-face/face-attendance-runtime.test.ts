import assert from "node:assert/strict";
import test from "node:test";

import { FaceAttendanceRuntimeService } from "./face-attendance-runtime";

const scope = { storeCode: "S1", deviceCode: "D1", hardwareId: "H1" };
const result = (eventGuid: string, status: "queued" | "verified" = "verified") => ({ eventGuid, status, reasonCode: null, punchGuid: null, receivedAtUtc: "2026-09-09T18:00:00.000Z", updatedAtUtc: "2026-09-09T18:00:00.000Z" });

class QueueRepository {
  public uploads = [
    { eventGuid: "11111111-1111-4111-8111-111111111111", userGuid: "a", employeeName: "A", punchType: "clockIn", occurredAtUtc: "2026-09-09T09:00:00.000Z", deviceObservedAtUtc: "2026-09-09T09:00:00.000Z", localSequence: 1, rosterVersion: "1", enrollmentVersion: "1", timeAnchorId: "t", timeTrusted: true, photoSha256: "a".repeat(64), keyId: "k", signature: "sig", photoBase64: "/9j/4AAQ", localState: "pending-upload", attemptCount: 0, scope },
    { eventGuid: "22222222-2222-4222-8222-222222222222", userGuid: "b", employeeName: "B", punchType: "clockOut", occurredAtUtc: "2026-09-09T17:00:00.000Z", deviceObservedAtUtc: "2026-09-09T17:00:00.000Z", localSequence: 2, rosterVersion: "1", enrollmentVersion: "1", timeAnchorId: "t", timeTrusted: true, photoSha256: "b".repeat(64), keyId: "k", signature: "sig", photoBase64: "/9j/4AAQ", localState: "pending-upload", attemptCount: 0, scope },
  ];
  public applied: string[] = [];
  public readonly retried: { eventGuid: string; errorCode: string }[] = [];
  public async snapshot() { return { employees: [], entries: [], rosterVersion: "1", serverTimeUtc: "2026-09-09T18:00:00.000Z", storeTimeZone: "Australia/Brisbane", canManage: false, canViewPhotos: false, canReview: false, hasSyncedRoster: true }; }
  public async signingContext() { return { keyId: "k", timeAnchorId: "t", serverEpochMs: Date.parse("2026-09-09T18:00:00.000Z"), uptimeMs: 100, bootEpochMs: Date.parse("2026-09-09T17:59:59.900Z") }; }
  public async saveDeviceSession() {}
  public async claimNextUpload() { return this.uploads.shift() ?? null; }
  public async claimNextReview() { return null; }
  public async applyServerResult(eventGuid: string) { this.applied.push(eventGuid); }
  public async releaseForRetry(eventGuid: string, _leaseId: string, errorCode: string) { this.retried.push({ eventGuid, errorCode }); }
  public async nextDueAtUtc(): Promise<string | null> { return null; }
}

test("runtime state and subscribe are safely unbound; lost ACK query does not block a later 17:00 event", async () => {
  const repository = new QueueRepository(); const posted: string[] = []; let sessionCalls = 0;
  const api = { async getEvent(guid: string) { return guid.startsWith("111") ? result(guid, "queued") : null; }, async postEvent(input: { metadata: Record<string, unknown> }) { posted.push(String(input.metadata.eventGuid)); assert.equal(typeof input.metadata.rosterVersion, "number"); assert.equal(typeof input.metadata.enrollmentVersion, "number"); return result(String(input.metadata.eventGuid)); }, async deviceSession() { sessionCalls += 1; return { keyId: "k", secretBase64: null, timeAnchorId: "t", serverTimeUtc: "2026-09-09T18:00:00.000Z" }; } };
  const runtime = new FaceAttendanceRuntimeService({ api: api as never, repository: repository as never, scope, now: () => new Date("2026-09-09T18:00:00.000Z"), createGuid: () => "33333333-3333-4333-8333-333333333333", sha256JpegBytes: async () => "a".repeat(64), isOnline: async () => true, sign: async () => "sig", hasKey: async () => true, saveKey: async () => undefined, systemUptimeMilliseconds: () => 100 });
  const read = runtime.state; const subscribe = runtime.subscribe; let notified = 0; const stop = subscribe(() => { notified += 1; });
  await runtime.sync(); stop(); await runtime.dispose();
  assert.equal(read().syncing, false); assert.equal(notified > 0, true); assert.deepEqual(repository.applied, ["11111111-1111-4111-8111-111111111111", "22222222-2222-4222-8222-222222222222"]); assert.deepEqual(posted, ["22222222-2222-4222-8222-222222222222"]); assert.equal(sessionCalls, 0);
});

class RecordingRepository {
  public entries: Record<string, unknown>[] = [];
  public rosterVersion = "4";
  public onPersist: (() => Promise<void>) | null = null;
  public constructor(private readonly bootEpochMs: number) {}
  public async snapshot() { return { employees: [{ userGuid: "u", displayName: "Ada", employeeCode: "1", enrollmentVersion: "2", enrollmentStatus: "active" as const, lastPunchType: null, lastPunchTimeUtc: null }], entries: this.entries, rosterVersion: this.rosterVersion, serverTimeUtc: "2026-09-09T18:00:00.000Z", storeTimeZone: "Australia/Brisbane", lastSyncedAtUtc: "2026-09-09T17:00:00.000Z", canManage: false, canViewPhotos: false, canReview: false, hasSyncedRoster: true }; }
  public async signingContext() { return { keyId: "k", timeAnchorId: "anchor", serverEpochMs: Date.parse("2026-09-09T18:00:00.000Z"), uptimeMs: 100, bootEpochMs: this.bootEpochMs }; }
  public async saveDeviceSession() {}
  public async persist(input: Record<string, unknown>, sign: (sequence: number) => Promise<string>) { await this.onPersist?.(); const signature = await sign(1); const saved = { ...input, eventGuid: input.eventGuid, userGuid: input.userGuid, employeeName: input.employeeName, punchType: input.punchType, occurredAtUtc: input.occurredAtUtc, deviceObservedAtUtc: input.deviceObservedAtUtc, localSequence: 1, timeTrusted: input.timeTrusted, localState: "pending-upload", serverStatus: null, reasonCode: null, signature }; this.entries.push(saved); return { ...saved, scope }; }
  public async replaceRoster(input: { rosterVersion: string }) { this.rosterVersion = input.rosterVersion; }
  public async claimNextUpload() { return null; } public async claimNextReview() { return null; } public async applyServerResult() {} public async releaseForRetry() {} public async nextDueAtUtc() { return null; } public async reopenNeedsReview() {}
}

function recordingRuntime(nowMillis: number, uptime: number, anchorBootEpochMs: number) {
  const repository = new RecordingRepository(anchorBootEpochMs);
  const runtime = new FaceAttendanceRuntimeService({ api: {} as never, repository: repository as never, scope, now: () => new Date(nowMillis), createGuid: () => "44444444-4444-4444-8444-444444444444", sha256JpegBytes: async () => "a".repeat(64), isOnline: async () => false, sign: async () => "sig", hasKey: async () => true, saveKey: async () => undefined, systemUptimeMilliseconds: () => uptime });
  return { runtime, repository };
}

test("runtime captureTime freezes shutter clocks and record derives a trusted anchor timestamp", async () => {
  const now = Date.parse("2026-09-09T18:00:00.000Z"); const { runtime, repository } = recordingRuntime(now, 200, now - 200);
  const capture = runtime.captureTime();
  assert.deepEqual(capture, { capturedAtUtc: "2026-09-09T18:00:00.000Z", capturedUptimeMilliseconds: 200 });
  await runtime.record({ employee: (await repository.snapshot()).employees[0]!, punchType: "clockIn", photoBase64: "/9j/4AAQ", capturedAtUtc: "2026-09-09T18:00:00.050Z", capturedUptimeMilliseconds: 150 });
  assert.equal(repository.entries[0]!.timeTrusted, true);
  assert.equal(repository.entries[0]!.occurredAtUtc, "2026-09-09T18:00:00.050Z");
  assert.equal(repository.entries[0]!.deviceObservedAtUtc, "2026-09-09T18:00:00.050Z");
  await runtime.dispose();
});

test("runtime marks a clock rollback as untrusted and keeps the shutter wall time", async () => {
  const now = Date.parse("2026-09-09T17:00:00.000Z"); const { runtime, repository } = recordingRuntime(now, 200, Date.parse("2026-09-09T17:59:59.800Z"));
  await runtime.record({ employee: (await repository.snapshot()).employees[0]!, punchType: "clockOut", photoBase64: "/9j/4AAQ", capturedAtUtc: "2026-09-09T17:00:00.025Z", capturedUptimeMilliseconds: 150 });
  assert.equal(repository.entries[0]!.timeTrusted, false);
  assert.equal(repository.entries[0]!.occurredAtUtc, "2026-09-09T17:00:00.025Z");
  await runtime.dispose();
});

test("record freezes roster version while refresh changes the current roster during persistence", async () => {
  const now = Date.parse("2026-09-09T18:00:00.000Z"); const repository = new RecordingRepository(now - 200); let canonical = "";
  const api = {
    async deviceSession() { return { keyId: "k", secretBase64: null, timeAnchorId: "anchor", serverTimeUtc: "2026-09-09T18:00:00.000Z" }; },
    async roster() { return { rosterVersion: "5", serverTimeUtc: "2026-09-09T18:00:00.000Z", storeTimeZone: "Australia/Brisbane", canManage: false, canViewPhotos: false, canReview: false, employees: [] }; },
  };
  const runtime = new FaceAttendanceRuntimeService({ api: api as never, repository: repository as never, scope, now: () => new Date(now), createGuid: () => "12121212-1212-4121-8121-121212121212", sha256JpegBytes: async () => "a".repeat(64), isOnline: async () => true, sign: async (_keyId, metadata) => { canonical = metadata; return "sig"; }, hasKey: async () => true, saveKey: async () => undefined, systemUptimeMilliseconds: () => 200 });
  repository.onPersist = () => runtime.refresh();
  await runtime.record({ employee: (await repository.snapshot()).employees[0]!, punchType: "clockIn", photoBase64: "/9j/4AAQ", capturedAtUtc: "2026-09-09T18:00:00.050Z", capturedUptimeMilliseconds: 150 });
  assert.equal(repository.entries[0]?.rosterVersion, "4");
  assert.equal(JSON.parse(canonical)[10], "4");
  assert.equal(runtime.state().rosterVersion, "5");
  await runtime.dispose();
});

test("runtime retries an incomplete acknowledgement without applying it or discarding the claimed photo", async () => {
  const repository = new QueueRepository(); repository.uploads = repository.uploads.slice(0, 1); const photo = repository.uploads[0]!.photoBase64;
  const api = { async getEvent() { throw new Error("FACE_EVENT_RESPONSE_INVALID"); }, async deviceSession() { return { keyId: "k", secretBase64: null, timeAnchorId: "t", serverTimeUtc: "2026-09-09T18:00:00.000Z" }; } };
  const runtime = new FaceAttendanceRuntimeService({ api: api as never, repository: repository as never, scope, now: () => new Date("2026-09-09T18:00:00.000Z"), createGuid: () => "13131313-1313-4131-8131-131313131313", sha256JpegBytes: async () => "a".repeat(64), isOnline: async () => true, sign: async () => "sig", hasKey: async () => true, saveKey: async () => undefined, systemUptimeMilliseconds: () => 100 });
  await runtime.sync();
  assert.deepEqual(repository.applied, []);
  assert.deepEqual(repository.retried, [{ eventGuid: "11111111-1111-4111-8111-111111111111", errorCode: "FACE_EVENT_RESPONSE_INVALID" }]);
  assert.equal(photo, "/9j/4AAQ");
  await runtime.dispose();
});

test("runtime exposes the bounded Hbpos error code instead of its unstable message", async () => {
  const repository = new QueueRepository(); repository.uploads = repository.uploads.slice(0, 1);
  const failure = Object.assign(new Error("network wording changes"), { code: "HBPOS_NETWORK_UNAVAILABLE" });
  const api = { async getEvent() { throw failure; }, async deviceSession() { return { keyId: "k", secretBase64: null, timeAnchorId: "t", serverTimeUtc: "2026-09-09T18:00:00.000Z" }; } };
  const runtime = new FaceAttendanceRuntimeService({ api: api as never, repository: repository as never, scope, now: () => new Date("2026-09-09T18:00:00.000Z"), createGuid: () => "14141414-1414-4141-8141-141414141414", sha256JpegBytes: async () => "a".repeat(64), isOnline: async () => true, sign: async () => "sig", hasKey: async () => true, saveKey: async () => undefined, systemUptimeMilliseconds: () => 100 });
  await runtime.sync();
  assert.equal(runtime.state().lastErrorCode, "HBPOS_NETWORK_UNAVAILABLE");
  await runtime.dispose();
});

test("a failed roster refresh keeps the prior roster timestamp after saving a newer device session", async () => {
  class TimestampRepository extends QueueRepository {
    public serverTimeUtc = "2026-09-09T17:00:00.000Z";
    public override async snapshot() { return { employees: [], entries: [], rosterVersion: "4", serverTimeUtc: this.serverTimeUtc, storeTimeZone: "Australia/Brisbane", lastSyncedAtUtc: "2026-09-09T17:00:00.000Z", canManage: false, canViewPhotos: false, canReview: false, hasSyncedRoster: true }; }
    public override async saveDeviceSession() { this.serverTimeUtc = "2026-09-09T18:00:00.000Z"; }
    public async clearManagerPermissions() {}
  }
  const repository = new TimestampRepository(); repository.uploads = [];
  const api = { async deviceSession() { return { keyId: "k", secretBase64: null, timeAnchorId: "t", serverTimeUtc: "2026-09-09T18:00:00.000Z" }; }, async roster() { throw new Error("ROSTER_DOWN"); } };
  const runtime = new FaceAttendanceRuntimeService({ api: api as never, repository: repository as never, scope, now: () => new Date("2026-09-09T18:00:00.000Z"), createGuid: () => "15151515-1515-4151-8151-151515151515", sha256JpegBytes: async () => "a".repeat(64), isOnline: async () => true, sign: async () => "sig", hasKey: async () => true, saveKey: async () => undefined, systemUptimeMilliseconds: () => 100 });
  await assert.rejects(() => runtime.refresh(), /ROSTER_DOWN/);
  assert.equal(runtime.state().serverTimeUtc, "2026-09-09T18:00:00.000Z");
  assert.equal(runtime.state().lastSyncedAtUtc, "2026-09-09T17:00:00.000Z");
  await runtime.dispose();
});

test("dispose waits an in-flight event query and never schedules a retry against a closed database", async () => {
  const repository = new QueueRepository(); repository.uploads = repository.uploads.slice(0, 1);
  let release!: (value: null) => void; const waiting = new Promise<null>((resolve) => { release = resolve; }); let entered!: () => void; const enteredQuery = new Promise<void>((resolve) => { entered = resolve; }); let nextDueCalls = 0;
  repository.nextDueAtUtc = async () => { nextDueCalls += 1; return "2026-09-09T18:00:00.000Z"; };
  const api = { async getEvent() { entered(); return waiting; }, async postEvent() { throw new Error("must not post"); }, async deviceSession() { return { keyId: "k", secretBase64: null, timeAnchorId: "t", serverTimeUtc: "2026-09-09T18:00:00.000Z" }; } };
  const runtime = new FaceAttendanceRuntimeService({ api: api as never, repository: repository as never, scope, now: () => new Date("2026-09-09T18:00:00.000Z"), createGuid: () => "66666666-6666-4666-8666-666666666666", sha256JpegBytes: async () => "a".repeat(64), isOnline: async () => true, sign: async () => "sig", hasKey: async () => true, saveKey: async () => undefined, systemUptimeMilliseconds: () => 100 });
  const sync = runtime.sync(); await enteredQuery;
  const stopped = runtime.dispose(); release(null);
  await Promise.all([sync, stopped]);
  assert.equal(nextDueCalls, 0);
  await assert.rejects(() => runtime.sync(), /FACE_ATTENDANCE_DISPOSED/);
});

test("manager photo and review ports reject before any API call when roster lacks online manager permission", async () => {
  const repository = new QueueRepository(); let called = false;
  const api = { async getPhoto() { called = true; return "/9j/4AAQ"; }, async review() { called = true; }, async deviceSession() { return { keyId: "k", secretBase64: null, timeAnchorId: "t", serverTimeUtc: "2026-09-09T18:00:00.000Z" }; } };
  const runtime = new FaceAttendanceRuntimeService({ api: api as never, repository: repository as never, scope, now: () => new Date("2026-09-09T18:00:00.000Z"), createGuid: () => "77777777-7777-4777-8777-777777777777", sha256JpegBytes: async () => "a".repeat(64), isOnline: async () => true, sign: async () => "sig", hasKey: async () => true, saveKey: async () => undefined, systemUptimeMilliseconds: () => 100 });
  await assert.rejects(() => runtime.getPhoto("11111111-1111-4111-8111-111111111111"), /FACE_MANAGER_ONLINE_AUTH_REQUIRED/);
  await assert.rejects(() => runtime.review({ eventGuid: "11111111-1111-4111-8111-111111111111", decision: "approve", reason: "checked" }), /FACE_MANAGER_ONLINE_AUTH_REQUIRED/);
  assert.equal(called, false); await runtime.dispose();
});


class PermissionRepository extends QueueRepository {
  public canManage = false; public canViewPhotos = false; public canReview = false;
  public override async snapshot() { return { employees: [], entries: [], rosterVersion: "1", serverTimeUtc: "2026-09-09T18:00:00.000Z", storeTimeZone: "Australia/Brisbane", canManage: this.canManage, canViewPhotos: this.canViewPhotos, canReview: this.canReview, hasSyncedRoster: true }; }
  public async replaceRoster(input: { canManage: boolean; canViewPhotos: boolean; canReview: boolean }) { this.canManage = input.canManage; this.canViewPhotos = input.canViewPhotos; this.canReview = input.canReview; }
  public async clearManagerPermissions() { this.canManage = false; this.canViewPhotos = false; this.canReview = false; }
  public async reopenNeedsReview() {}
}

function managerRuntime(permission: Readonly<{ canManage: boolean; canViewPhotos: boolean; canReview: boolean }>) {
  const repository = new PermissionRepository(); repository.uploads = []; let photos = 0; let reviews = 0; let enrolls = 0;
  const api = {
    async roster() { return { rosterVersion: "1", serverTimeUtc: "2026-09-09T18:00:00.000Z", storeTimeZone: "Australia/Brisbane", ...permission, employees: [] }; },
    async getPhoto() { photos += 1; return "/9j/4AAQ"; }, async review() { reviews += 1; }, async enroll() { enrolls += 1; },
    async deviceSession() { return { keyId: "k", secretBase64: null, timeAnchorId: "t", serverTimeUtc: "2026-09-09T18:00:00.000Z" }; },
  };
  const runtime = new FaceAttendanceRuntimeService({ api: api as never, repository: repository as never, scope, now: () => new Date("2026-09-09T18:00:00.000Z"), createGuid: () => "88888888-8888-4888-8888-888888888888", sha256JpegBytes: async () => "a".repeat(64), isOnline: async () => true, sign: async () => "sig", hasKey: async () => true, saveKey: async () => undefined, systemUptimeMilliseconds: () => 100 });
  return { runtime, calls: () => ({ photos, reviews, enrolls }) };
}

test("photo-only roster cannot review or enroll, while review-only roster cannot view photos", async () => {
  const photoOnly = managerRuntime({ canManage: false, canViewPhotos: true, canReview: false });
  await photoOnly.runtime.refresh();
  assert.equal(await photoOnly.runtime.getPhoto("11111111-1111-4111-8111-111111111111"), "/9j/4AAQ");
  await assert.rejects(() => photoOnly.runtime.review({ eventGuid: "11111111-1111-4111-8111-111111111111", decision: "approve", reason: "checked" }), /FACE_MANAGER_ONLINE_AUTH_REQUIRED/);
  await assert.rejects(() => photoOnly.runtime.enroll({ userGuid: "u", photosBase64: ["/9j/4AAQ", "/9j/4AAQ", "/9j/4AAQ"] }), /FACE_MANAGER_ONLINE_AUTH_REQUIRED/);
  assert.deepEqual(photoOnly.calls(), { photos: 1, reviews: 0, enrolls: 0 }); await photoOnly.runtime.dispose();

  const reviewOnly = managerRuntime({ canManage: false, canViewPhotos: false, canReview: true });
  await reviewOnly.runtime.refresh();
  await assert.rejects(() => reviewOnly.runtime.getPhoto("11111111-1111-4111-8111-111111111111"), /FACE_MANAGER_ONLINE_AUTH_REQUIRED/);
  await reviewOnly.runtime.review({ eventGuid: "11111111-1111-4111-8111-111111111111", decision: "approve", reason: "checked" });
  await assert.rejects(() => reviewOnly.runtime.enroll({ userGuid: "u", photosBase64: ["/9j/4AAQ", "/9j/4AAQ", "/9j/4AAQ"] }), /FACE_MANAGER_ONLINE_AUTH_REQUIRED/);
  assert.deepEqual(reviewOnly.calls(), { photos: 0, reviews: 1, enrolls: 0 }); await reviewOnly.runtime.dispose();
});


test("dispose waits a roster response before DB work and settles a rejected refresh without blocking shutdown", async () => {
  const repository = new PermissionRepository(); repository.uploads = []; let releaseRoster!: () => void; let replaceCalls = 0;
  const rosterPending = new Promise<void>((resolve) => { releaseRoster = resolve; });
  repository.replaceRoster = async (input) => { replaceCalls += 1; await PermissionRepository.prototype.replaceRoster.call(repository, input); };
  const api = {
    async deviceSession() { return { keyId: "k", secretBase64: null, timeAnchorId: "t", serverTimeUtc: "2026-09-09T18:00:00.000Z" }; },
    async roster() { await rosterPending; return { rosterVersion: "1", serverTimeUtc: "2026-09-09T18:00:00.000Z", storeTimeZone: "Australia/Brisbane", canManage: false, canViewPhotos: true, canReview: false, employees: [] }; },
  };
  const runtime = new FaceAttendanceRuntimeService({ api: api as never, repository: repository as never, scope, now: () => new Date("2026-09-09T18:00:00.000Z"), createGuid: () => "99999999-9999-4999-8999-999999999999", sha256JpegBytes: async () => "a".repeat(64), isOnline: async () => true, sign: async () => "sig", hasKey: async () => true, saveKey: async () => undefined, systemUptimeMilliseconds: () => 100 });
  const refresh = runtime.refresh(); await Promise.resolve();
  let disposed = false; const stopping = runtime.dispose().then(() => { disposed = true; });
  await Promise.resolve(); assert.equal(disposed, false); assert.equal(replaceCalls, 0);
  releaseRoster(); await assert.rejects(refresh, /FACE_ATTENDANCE_DISPOSED/); await stopping; assert.equal(replaceCalls, 0);

  let rejectRoster!: (error: Error) => void; const failedRoster = new Promise<never>((_resolve, reject) => { rejectRoster = reject; }); let enteredFailedRoster!: () => void; const failedRosterEntered = new Promise<void>((resolve) => { enteredFailedRoster = resolve; });
  const rejected = new FaceAttendanceRuntimeService({ api: { ...api, async roster() { enteredFailedRoster(); return failedRoster; } } as never, repository: new PermissionRepository() as never, scope, now: () => new Date("2026-09-09T18:00:00.000Z"), createGuid: () => "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", sha256JpegBytes: async () => "a".repeat(64), isOnline: async () => true, sign: async () => "sig", hasKey: async () => true, saveKey: async () => undefined, systemUptimeMilliseconds: () => 100 });
  const failedRefresh = rejected.refresh(); await failedRosterEntered; const failedStop = rejected.dispose(); rejectRoster(new Error("ROSTER_DOWN")); await assert.doesNotReject(() => failedStop); await assert.rejects(failedRefresh, /ROSTER_DOWN/);
});

test("cashier invalidation synchronously clears management and a delayed old roster cannot restore it", async () => {
  const repository = new PermissionRepository(); repository.uploads = [];
  let resolveOld!: (value: { rosterVersion: string; serverTimeUtc: string; storeTimeZone: string; canManage: boolean; canViewPhotos: boolean; canReview: boolean; employees: never[] }) => void;
  let resolveNew!: (value: { rosterVersion: string; serverTimeUtc: string; storeTimeZone: string; canManage: boolean; canViewPhotos: boolean; canReview: boolean; employees: never[] }) => void;
  const oldRoster = new Promise<ReturnType<typeof roster>>(resolve => { resolveOld = resolve; });
  const newRoster = new Promise<ReturnType<typeof roster>>(resolve => { resolveNew = resolve; });
  let rosterCalls = 0; let firstRosterEntered!: () => void; const firstRoster = new Promise<void>(resolve => { firstRosterEntered = resolve; });
  const roster = (canManage: boolean, canViewPhotos: boolean, canReview: boolean) => ({ rosterVersion: "1", serverTimeUtc: "2026-09-09T18:00:00.000Z", storeTimeZone: "Australia/Brisbane", canManage, canViewPhotos, canReview, employees: [] as never[] });
  const api = {
    async deviceSession() { return { keyId: "k", secretBase64: null, timeAnchorId: "t", serverTimeUtc: "2026-09-09T18:00:00.000Z" }; },
    async roster() { rosterCalls += 1; if (rosterCalls === 1) { firstRosterEntered(); return oldRoster; } return newRoster; },
  };
  const runtime = new FaceAttendanceRuntimeService({ api: api as never, repository: repository as never, scope, now: () => new Date("2026-09-09T18:00:00.000Z"), createGuid: () => "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", sha256JpegBytes: async () => "a".repeat(64), isOnline: async () => true, sign: async () => "sig", hasKey: async () => true, saveKey: async () => undefined, systemUptimeMilliseconds: () => 100 });
  const oldRefresh = runtime.refresh(); await firstRoster;
  runtime.invalidateManagement();
  assert.deepEqual({ canManage: runtime.state().canManage, canViewPhotos: runtime.state().canViewPhotos, canReview: runtime.state().canReview }, { canManage: false, canViewPhotos: false, canReview: false });
  const newRefresh = runtime.refresh();
  resolveNew(roster(false, false, false)); await newRefresh;
  resolveOld(roster(true, true, true)); await oldRefresh;
  assert.deepEqual({ canManage: runtime.state().canManage, canViewPhotos: runtime.state().canViewPhotos, canReview: runtime.state().canReview }, { canManage: false, canViewPhotos: false, canReview: false });
  await runtime.dispose();
});
