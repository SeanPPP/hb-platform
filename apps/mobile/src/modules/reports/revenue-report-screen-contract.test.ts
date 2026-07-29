import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(directory, "RevenueReportScreen.tsx"), "utf8");
const screenStart = source.indexOf("export function RevenueReportScreen(");
const modalStart = source.indexOf("      <Portal>", screenStart);

assert.ok(screenStart >= 0, "必须能够定位营业额报告屏幕");
assert.ok(modalStart > screenStart, "营业额表格滚动区必须位于详情弹窗之前");

const screenSource = source.slice(screenStart, modalStart);
const flatLists = screenSource.match(/<FlatList\b/g) ?? [];

assert.equal(flatLists.length, 1, "营业额报告主体必须只使用一个 FlatList 承担纵向滚动");

const flatListStart = screenSource.indexOf("<FlatList");

assert.ok(flatListStart >= 0, "必须能够定位营业额报告的主 FlatList");

const flatListSource = screenSource.slice(flatListStart);

assert.match(
  flatListSource,
  /ListHeaderComponent=\{\s*<View style=\{styles\.filters\}>[\s\S]*?getPeriodLabel\(period\)[\s\S]*?<SegmentedButtons[\s\S]*?<MonthDatePickerField[\s\S]*?style=\{styles\.toolbar\}/,
  "日期范围、日周月、日期选择器和快捷筛选必须放入 ListHeaderComponent",
);
assert.doesNotMatch(
  flatListSource,
  /ListHeaderComponent=\{\(\) =>/,
  "ListHeaderComponent 必须使用稳定元素，避免刷新重渲染时重置日期选择器状态",
);
assert.match(
  flatListSource,
  /data=\{\[\{ type: "table-header" \}, \.\.\.rows\]\}/,
  "表格列标题必须作为 FlatList 的首个数据条目",
);
assert.match(
  flatListSource,
  /stickyHeaderIndices=\{\[1\]\}/,
  "ListHeaderComponent 之后的表格列标题必须吸顶",
);
assert.match(
  flatListSource,
  /contentInsetAdjustmentBehavior="automatic"/,
  "主 FlatList 必须自动适配安全区内容边距",
);
assert.match(
  flatListSource,
  /"type" in item[\s\S]*?renderSummaryTableHeader\(\)/,
  "表头哨兵条目必须渲染营业额表格列标题",
);
assert.match(flatListSource, /<RefreshControl[\s\S]*?onRefresh=\{refresh\}/, "主列表必须保留下拉刷新");
assert.match(flatListSource, /summaryQuery\.isLoading/, "主列表必须保留加载状态");
assert.match(flatListSource, /summaryQuery\.isError/, "主列表必须保留错误状态");
assert.match(flatListSource, /rows\.length === 0/, "主列表必须保留空状态");
assert.match(
  screenSource,
  /onPress=\{\(\) => setDrilldown\(/,
  "营业额数据行必须保留分店下钻入口",
);

console.log("revenue-report-screen-contract.test.ts: ok");
