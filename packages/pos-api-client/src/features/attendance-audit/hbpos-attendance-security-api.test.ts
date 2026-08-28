import assert from "node:assert/strict";
import test from "node:test";

import {
  AttendanceSecurityApiError,
  HbposAttendanceSecurityApi,
  type AttendanceSecurityTransport,
  type AttendanceSecurityTransportRequest,
  type AttendanceSecurityTransportResponse,
} from "./hbpos-attendance-security-api";

class QueueTransport implements AttendanceSecurityTransport {
  public readonly requests: AttendanceSecurityTransportRequest[] = [];
  private readonly responses: AttendanceSecurityTransportResponse<unknown>[] =
    [];

  public enqueue(
    status: number,
    data: unknown,
  ): void {
    this.responses.push({ status, data });
  }

  public async request<T>(
    request: AttendanceSecurityTransportRequest,
  ): Promise<AttendanceSecurityTransportResponse<T>> {
    this.requests.push(request);
    const response = this.responses.shift();
    if (!response) throw new Error("missing fake response");
    return response as AttendanceSecurityTransportResponse<T>;
  }
}

test("考勤密钥使用设备认证 PUT，并严格校验 kid 与服务端时间", async () => {
  const transport = new QueueTransport();
  transport.enqueue(200, {
    success: true,
    data: {
      kid: "kid_01",
      registeredAtUtc: "2026-07-28T01:02:03.000Z",
      serverTimeUtc: "2026-07-28T01:02:04.000Z",
    },
  });
  const api = new HbposAttendanceSecurityApi(transport);

  const result = await api.registerAttendanceKey({
    algorithm: "A256GCM",
    keyMaterialBase64Url: "A".repeat(43),
    kid: "kid_01",
  });

  assert.deepEqual(transport.requests, [
    {
      method: "PUT",
      url: "/api/v1/attendance/signing-key",
      data: {
        algorithm: "A256GCM",
        keyMaterial: "A".repeat(43),
        kid: "kid_01",
      },
    },
  ]);
  assert.deepEqual(result, {
    kid: "kid_01",
    registeredAtEpochMs: Date.parse("2026-07-28T01:02:03.000Z"),
    serverTimeEpochMs: Date.parse("2026-07-28T01:02:04.000Z"),
  });
});

test("公钥包使用条件 GET；304 保留缓存且 ACK 冲突返回服务端版本", async () => {
  const transport = new QueueTransport();
  transport.enqueue(304, null);
  transport.enqueue(409, {
    version: 8,
    serverTimeUtc: "2026-07-28T01:02:05.000Z",
  });
  const api = new HbposAttendanceSecurityApi(transport);

  const fetched = await api.fetchEmergencyPublicKeys(7);
  const acknowledged = await api.acknowledgeEmergencyPublicKeys(7);

  assert.deepEqual(fetched, { kind: "not-modified" });
  assert.deepEqual(acknowledged, {
    acknowledged: false,
    serverVersion: 8,
    serverTimeEpochMs: Date.parse("2026-07-28T01:02:05.000Z"),
  });
  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/emergency-login/public-keys",
      headers: {
        "If-None-Match": '"emergency-login-keys-v7"',
      },
      acceptedStatuses: [304],
    },
    {
      method: "POST",
      url: "/api/v1/emergency-login/public-keys/ack",
      data: { version: 7 },
      acceptedStatuses: [409],
    },
  ]);
});

test("公钥包只接受完整非负版本、唯一 kid 与严格 ES256 元数据", async () => {
  const transport = new QueueTransport();
  transport.enqueue(200, {
    version: 9,
    activeKeyId: "KEY01",
    generatedAtUtc: "2026-07-28T01:02:03.000Z",
    keys: [
      {
        kid: "KEY01",
        algorithm: "ES256",
        pem: `-----BEGIN PUBLIC KEY-----\n${"A".repeat(96)}\n-----END PUBLIC KEY-----`,
        fingerprint: "A".repeat(64),
      },
    ],
  });
  const api = new HbposAttendanceSecurityApi(transport);

  const fetched = await api.fetchEmergencyPublicKeys(null);

  assert.equal(fetched.kind, "changed");
  if (fetched.kind !== "changed") return;
  assert.equal(fetched.package.version, 9);
  assert.equal(fetched.package.keys[0]?.kid, "KEY01");
  assert.deepEqual(transport.requests, [
    {
      method: "GET",
      url: "/api/v1/emergency-login/public-keys",
    },
  ]);
});

test("响应 kid 漂移、无效时间或私钥 PEM 均失败关闭", async () => {
  const transport = new QueueTransport();
  const api = new HbposAttendanceSecurityApi(transport);
  transport.enqueue(200, {
    success: true,
    data: {
      kid: "other",
      registeredAtUtc: "not-a-date",
      serverTimeUtc: "2026-07-28T01:02:04.000Z",
    },
  });

  await assert.rejects(
    () =>
      api.registerAttendanceKey({
        algorithm: "A256GCM",
        keyMaterialBase64Url: "A".repeat(43),
        kid: "kid_01",
      }),
    (error: unknown) =>
      error instanceof AttendanceSecurityApiError &&
      error.kind === "invalid-response",
  );

  transport.enqueue(200, {
    version: 1,
    activeKeyId: "KEY01",
    generatedAtUtc: "2026-07-28T01:02:03.000Z",
    keys: [
      {
        kid: "KEY01",
        algorithm: "ES256",
        pem: "-----BEGIN PRIVATE KEY-----\nSECRET\n-----END PRIVATE KEY-----",
        fingerprint: "A".repeat(64),
      },
    ],
  });
  await assert.rejects(
    () => api.fetchEmergencyPublicKeys(null),
    (error: unknown) =>
      error instanceof AttendanceSecurityApiError &&
      error.kind === "invalid-response",
  );
});
