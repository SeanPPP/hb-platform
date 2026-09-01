import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(directory, "RevenueReportScreen.tsx"), "utf8");
const hubSource = readFileSync(join(directory, "reports-hub-screen.tsx"), "utf8");
const screenStart = source.indexOf("export function RevenueReportScreen(");
const modalStart = source.indexOf("      <Portal>", screenStart);

assert.ok(screenStart >= 0, "必须能够定位营业额报告屏幕");
assert.ok(modalStart > screenStart, "营业额表格滚动区必须位于详情弹窗之前");

assert.match(
  hubSource,
  /if \(value !== "revenue" && value !== "product"\) return;[\s\S]*?if \(value === tab\) return;[\s\S]*?markReportNavigationStart\(value\);[\s\S]*?setTab\(value\)/,
  "报告页签只有确实切换时才记录对应报表的点击起点",
);
assert.match(
  hubSource,
  /useFocusEffect\([\s\S]*?getPendingReportNavigationToken\(tab\)[\s\S]*?refetchQueries\([\s\S]*?getReportRefreshQueryOptions\(tab\)[\s\S]*?return \(\) => \{[\s\S]*?discardReportNavigationStart\(tab, navigationActionId\)/,
  "返回常驻报告页时必须刷新活动页签，并只在焦点会话结束时清理仍未认领的 marker",
);
assert.match(
  hubSource,
  /const \[focusNavigationActionId, setFocusNavigationActionId\] = useState<number \| null>\(null\);[\s\S]*?setFocusNavigationActionId\(navigationActionId\);[\s\S]*?<RevenueReportScreen[\s\S]*?reportNavigationActionId=\{focusNavigationActionId\}/,
  "Hub 必须把当前焦点 actionId 显式传给活动营业额屏",
);
const focusEffectStart = hubSource.indexOf("  useFocusEffect(");
const freshnessTimeStart = hubSource.indexOf("  const freshnessTime", focusEffectStart);
assert.ok(focusEffectStart >= 0 && freshnessTimeStart > focusEffectStart, "必须能够隔离 Reports 焦点会话");
assert.doesNotMatch(
  hubSource.slice(focusEffectStart, freshnessTimeStart),
  /\.finally\([\s\S]*?discardReportNavigationStart/,
  "复用在途请求时不得在 refetch Promise 结束后直接丢弃本次 grouped marker",
);
assert.match(
  source,
  /revenueLoadTimer\.start\(cacheState, "revenue"\)/,
  "营业额首次查询必须继承营业额导航起点",
);
assert.match(
  source,
  /reportNavigationActionId\?: number \| null;[\s\S]*?reportNavigationActionId = null[\s\S]*?const summaryQueryEnabled = isRevenuePeriodAvailable\(period, dateBounds\);/,
  "营业额屏必须接收当前焦点 actionId，并显式判定主查询是否可执行",
);
assert.match(
  source,
  /useLayoutEffect\(\(\) => \{\s*if \(reportNavigationActionId === null \|\| summaryQueryEnabled\) return;\s*discardReportNavigationStart\("revenue", reportNavigationActionId\);[\s\S]*?\}, \[reportNavigationActionId, summaryQueryEnabled\]\);/,
  "营业额主查询不可执行时必须按 expected actionId 清理；有效或在途查询不得提前清理",
);
assert.match(
  source,
  /const summaryQuery = useQuery\(\{[\s\S]*?enabled: summaryQueryEnabled,[\s\S]*?\.\.\.REPORT_QUERY_OPTIONS/,
  "营业额无效周期必须与 marker 清理使用同一个主查询可执行条件",
);

const screenSource = source.slice(screenStart, modalStart);
const flatLists = screenSource.match(/<FlatList\b/g) ?? [];

assert.equal(flatLists.length, 1, "营业额报告主体必须只使用一个 FlatList 承担纵向滚动");

const flatListStart = screenSource.indexOf("<FlatList");

assert.ok(flatListStart >= 0, "必须能够定位营业额报告的主 FlatList");

const flatListSource = screenSource.slice(flatListStart);

assert.match(
  flatListSource,
  /ListHeaderComponent=\{\s*<View style=\{styles\.listHeader\}>[\s\S]*?<View style=\{styles\.filtersCard\}>[\s\S]*?<SegmentedButtons[\s\S]*?<MonthDatePickerField[\s\S]*?style=\{styles\.toolbar\}[\s\S]*?getPeriodLabel\(period\)/,
  "紧凑日期筛选必须完整放入 ListHeaderComponent",
);
assert.doesNotMatch(
  flatListSource,
  /ListHeaderComponent=\{\(\) =>/,
  "ListHeaderComponent 必须使用稳定元素，避免刷新重渲染时重置日期选择器状态",
);
assert.match(
  flatListSource,
  /data=\{summaryListItems\}/,
  "完整分店行和表头必须由稳定的 summaryListItems 驱动",
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
  /item\.type === "table-header"[\s\S]*?renderSummaryTableHeader\(\)/,
  "表头哨兵条目必须渲染营业额表格列标题",
);
assert.match(
  flatListSource,
  /reports\.sections\.branchRanking[\s\S]*?ListFooterComponent[\s\S]*?<RevenueSummaryCard[\s\S]*?reports\.sections\.summary/,
  "全店汇总必须移到排行之后，为窄屏首屏保留至少六行排行空间",
);
assert.match(flatListSource, /viewabilityConfig=\{summaryViewabilityConfig\}/, "首屏数据必须接入稳定可见性计时");
assert.match(flatListSource, /onViewableItemsChanged=\{onSummaryViewableItemsChanged\}/, "首条可见分店必须完成性能计时");
assert.match(
  source,
  /fetchExecutiveHourlyTraffic\(detailParams, \{ signal \}\)/,
  "营业额分时下钻必须透传取消信号",
);
assert.match(
  source,
  /fetchBranchDailyPerformance\(detailParams, \{ signal \}\)/,
  "营业额逐日下钻必须透传取消信号",
);
assert.match(
  screenSource,
  /useQuery<RevenueDetailSnapshot<DetailRow>>\(\{[\s\S]*?fetchExecutiveHourlyTraffic[\s\S]*?fetchBranchDailyPerformance/,
  "分时和逐日下钻必须以带完整性元数据的快照作为查询结果",
);
assert.match(
  source,
  /recordReportLoadPerformance\(\s*detailLoadTypeRef\.current === "hourly" \? "revenue-hourly" : "revenue-daily",\s*measurement,?\s*\)/,
  "分时和逐日下钻必须分别记录首行可见耗时",
);
assert.match(
  source,
  /onViewableItemsChanged=\{onDetailViewableItemsChanged\}/,
  "营业额下钻必须以真实可见行完成性能计时",
);
assert.match(source, /InteractionManager\.runAfterInteractions/, "营业额下钻必须等待 Paper Modal 展示动画完成");
assert.match(source, /detailLoadGate\.setPresentationReady\(true\)/, "弹窗展示完成必须成为独立计时门禁");
assert.match(
  source,
  /const isSameDetailQuery = detailLoadQueryKeyRef\.current === detailQueryKey;[\s\S]*?restorePhysicalState: isSameDetailQuery[\s\S]*?firstRowVisible: detailPhysicalRowVisibleRef\.current[\s\S]*?presentationReady: detailPhysicalPresentationReadyRef\.current/,
  "同 key 重试必须从独立物理 refs 恢复首行可见与弹窗展示状态",
);
assert.match(
  source,
  /hasUsableSuccessfulReportCache\([\s\S]*?queryClient\.getQueryState\(summaryQueryKey\)\?\.status[\s\S]*?cachedSummary[\s\S]*?cachedSummary\.isComplete && cachedSummary\.rows\.length > 0/,
  "营业额主排行只有 success 状态的完整非空缓存才能按 warm 预算统计",
);
assert.match(
  source,
  /hasUsableSuccessfulReportCache\([\s\S]*?queryClient\.getQueryState\(detailQueryKey\)\?\.status[\s\S]*?cachedDetailSnapshot[\s\S]*?cachedSnapshot\.isComplete && cachedSnapshot\.rows\.length > 0/,
  "营业额下钻必须排除 error、空缓存和不完整快照的 warm 分类",
);
assert.match(
  source,
  /detailPhysicalRowVisibleRef\.current = hasVisibleDetailRow;[\s\S]*?detailLoadGate\.setFirstRowVisible\(hasVisibleDetailRow\)/,
  "营业额下钻必须独立保存当前物理可见状态",
);
assert.match(
  source,
  /detailPhysicalPresentationReadyRef\.current = true;[\s\S]*?detailLoadGate\.setPresentationReady\(true\)/,
  "营业额下钻必须独立保存当前物理展示状态",
);
assert.match(
  screenSource,
  /detailQuery\.data\?\.isComplete \? detailQuery\.data\.rows : \[\]/,
  "分时和逐日下钻不得直接渲染未完整的响应行",
);
assert.match(
  screenSource,
  /!detailQuery\.data\.isComplete[\s\S]*?detailLoadGate\.fail\(\)/,
  "缺少完整性元数据或有界追数耗尽时不得计入首条数据性能",
);
assert.match(
  screenSource,
  /detailRequestGenerationRef\.current !== requestGeneration[\s\S]*?AbortError/,
  "分时和逐日旧请求晚返回必须按请求代次丢弃，不能覆盖当前弹窗会话",
);
assert.match(
  source.slice(modalStart),
  /detailPending[\s\S]*?reports\.states\.statisticsIncomplete[\s\S]*?detailQuery\.refetch\(\)/,
  "分时和逐日补算耗尽时必须明确提示并允许人工重试",
);
assert.match(flatListSource, /<MonthDatePickerField\s+compact/, "营业额日期控件必须使用紧凑模式，为排行保留首屏空间");
assert.doesNotMatch(flatListSource, /supplierPage|pageSize|pagination|slice\(/i, "营业额分店排行不得分页或截断");
assert.match(flatListSource, /<RefreshControl[\s\S]*?onRefresh=\{refresh\}/, "主列表必须保留下拉刷新");
assert.match(flatListSource, /summaryQuery\.isLoading/, "主列表必须保留加载状态");
assert.match(flatListSource, /summaryQuery\.isError/, "主列表必须保留错误状态");
assert.match(flatListSource, /visibleRows\.length === 0/, "筛选后的主列表必须保留空状态");
assert.match(
  screenSource,
  /queryFn: async \(\{ signal \}\)[\s\S]*?fetchExecutiveBranchPerformance\(queryParams, \{ signal \}\)/,
  "营业额查询必须把 React Query 取消信号传入同一轮补算轮询",
);
assert.match(
  screenSource,
  /const isSameQuery = revenueLoadQueryKeyRef\.current === summaryQueryKey;[\s\S]*?if \(!isSameQuery\) \{\s*revenueBranchVisibleRef\.current = false;/,
  "同 key 刷新必须保留当前真实可见行，新 key 才能重置可见性",
);
assert.match(
  screenSource,
  /revenueBranchVisibleRef\.current = hasVisibleBranch;/,
  "FlatList 可见性回调必须同时记录行进入和离开首屏",
);
assert.match(
  screenSource,
  /revenueRequestGenerationRef\.current !== requestGeneration[\s\S]*?AbortError/,
  "旧请求晚返回必须通过请求代次丢弃，不能污染新会话",
);
assert.match(
  screenSource,
  /!summaryQuery\.data\.isComplete[\s\S]*?revenueLoadTimer\.fail\(\)/,
  "部分或耗尽快照不得完成首条完整业务数据计时",
);
assert.match(
  screenSource,
  /lastCompleteSummaryRef[\s\S]*?summaryQuery\.data\.isComplete[\s\S]*?lastCompleteSummaryRef\.current\s*=/,
  "营业额排行必须只保存已确认完整的快照",
);
assert.match(
  screenSource,
  /summaryQuery\.data\?\.isComplete[\s\S]*?summaryQuery\.data\.rows[\s\S]*?lastCompleteSummaryRef\.current\?\.queryKey === summaryQueryKey/,
  "补算未完成时只能保留同查询上一次完整排行，不能展示本次部分快照",
);
assert.doesNotMatch(
  screenSource,
  /summaryQuery\.data\?\.rows \?\? \[\]/,
  "排行不得直接渲染未经过完整性门禁的响应行",
);
assert.match(
  flatListSource,
  /summaryPollingExhausted[\s\S]*?reports\.states\.statisticsIncomplete[\s\S]*?icon="refresh"[\s\S]*?summaryQuery\.refetch\(\)/,
  "有界轮询耗尽后必须明确停止自动刷新文案，并在排行标题提供重试",
);
assert.match(
  screenSource,
  /summaryQuery\.dataUpdatedAt/,
  "同 key 且结构相同的 refetch 也必须触发一次完成检查",
);
assert.doesNotMatch(
  screenSource,
  /revenueEmptyRetryCount|retryNumber \* 650/,
  "补算退避必须收进单次 API 会话，不能由组件重启成 warm 查询",
);
assert.match(flatListSource, /setBranchPickerVisible\(true\)/, "25 到 30 家分店必须提供快速选择入口");
assert.match(flatListSource, /searchBranchPlaceholder/, "分店排行必须提供名称或编号搜索");
assert.match(
  screenSource,
  /onPress=\{\(\) => setDrilldown\(/,
  "营业额数据行必须保留分店下钻入口",
);
assert.match(screenSource, /formatOrdinal\(rowIndex\)/, "分店排行必须显示连续行号");
assert.match(source.slice(modalStart), /<Modal[\s\S]*?<FlatList[\s\S]*?renderDetailRow/, "分时或每日明细必须在下一层可滚动抽屉中显示");
assert.match(source.slice(modalStart), /data=\{branchPickerRows\}[\s\S]*?initialNumToRender=\{14\}/, "分店选择器必须连续滚动全部分店而不分页");
assert.match(source.slice(modalStart), /branchPickerSearch[\s\S]*?searchBranchPlaceholder/, "分店选择抽屉必须支持从 25 到 30 家中快速搜索");
assert.match(source, /numberOfLines=\{2\}[\s\S]*?getWeekdayLabel/, "每日明细必须完整显示日期与星期");
assert.match(source, /tableRow:\s*\{[\s\S]*?minHeight: 54/, "排行行高必须保持 54px 高密度且仍大于 44px 触控下限");

console.log("revenue-report-screen-contract.test.ts: ok");
