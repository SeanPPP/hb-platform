import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const currentDirectory = dirname(fileURLToPath(import.meta.url));
const homeSource = readFileSync(
  resolve(currentDirectory, "../../../app/(shell)/home.tsx"),
  "utf8",
);

assert.match(
  homeSource,
  /isLocationLookupEnabled\(access\)/,
  "首页应从现有 access 能力派生货位查询开关",
);
assert.match(
  homeSource,
  /useProducts\(productQuery, locationLookupEnabled\)/,
  "首页应把权限范围传给商品查询 key，而不是发给后端",
);
assert.match(
  homeSource,
  /locationSearchPlaceholder/,
  "有权限的首页应使用货号、商品条码或货位提示文案",
);

console.log("home-location-lookup-source.test.ts: ok");
