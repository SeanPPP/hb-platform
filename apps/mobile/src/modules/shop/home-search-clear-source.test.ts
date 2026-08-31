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
  /const searchReturnPageRef = useRef<number \| null>\(null\);/,
  "首页应记录首次进入搜索前的页码",
);
assert.match(
  homeSource,
  /const handleSearchInputChange = useCallback\([\s\S]*?if \(!value\.trim\(\)\)[\s\S]*?type: "clear"[\s\S]*?\n  \},/,
  "手动删空或点击 Searchbar 清空按钮时都应退出已提交搜索",
);
assert.match(
  homeSource,
  /onChangeText=\{handleSearchInputChange\}/,
  "Searchbar 必须使用统一的关键词输入处理器",
);
assert.doesNotMatch(
  homeSource,
  /onClearIconPress=/,
  "Searchbar 已会触发 onChangeText，不能再绑定清空回调导致重复恢复",
);

const productContextReset = homeSource.match(
  /useEffect\(\(\) => \{[\s\S]*?searchReturnPageRef\.current = null;[\s\S]*?setPageNumber\(1\);[\s\S]*?\}, \[selectedCategoryGUID, selectedGrade, selectedStoreCode\]\);/,
);
assert.ok(
  productContextReset,
  "门店、分类或 Grade 改变后应让旧返回页码失效并回到第一页",
);

console.log("home-search-clear-source.test.ts: ok");
