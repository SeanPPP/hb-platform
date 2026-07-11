import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const currentDir = dirname(fileURLToPath(import.meta.url));
const searchPanelSource = readFileSync(
  resolve(currentDir, "../../components/product-maintenance/SearchPanel.tsx"),
  "utf8"
);
const productQuerySource = readFileSync(
  resolve(currentDir, "../../../app/(tabs)/product-query.tsx"),
  "utf8"
);

const searchbarSource = searchPanelSource.match(/<Searchbar[\s\S]*?\/>/)?.[0] ?? "";
const searchPanelUsageSource = productQuerySource.match(/<SearchPanel[\s\S]*?\/>/)?.[0] ?? "";

assert.ok(searchbarSource, "搜索面板应包含 Searchbar");
assert.ok(
  searchbarSource.includes("onFocus={onFocus}"),
  "SearchPanel 内部 Searchbar 必须转发 onFocus"
);
assert.ok(
  searchbarSource.includes("onBlur={onBlur}"),
  "SearchPanel 内部 Searchbar 必须转发 onBlur"
);
assert.ok(searchPanelUsageSource, "商品查询页应渲染 SearchPanel");
assert.ok(
  searchPanelUsageSource.includes("onFocus={pauseHiddenScannerFocus}"),
  "商品查询页的 SearchPanel 必须在聚焦时暂停隐藏扫码输入框抢焦点"
);
assert.ok(
  searchPanelUsageSource.includes("onBlur={resumeHiddenScannerFocusLater}"),
  "商品查询页的 SearchPanel 必须在失焦后延迟恢复隐藏扫码输入框焦点"
);

console.log("ok");
