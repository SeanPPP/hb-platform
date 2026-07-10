import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}

const currentDir = dirname(fileURLToPath(import.meta.url));
const homeSource = readFileSync(resolve(currentDir, "../../../app/(tabs)/home.tsx"), "utf8");
const selectCategoryCallbackSource =
  homeSource.match(/const handleSelectCategoryFilter = useCallback\(\(categoryGUID\?: string\) => \{[\s\S]*?\n  \}, \[\]\);/)?.[0] ??
  "";

assertEqual(
  Boolean(selectCategoryCallbackSource),
  true,
  "首页商品筛选应有统一分类选择回调"
);

assertEqual(
  selectCategoryCallbackSource.includes("setFiltersVisible(false);"),
  true,
  "分类选择完成后应自动关闭筛选弹层"
);

assertEqual(
  (homeSource.match(/onPress=\{\(\) => handleSelectCategoryFilter\(undefined\)\}/g) ?? []).length,
  2,
  "All categories 和 All 都应清空分类并关闭筛选弹层"
);

assertEqual(
  homeSource.includes("onPress={() => handleSelectCategoryFilter(node.categoryGUID)}"),
  true,
  "点击具体分类应选择分类并关闭筛选弹层"
);

assertEqual(
  homeSource.includes("onPress={() => toggleCategoryExpanded(node.categoryGUID)}"),
  true,
  "分类展开按钮应保持只展开树节点"
);
