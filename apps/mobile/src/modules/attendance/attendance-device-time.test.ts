import assert from "node:assert/strict";

function formatDeviceLocal(value: Date) {
  const pad = (part: number) => String(part).padStart(2, "0");
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`
    + `T${pad(value.getHours())}:${pad(value.getMinutes())}:${pad(value.getSeconds())}`;
}

async function run() {
  // 这个模块是移动端与 API 的时间边界：输入是设备本地时间，接口与打卡显示统一使用 UTC instant。
  const deviceTime = await import("./attendance-device-time");
  const utcWithSuffix = "2026-05-17T23:00:00Z";
  const utcWithoutSuffix = "2026-05-17T23:00:00";
  const expectedDeviceLocal = formatDeviceLocal(new Date(utcWithSuffix));

  assert.equal(
    deviceTime.toAttendanceDeviceLocalTime(utcWithSuffix),
    expectedDeviceLocal,
    "punchTimeUtc 必须显示为手机本地时间",
  );
  assert.equal(
    deviceTime.toAttendanceDeviceLocalTime(utcWithoutSuffix),
    expectedDeviceLocal,
    "没有 Z 后缀的 API UTC 时间也必须按 UTC instant 解析",
  );
  assert.equal(
    deviceTime.resolveAttendancePunchDisplayTime({
      punchTimeUtc: utcWithSuffix,
      punchTimeLocal: "1999-01-01T00:00:00",
      effectivePunchTime: "1999-01-01T00:00:00",
    }),
    expectedDeviceLocal,
    "手机打卡显示必须优先 punchTimeUtc，不能相信门店本地或 effective 时间",
  );
  assert.equal(
    deviceTime.toAttendancePunchTimeUtc("2026-05-18T09:00:00"),
    new Date("2026-05-18T09:00:00").toISOString(),
    "补卡输入的手机本地时间必须转换成 UTC 再发送",
  );

  const originalTimeZone = process.env.TZ;
  process.env.TZ = "Australia/Sydney";
  try {
    assert.equal(
      deviceTime.toAttendancePunchTimeUtc("2026-04-05T02:30:00"),
      "",
      "Sydney 夏令时回拨的 02:30 是双重含义，补卡必须拒绝而非静默选择一个 UTC instant",
    );
  } finally {
    process.env.TZ = originalTimeZone;
  }

  console.log("attendance-device-time.test.ts: ok");
}

void run();
