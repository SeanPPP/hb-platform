import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { resolveHomeProductColumns } from "./home-layout";

assert.equal(resolveHomeProductColumns(360), 2, "Zebra TC26（360dp）每行两张商品卡");
assert.equal(resolveHomeProductColumns(390), 2, "阈值宽度按窄屏处理");
assert.equal(resolveHomeProductColumns(391), 3, "超过窄屏阈值保持三列");
assert.equal(resolveHomeProductColumns(820), 3, "平板保持三列");

const currentDirectory = dirname(fileURLToPath(import.meta.url));
const homeSource = readFileSync(resolve(currentDirectory, "../../../app/(shell)/home.tsx"), "utf8");
assert.match(
  homeSource,
  /key=\{`product-grid-\$\{productColumns\}`\}[\s\S]*?numColumns=\{productColumns\}/,
  "列数随窗口宽度变化时 FlatList 必须随列数换 key 重建，否则运行时改 numColumns 会报错",
);

console.log("home-layout.test.ts: ok");
