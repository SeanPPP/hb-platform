import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(directory, "api.ts"), "utf8");
const types = readFileSync(join(directory, "types.ts"), "utf8");

assert.match(source, /export async function getMyAttendancePunchAdjustments/);
assert.match(source, /apiClient\.get\(`\$\{ATTENDANCE_BASE\}\/my\/punch-adjustments`\)/);
assert.match(source, /export async function previewMyAttendancePunchAdjustment/);
assert.match(source, /apiClient\.post\(\s*`\$\{ATTENDANCE_BASE\}\/my\/punch-adjustments\/preview`/);
assert.match(source, /normalizeAttendancePunchAdjustmentPreview\(response\.data\)/);
assert.match(source, /export async function createMyAttendancePunchAdjustment/);
assert.match(source, /apiClient\.post\(\s*`\$\{ATTENDANCE_BASE\}\/my\/punch-adjustments`/);
assert.match(
  types,
  /requestedPunchTimeUtc\??:\s*string/,
  "preview/create 的共享 payload 必须携带 requestedPunchTimeUtc",
);
assert.match(
  source,
  /sanitizePayload\(\{ \.\.\.payload, reason: payload\.reason\.trim\(\) \}\)/,
  "preview/create 必须原样发送 payload 内的 requestedPunchTimeUtc",
);

console.log("attendance-punch-adjustment-api.test.ts: ok");
