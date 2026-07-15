import assert from "node:assert/strict";
import { getIdentityPhotoRefetchDelay } from "./identity-photo-expiry";

const now = Date.parse("2026-07-15T00:00:00Z");
assert.equal(
  getIdentityPhotoRefetchDelay("2026-07-15T00:05:00Z", now),
  270_000,
  "私有 URL 应在到期前 30 秒刷新"
);
assert.equal(
  getIdentityPhotoRefetchDelay("2026-07-15T00:00:20Z", now),
  1_000,
  "已进入安全窗口时应尽快刷新且避免零间隔循环"
);
assert.equal(getIdentityPhotoRefetchDelay(undefined, now), false, "没有私有 URL 到期时间时不应轮询");
assert.equal(getIdentityPhotoRefetchDelay("invalid", now), false, "非法到期时间不应触发紧密轮询");

console.log("identity-photo-expiry.test.ts: ok");
