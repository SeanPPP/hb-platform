import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(directory, "api.ts"), "utf8");
const types = readFileSync(join(directory, "types.ts"), "utf8");

assert.match(types, /interface AttendanceQrResolveResult\s*\{[\s\S]*storeCode:\s*string;[\s\S]*deviceCode:\s*string;[\s\S]*expiresAtUtc:\s*string;[\s\S]*storeName\?:\s*string;/,
  "resolve 结果必须暴露后端验证的门店、设备、有效期和可选门店名");
assert.match(source, /export async function resolveAttendanceQr\(\s*qrToken:\s*string[\s\S]*apiClient\.post\(\s*`\$\{ATTENDANCE_BASE\}\/qr\/resolve`\s*,\s*\{\s*qrToken\s*\}/,
  "resolve 必须通过现有 apiClient POST 原始 token");
assert.match(source, /import \{[\s\S]{0,200}normalizeAttendanceQrResolveResult[\s\S]{0,200}\} from "@\/modules\/attendance\/attendance-qr";/,
  "resolve 必须复用已执行测试的 fail-closed normalizer");
assert.match(source, /return normalizeAttendanceQrResolveResult\(response\.data\)/,
  "resolve 请求必须通过 fail-closed normalizer 后才能返回页面");
assert.doesNotMatch(source, /resolveAttendanceQr[\s\S]{0,500}(useQuery|queryClient|retry)/,
  "30 秒 resolve 结果不得缓存或业务重试");
assert.match(source, /return normalizeAttendancePunchMutationResult\(\s*response\.data,\s*normalizePunch\(/,
  "punch mutation 成功响应必须经过严格 normalizer，不能直接返回历史宽松结果");

console.log("attendance-api.test.ts: ok");
