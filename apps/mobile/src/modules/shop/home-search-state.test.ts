import assert from "node:assert/strict";
import * as homeFilters from "./home-filters";

interface HomeSearchPageState {
  keyword: string;
  pageNumber: number;
  returnPageNumber: number | null;
}

type HomeSearchPageAction =
  | { type: "apply"; input: string }
  | { type: "clear" };

type ResolveHomeSearchPageState = (
  state: HomeSearchPageState,
  action: HomeSearchPageAction,
) => HomeSearchPageState;

const resolveHomeSearchPageState = (
  homeFilters as typeof homeFilters & {
    resolveHomeSearchPageState?: ResolveHomeSearchPageState;
  }
).resolveHomeSearchPageState;

assert.equal(
  typeof resolveHomeSearchPageState,
  "function",
  "首页搜索必须提供可测试的页码状态转换",
);

const applyFromPageFour = resolveHomeSearchPageState!(
  { keyword: "", pageNumber: 4, returnPageNumber: null },
  { type: "apply", input: "  JM  " },
);
assert.deepEqual(
  applyFromPageFour,
  { keyword: "JM", pageNumber: 1, returnPageNumber: 4 },
  "首次提交关键词应记录搜索前页码并从搜索结果第一页开始",
);

const refineSearch = resolveHomeSearchPageState!(
  { ...applyFromPageFour, pageNumber: 2 },
  { type: "apply", input: "JM-00" },
);
assert.deepEqual(
  refineSearch,
  { keyword: "JM-00", pageNumber: 1, returnPageNumber: 4 },
  "修改已提交关键词不得覆盖最初记录的返回页码",
);

assert.deepEqual(
  resolveHomeSearchPageState!(
    { ...refineSearch, pageNumber: 2 },
    { type: "apply", input: "JM-00" },
  ),
  { ...refineSearch, pageNumber: 2 },
  "重复提交相同关键词不得把当前搜索结果页重置到第一页",
);

assert.deepEqual(
  resolveHomeSearchPageState!(refineSearch, { type: "clear" }),
  { keyword: "", pageNumber: 4, returnPageNumber: null },
  "清空已提交关键词应恢复搜索前页码",
);

assert.deepEqual(
  resolveHomeSearchPageState!(
    { keyword: "", pageNumber: 3, returnPageNumber: null },
    { type: "clear" },
  ),
  { keyword: "", pageNumber: 3, returnPageNumber: null },
  "只清空未提交的输入内容不得改变当前页码",
);

assert.deepEqual(
  resolveHomeSearchPageState!(
    { keyword: "JM", pageNumber: 2, returnPageNumber: 5 },
    { type: "apply", input: "   " },
  ),
  { keyword: "", pageNumber: 5, returnPageNumber: null },
  "提交空白内容应与清空按钮使用相同的恢复行为",
);

console.log("home-search-state.test.ts: ok");
