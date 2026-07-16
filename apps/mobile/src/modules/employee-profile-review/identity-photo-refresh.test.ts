import assert from "node:assert/strict";
import {
  createIdentityPhotoErrorRefetchGuard,
  getIdentityPhotoRefreshDelay,
} from "./identity-photo-refresh";

const now = Date.parse("2026-07-16T10:00:00.000Z");
assert.equal(
  getIdentityPhotoRefreshDelay("2026-07-16T10:05:00.000Z", now, 15_000),
  285_000
);
assert.equal(
  getIdentityPhotoRefreshDelay("2026-07-16T10:00:00.000Z", now, 15_000),
  0
);
assert.equal(getIdentityPhotoRefreshDelay("invalid", now, 15_000), null);
assert.equal(getIdentityPhotoRefreshDelay(undefined, now, 15_000), null);

const guard = createIdentityPhotoErrorRefetchGuard();
assert.equal(guard.shouldRefetch("https://signed.example/photo-a"), true);
assert.equal(guard.shouldRefetch("https://signed.example/photo-a"), false);
assert.equal(guard.shouldRefetch("https://signed.example/photo-b"), true);
assert.equal(guard.shouldRefetch(""), false);
