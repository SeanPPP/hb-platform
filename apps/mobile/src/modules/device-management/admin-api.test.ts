import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(directory, "admin-api.ts"), "utf8");
const sheetSource = readFileSync(join(directory, "admin-sheets.tsx"), "utf8");

assert.match(source, /const POS_ACTIVATION_PATH = "\/react\/v1\/device-activation-codes";/);
assert.match(source, /const MOBILE_ACTIVATION_PATH = "\/react\/v1\/mobile-device-activation-codes";/);
assert.match(source, /const EMERGENCY_LOGIN_PATH = "\/react\/v1\/emergency-login-grants";/);
assert.match(source, /apiClient\.put\(`\$\{DEVICE_REGISTRATION_PATH\}\/\$\{id\}`, \{[\s\S]*是否允许交易: payload\.allowTransactions/,
  "设备编辑必须保持 Web DTO 的交易许可字段");
assert.match(source, /apiClient\.get\(`\$\{MOBILE_ACTIVATION_PATH\}\/manageable-accounts`, \{ params: \{ storeCode \} \}\)/,
  "Mobile 激活码必须按门店读取可管理账号");
assert.match(source, /createMobileActivationCode = \(payload: MobileActivationCodeCreatePayload\) => createActivationCode\(MOBILE_ACTIVATION_PATH, payload\)/,
  "Mobile 创建必须走独立端点");
assert.match(source, /reason: payload\.reason\.trim\(\)/,
  "创建请求必须归一化原因，且不自行包装认证请求");
assert.match(source, /"设备状态"/, "设备详情必须识别真实 DTO 的设备状态字段");
assert.match(source, /"设备状态描述"/, "设备详情必须识别真实 DTO 的设备状态描述字段");
assert.match(source, /apiClient\.post\(`\$\{EMERGENCY_LOGIN_PATH\}\/\$\{encodeURIComponent\(grantId\)\}\/revoke`, \{ reason: reason\.trim\(\) \}\)/,
  "紧急授权撤销必须使用精确 grant ID 和原因");
assert.match(sheetSource, /setCreated\([\s\S]*?closeCreate\(\);\n\s*void load\(\);/,
  "激活码创建成功后必须先显示一次性结果，再独立回读列表");
assert.match(sheetSource, /const message = getSafeErrorMessage\(error, t, language, "deviceManagement:messages\.activationCreateFailed"\);[\s\S]*setCreateOutcomeUnknown[\s\S]*void load\(\);/,
  "创建结果未知时只能回读，不能自动重试创建");
assert.match(sheetSource, /loadGate\.current\.invalidate\(\); setGrants\(\[\]\); setStores\(\[\]\); setCreated\(null\);[\s\S]*?\}, \[canManageMobile, canManagePos, kind, userGuid\]\)/,
  "账号切换必须使旧请求失效并清除一次性激活码和旧授权列表");
assert.doesNotMatch(sheetSource, /(queryClient|useMutation|AsyncStorage|SecureStorage).*secret|secret.*(queryClient|useMutation|AsyncStorage|SecureStorage)/,
  "一次性凭证不能进入持久或 mutation 缓存");

console.log("admin-api.test.ts: ok");
