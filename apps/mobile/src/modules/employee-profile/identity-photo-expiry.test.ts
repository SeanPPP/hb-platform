import assert from "node:assert/strict";
import * as expiryModule from "./identity-photo-expiry";
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

const shouldRefreshAfterLoadError = (
  expiryModule as typeof expiryModule & {
    shouldRefreshIdentityPhotoAfterLoadError?: (
      imageUrl: string | undefined,
      lastAttemptedUrl: string
    ) => boolean;
  }
).shouldRefreshIdentityPhotoAfterLoadError;
assert.equal(
  typeof shouldRefreshAfterLoadError,
  "function",
  "图片加载失败必须通过按 URL 限流的刷新 helper"
);
assert.equal(shouldRefreshAfterLoadError!(undefined, ""), false);
assert.equal(shouldRefreshAfterLoadError!("https://cdn/pending?sig=one", ""), true);
assert.equal(
  shouldRefreshAfterLoadError!("https://cdn/pending?sig=one", "https://cdn/pending?sig=one"),
  false,
  "同一失败签名 URL 只允许自动刷新一次，避免 onError 循环"
);
assert.equal(
  shouldRefreshAfterLoadError!("https://cdn/pending?sig=two", "https://cdn/pending?sig=one"),
  true,
  "签名 URL 更新后必须允许再次受控刷新"
);

console.log("identity-photo-expiry.test.ts: ok");
