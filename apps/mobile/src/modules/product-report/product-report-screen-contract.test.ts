import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(directory, "product-report-screen.tsx"), "utf8");
const hubSource = readFileSync(join(directory, "../reports/reports-hub-screen.tsx"), "utf8");

assert.match(
  source,
  /getCashierEnabledStoreCodes\(storeOptionsQuery\.data \?\? \[\]\)/,
  "商品报告必须以 Store.IsActive 分店选项生成同一份收银启用白名单",
);
assert.match(
  source,
  /getCashierScopedBranchCodes\(cashierEnabledStoreCodes, selectedStoreCode\)/,
  "商品报告的单店筛选必须受收银启用白名单约束",
);
assert.equal(
  (source.match(/buildProductReportDateQuery\(activeRange, cashierEnabledStoreCodes\)/g) ?? []).length,
  2,
  "供应商与商品分店下钻都必须显式限定在全部收银启用分店",
);
assert.match(
  source,
  /storeOptionsQuery\.isSuccess[\s\S]*?branchCodes\.length > 0[\s\S]*?enabled: Boolean\(queryParams\)/,
  "白名单未完成、为空或单店选择已失效时不得执行商品报告主查询",
);
assert.match(
  source,
  /const cashierStoreScopeVersion = storeOptionsQuery\.dataUpdatedAt;/,
  "商品报告必须以白名单权威回读时间作为查询 revision",
);
assert.equal(
  (source.match(/cashierStoreScopeVersion/g) ?? []).length >= 7,
  true,
  "白名单 revision 必须覆盖变量定义、三个主查询和两个分店下钻查询",
);
assert.match(
  source,
  /const mainReportStatisticsPending =\s*storeOptionsQuery\.isFetching/,
  "白名单后台重验期间必须隐藏商品报告旧缓存",
);

function assertSourceOrder(scope: string, markers: string[], message: string) {
  let previousIndex = -1;
  markers.forEach((marker) => {
    const markerIndex = scope.indexOf(marker, previousIndex + 1);
    assert.ok(markerIndex > previousIndex, `${message}：${marker}`);
    previousIndex = markerIndex;
  });
}

const pickerStart = source.indexOf("function StorePickerModal(");
const pickerEnd = source.indexOf("function BranchDrilldownModal(", pickerStart);

assert.ok(pickerStart >= 0, "商品报告必须保留分店选择弹层");
assert.ok(pickerEnd > pickerStart, "必须能够隔离分店选择弹层源码");

assert.match(
  hubSource,
  /if \(value !== "revenue" && value !== "product"\) return;[\s\S]*?if \(value === tab\) return;[\s\S]*?markReportNavigationStart\(value\);[\s\S]*?setTab\(value\)/,
  "商品页签只有确实切换时才记录商品报告点击起点",
);
assert.match(
  hubSource,
  /const \[focusNavigationActionId, setFocusNavigationActionId\] = useState<number \| null>\(null\);[\s\S]*?<ProductReportScreen[\s\S]*?reportNavigationActionId=\{focusNavigationActionId\}/,
  "Hub 必须把当前焦点 actionId 显式传给活动商品屏",
);
assert.match(
  source,
  /productLoadTimer\.start\(cacheState, "product"\)/,
  "商品首次查询必须继承商品导航起点",
);
assert.match(
  source,
  /reportNavigationActionId\?: number \| null;[\s\S]*?reportNavigationActionId = null/,
  "商品屏必须接收当前焦点 actionId",
);
assert.match(
  source,
  /if \(reportNavigationActionId === null\) return;[\s\S]*?if \(dateRangeValid && storeOptionsQuery\.isPending\) return;[\s\S]*?!dateRangeValid[\s\S]*?storeOptionsQuery\.isError[\s\S]*?storeOptionsQuery\.isSuccess && cashierEnabledStoreCodes\.length === 0[\s\S]*?discardReportNavigationStart\("product", reportNavigationActionId\)/,
  "商品报告只在日期无效或白名单已失败、已确认为空时清理 marker，白名单加载中不得提前清理",
);
assert.match(
  source,
  /storeOptionsQuery\.isFetching[\s\S]*?!storeOptionsQuery\.isSuccess[\s\S]*?!selectedStoreCode[\s\S]*?branchCodes\.length > 0[\s\S]*?setSelectedStoreCode\(undefined\)[\s\S]*?setDrilldown\(null\)/,
  "已停用的陈旧单店选择必须先阻断请求，再清除选择和下钻",
);
assert.equal(
  (source.match(/void storeOptionsQuery\.refetch\(\);/g) ?? []).length >= 3,
  true,
  "刷新、错误重试和未完成重试都必须先重验收银启用白名单",
);
assert.doesNotMatch(
  source,
  /totalRevenueQuery\.refetch\(\)|supplierQuery\.refetch\(\)|productQuery\.refetch\(\)/,
  "白名单重验与业务请求不得并发；成功回读后必须由 scope revision 自动触发业务查询",
);
assert.match(
  source,
  /storeOptionsQuery\.isSuccess && cashierEnabledStoreCodes\.length === 0[\s\S]*?reports\.states\.noCashierEnabledStores/,
  "零启用收银分店必须显示明确空态",
);

const pickerSource = source.slice(pickerStart, pickerEnd);

assert.match(pickerSource, /<ScrollView/);
assert.match(pickerSource, /nestedScrollEnabled/);
assert.match(pickerSource, /showsVerticalScrollIndicator/);
assert.match(pickerSource, /keyboardShouldPersistTaps="handled"/);
assert.match(pickerSource, /\{labelAll\}/);
assert.match(pickerSource, /options\.map/);
assert.match(pickerSource, /style=\{styles\.storeModalList\}/);
assert.match(pickerSource, /contentContainerStyle=\{styles\.storeModalListContent\}/);

assert.match(source, /function formatNullableMoney\(value: number \| null\)/);
assert.match(source, /function formatGrossMarginRate\(value: number \| null, costPendingLabel: string\)/);
assert.match(source, /\(value \* 100\)\.toFixed\(1\)/, "毛利率 0.4 必须显示为 40.0%，不能显示成 0.4%");
assert.match(source, /productReport\.states\.costPending/);
assert.match(source, /styles\.rowNumberColumn/);
assert.match(source, /rowNumber: \(supplierPage - 1\) \* SUPPLIER_PAGE_SIZE \+ index \+ 1/);
assert.match(source, /rowNumber: \(productPage - 1\) \* PRODUCT_PAGE_SIZE \+ index \+ 1/);
assert.match(source, /grossProfit/);
assert.match(source, /grossMarginRate/);
assert.match(source, /showsHorizontalScrollIndicator>/, "宽表必须显示水平滚动提示，让毛利字段可发现");
assert.match(source, /reports\.metrics\.current[\s\S]*?productReport\.metrics\.compare/, "双行数值必须明确标注本期与同期");
assert.match(source, /accessibilityRole="button"[\s\S]*?productReport\.drilldown\.product/, "商品行必须明确提供可点击的分店下钻入口");
assert.match(source, /new ReportLoadPerformanceTimer\(\)/, "商品报告必须计量首数据耗时");
assert.match(
  source,
  /totalRevenueQuery\.data\?\.isComplete === true[\s\S]*?supplierQuery\.data\?\.isComplete === true[\s\S]*?productQuery\.data\?\.isComplete === true/,
  "商品报告必须等待总额、供应商和商品三块 Fresh 数据后再标记归一化完成",
);
assert.match(
  source,
  /getProductReportCacheVersionState\(\[[\s\S]*?totalRevenueQuery\.data[\s\S]*?supplierQuery\.data[\s\S]*?productQuery\.data[\s\S]*?\]\)/,
  "商品报告必须比较三块并发结果的 cacheVersion",
);
assert.match(
  source,
  /getProductReportCacheVersionSyncDecision\([\s\S]*?decision !== "refetch"[\s\S]*?refetchMainReport\(\)/,
  "版本不一致时必须受控地协调重取三块数据",
);
assert.match(
  source,
  /const refetchMainReport = useCallback\(\(\) => Promise\.all\(\[[\s\S]*?totalRevenueQueryKey[\s\S]*?supplierQueryKey[\s\S]*?productQueryKey/,
  "cacheVersion 协调重取必须覆盖总额、供应商和商品三条精确查询",
);
assert.match(
  source,
  /mainReportCacheVersionState === "mismatch"[\s\S]*?mainReportVersionSyncExhausted[\s\S]*?mainReportStatisticsIncomplete/,
  "版本不一致期间不得展示混合批次，重试耗尽后必须进入未完成态",
);
assert.match(
  source,
  /mainReportStatisticsPending[\s\S]*?mainReportCacheVersionMismatch[\s\S]*?mainReportQueriesFetching/,
  "版本不一致的协调请求在途时必须继续隐藏旧混合批次",
);
const totalRevenueStateStart = source.indexOf("const totalRevenueStatisticsPending =");
const productQueryStart = source.indexOf("const productQuery =", totalRevenueStateStart);
const mainReportPendingStart = source.indexOf("const mainReportStatisticsPending =", productQueryStart);
const mainReportIncompleteStart = source.indexOf("const mainReportStatisticsIncomplete =", mainReportPendingStart);
const productRowStart = source.indexOf("const renderProductRow =");
const reportContentStart = source.indexOf("{!dateRangeValid ? (", productRowStart);
assert.ok(totalRevenueStateStart >= 0 && productQueryStart > totalRevenueStateStart, "必须定义总额统计状态");
assert.ok(
  mainReportPendingStart > productQueryStart && mainReportIncompleteStart > mainReportPendingStart,
  "必须能够隔离主报表全页 Loading 判定",
);
assert.ok(productRowStart >= 0 && reportContentStart > productRowStart, "必须能定位报告内容根分支");
const totalRevenueStateSource = source.slice(totalRevenueStateStart, productQueryStart);
const mainReportPendingSource = source.slice(mainReportPendingStart, mainReportIncompleteStart);
const productRowSource = source.slice(productRowStart, reportContentStart);
assert.match(
  totalRevenueStateSource,
  /!totalRevenueQuery\.data\.isComplete[\s\S]*?!totalRevenueQuery\.data\.pollingExhausted/,
  "总额统计在有界轮询中必须明确处于加载态",
);
assert.match(
  totalRevenueStateSource,
  /const totalRevenueStatisticsIncomplete[\s\S]*?!totalRevenueQuery\.data\.isComplete[\s\S]*?totalRevenueQuery\.data\.pollingExhausted/,
  "有界轮询耗尽的部分总额必须明确为未完成状态",
);
assert.match(
  source.slice(reportContentStart),
  /mainReportStatisticsPending \? \([\s\S]*?<LoadingState label=\{t\("reports\.states\.refreshingStatistics"\)\}/,
  "供应商或商品主表处于非 Fresh 追数时，页面不得进入行级渲染",
);
assert.match(
  source.slice(reportContentStart),
  /mainReportRequestError \? \([\s\S]*?<ErrorState[\s\S]*?resetMainReportVersionSync\(\);[\s\S]*?void storeOptionsQuery\.refetch\(\);/,
  "任一首屏请求失败时必须在汇总卡前显示统一重试，并先重验收银启用范围",
);
assert.match(
  source.slice(reportContentStart),
  /mainReportStatisticsIncomplete \? \([\s\S]*?<ErrorState[\s\S]*?reports\.states\.statisticsIncomplete[\s\S]*?resetMainReportVersionSync\(\);[\s\S]*?void storeOptionsQuery\.refetch\(\);/,
  "任何主表轮询耗尽为非 Fresh 时，页面必须显示先重验白名单的重试，而非空表",
);
assert.match(
  source,
  /supplierQuery\.data\?\.data \?\? \[\][\s\S]*?productQuery\.data\?\.data\.rows \?\? \[\]/,
  "主表业务行必须从 Fresh 快照 data 解包，不能把根级空 data 当业务空结果",
);
assert.match(
  source,
  /isDrilldownLoading[\s\S]*?\.data\.isComplete[\s\S]*?isDrilldownStatisticsIncomplete[\s\S]*?\.data\.pollingExhausted/,
  "供应商和商品下钻必须识别非 Fresh 追数与耗尽状态",
);
assert.match(
  source,
  /supplierRows=\{supplierBranchQuery\.data\?\.data \?\? \[\]\}[\s\S]*?productRows=\{productBranchQuery\.data\?\.data \?\? \[\]\}/,
  "下钻行数据必须从统计快照 data 解包，非 Fresh 不得渲染为空业务表",
);
assert.doesNotMatch(
  productRowSource,
  /totalRevenueStatisticsPending|totalRevenueStatisticsIncomplete/,
  "总额统计状态不得侵入商品图片或行级渲染",
);
assert.match(
  source,
  /queryFn: async \(\{ signal \}\) => \{[\s\S]*?fetchProductReportTotalRevenue\(queryParams!, \{ signal \}\)/,
  "商品页总营业额请求必须接收并透传 React Query 取消信号",
);
assert.match(
  source,
  /import \{[^}]*keepPreviousData[^}]*\} from "@tanstack\/react-query"/,
  "商品分页、搜索或供应商筛选切换 query key 时必须保留上一份商品上下文",
);
assert.match(
  source,
  /const productQuery = useQuery\(\{[\s\S]*?placeholderData: keepPreviousData[\s\S]*?\}\);/,
  "page1 切换 page2 时商品查询必须使用上一页快照作占位，不能触发全页冷加载",
);
assert.match(
  source,
  /const productSectionLoading =[\s\S]*?productQuery\.isPlaceholderData[\s\S]*?productSectionLoading \? \([\s\S]*?<LoadingState label=\{t\("productReport\.states\.loading"\)\}/,
  "商品 query key 切换只能在商品区域显示局部加载",
);
assert.match(
  source,
  /productSectionLoading \? \([\s\S]*?styles\.productSummaryLoading[\s\S]*?productReport\.states\.loading[\s\S]*?\) : \([\s\S]*?<ProductPageSummaryCard/,
  "placeholder 期间商品汇总卡必须显示局部 loading，不能把旧行按新页码、筛选或搜索语境重新标注",
);
assert.doesNotMatch(
  mainReportPendingSource,
  /isPlaceholderData/,
  "上一页商品占位数据不得让汇总卡和供应商表进入全页 Loading",
);
assert.match(
  mainReportPendingSource,
  /productQuery\.isLoading/,
  "没有上一页占位数据的首次冷加载仍必须使用全页 Loading 门禁",
);
assert.match(
  source,
  /queryFn: async \(\{ signal \}\) => \{[\s\S]*?fetchSupplierReportRows\(kind, queryParams!, 1000, \{ signal \}\)/,
  "供应商首屏请求必须接收并透传 React Query 取消信号",
);
assert.match(
  source,
  /fetchProductReportProductRows\([\s\S]*?productSearch,[\s\S]*?\{ signal \},[\s\S]*?\)/,
  "商品首屏请求必须接收并透传 React Query 取消信号",
);
assert.match(
  source,
  /fetchSupplierBranchBreakdown\([\s\S]*?supplier\.supplierCode,[\s\S]*?\{ signal \},[\s\S]*?\)/,
  "供应商下钻请求必须接收并透传 React Query 取消信号",
);
assert.match(
  source,
  /fetchProductBranchBreakdown\([\s\S]*?product\.productCode,[\s\S]*?\{ signal \},[\s\S]*?\)/,
  "商品下钻请求必须接收并透传 React Query 取消信号",
);
assert.match(source, /recordReportLoadPerformance\("product", measurement\)/, "商品报告必须记录 2 秒预算结果");
assert.match(
  source,
  /recordReportLoadPerformance\(\s*drilldownLoadKindRef\.current === "supplier" \? "supplier-branches" : "product-branches",\s*measurement,?\s*\)/,
  "供应商和商品分店下钻必须分别记录首行可见耗时",
);
assert.match(
  source,
  /onFirstDataVisibilityChange=\{updateDrilldownFirstDataVisibility\}/,
  "分店下钻弹窗必须把第一条业务行可见事件回传给计时器",
);
assert.match(
  source,
  /InteractionManager\.runAfterInteractions[\s\S]*?firstRowRef\.current\?\.measureInWindow/,
  "分店下钻必须等弹窗动画结束并复测第一行与真实视口的交集",
);
assert.match(
  source,
  /const isSameDrilldownQuery = drilldownLoadQueryKeyRef\.current === queryKey;[\s\S]*?restorePhysicalState: isSameDrilldownQuery[\s\S]*?firstRowVisible: drilldownPhysicalRowVisibleRef\.current[\s\S]*?presentationReady: drilldownPhysicalPresentationReadyRef\.current/,
  "同 key 重试必须从独立物理 refs 恢复首行可见与弹窗展示状态",
);
assert.match(
  source,
  /hasUsableSuccessfulReportCache\(\s*queryClient\.getQueryState\(totalRevenueQueryKey\)\?\.status[\s\S]*?hasUsableSuccessfulReportCache\(\s*queryClient\.getQueryState\(supplierQueryKey\)\?\.status[\s\S]*?hasUsableSuccessfulReportCache\(\s*queryClient\.getQueryState\(productQueryKey\)\?\.status/,
  "商品主报表三个缓存都必须处于 success 状态才能按 warm 预算统计",
);
assert.match(
  source,
  /hasUsableSuccessfulReportCache\([\s\S]*?queryClient\.getQueryState\(supplierBranchQueryKey\)\?\.status[\s\S]*?cachedSupplierBranches/,
  "供应商下钻必须排除 error 缓存的 warm 分类",
);
assert.match(
  source,
  /hasUsableSuccessfulReportCache\([\s\S]*?queryClient\.getQueryState\(productBranchQueryKey\)\?\.status[\s\S]*?cachedProductBranches/,
  "商品下钻必须排除 error 缓存的 warm 分类",
);
assert.match(
  source,
  /drilldownPhysicalPresentationReadyRef\.current = true;[\s\S]*?drilldownPhysicalRowVisibleRef\.current = visible/,
  "商品下钻必须独立保存当前物理展示与可见状态",
);
assert.match(
  source,
  /const hasBusinessRows =[\s\S]*?supplierQuery\.data\?\.data\.length[\s\S]*?productQuery\.data\?\.data\.rows\.length[\s\S]*?productLoadTimer\.cancel\(\)/,
  "完整空商品与供应商结果必须取消 first-data 样本",
);
assert.doesNotMatch(
  source,
  /productSummaryRef/,
  "汇总卡可见不代表真实业务行可见，不能用于完成 report_first_data",
);
assert.match(
  source,
  /const firstSupplierReportRowRef = useRef<View>\(null\);[\s\S]*?const firstProductReportRowRef = useRef<View>\(null\);/,
  "商品主报表必须分别追踪第一条供应商行和第一条商品行",
);
assert.match(
  source,
  /\[firstSupplierReportRowRef, firstProductReportRowRef\]\.forEach\(\(rowRef\) => \{[\s\S]*?rowRef\.current\?\.measureInWindow[\s\S]*?x >= width[\s\S]*?x \+ measuredWidth <= 0[\s\S]*?y >= height[\s\S]*?y \+ measuredHeight <= 0[\s\S]*?firstProductReportDataVisibleRef\.current = true;[\s\S]*?completeProductLoad\(\)/,
  "汇总卡已可见但明细行仍在屏外时不得完成，只有真实供应商或商品行与视口相交后才能完成",
);
assert.match(
  source,
  /supplierPageRows\.map\(\(item, index\) => \([\s\S]*?ref=\{index === 0 \? firstSupplierReportRowRef : undefined\}[\s\S]*?onLayout=\{index === 0 \? scheduleProductDataVisibilityCheck : undefined\}/,
  "第一条供应商业务行布局或进入视口后必须触发真实可见性复测",
);
assert.match(
  source,
  /productRows\.map\(\(item, index\) => \([\s\S]*?ref=\{index === 0 \? firstProductReportRowRef : undefined\}[\s\S]*?onLayout=\{index === 0 \? scheduleProductDataVisibilityCheck : undefined\}/,
  "第一条商品业务行布局或进入视口后必须触发真实可见性复测",
);
assert.match(
  source,
  /const markProductDataVisible = useCallback\(\(\) => \{\s*if \(!productLoadActiveRef\.current\) return;/,
  "性能会话结束后滚动不得继续跨桥测量汇总卡",
);
assert.match(source, /onScroll=\{markProductDataVisible\}/, "滚动后必须重新核对首数据是否真实进入视口");
assert.doesNotMatch(source, /firstProductReportRowLaidOutRef/, "性能计时不得继续使用仅布局未可见的旧标记");
assert.match(source, /productReport\.sections\.pageSummary/, "商品页必须在明细前展示本页营业额、毛利额和毛利率汇总");
assert.match(
  source,
  /productTableRow:\s*\{[\s\S]*?gap: 3,[\s\S]*?paddingHorizontal: 4,[\s\S]*?\}/,
  "375px 窄屏必须完整容纳商品核心毛利列",
);
assert.match(
  source,
  /supplierTableRow:\s*\{[\s\S]*?gap: 3,[\s\S]*?paddingHorizontal: 4,[\s\S]*?\}/,
  "375px 窄屏必须完整容纳供应商核心毛利列",
);
assert.match(source, /rowNumberColumn:\s*\{\s*width: 24,/, "行号列不能挤占核心毛利字段空间");
assert.match(source, /productInfoColumn:\s*\{[\s\S]*?width: 80,/, "商品名称列需为毛利率保留首屏空间");
assert.match(source, /supplierNameColumn:\s*\{\s*width: 80,/, "供应商名称列需为毛利率保留首屏空间");
assert.match(
  source,
  /supplierMoneyColumn[\s\S]*?formatWholeMoney\(item\.revenue\)[\s\S]*?formatWholeMoney\(item\.compareRevenue\)/,
  "供应商高密度排行必须用整元金额，避免大额数值被下钻箭头截断",
);

const supplierHeaderStart = source.indexOf("function SupplierTableHeader(");
const productHeaderStart = source.indexOf("function ProductTableHeader(");
assert.ok(supplierHeaderStart >= 0 && productHeaderStart > supplierHeaderStart, "供应商表头必须存在");
const supplierHeader = source.slice(supplierHeaderStart, productHeaderStart);
assert.match(supplierHeader, /productReport\.metrics\.grossProfit/);
assert.match(supplierHeader, /productReport\.metrics\.grossMarginRate/);
assert.equal(
  (supplierHeader.match(/productReport\.metrics\.growthRate/g) ?? []).length,
  1,
  "供应商增长列只能保留一列"
);

const supplierRowStart = source.indexOf("const renderSupplierRow =");
const mainReportReturnStart = source.indexOf("\n  return (\n    <View style={styles.container}>", productRowStart);
const productHeaderEnd = source.indexOf("function LoadingState(", productHeaderStart);
const drilldownStart = source.indexOf("function BranchDrilldownModal(");
const supplierBranchRowStart = source.indexOf("function SupplierBranchRow(", drilldownStart);
const productBranchRowStart = source.indexOf("function ProductBranchRow(", supplierBranchRowStart);
const productBranchRowEnd = source.indexOf("const styles = StyleSheet.create(", productBranchRowStart);
assert.ok(supplierRowStart >= 0 && productRowStart > supplierRowStart, "必须能够隔离供应商与商品主表行");
assert.ok(mainReportReturnStart > productRowStart, "必须能够隔离商品主表行");
assert.ok(productHeaderEnd > productHeaderStart, "商品表头必须存在");
assert.ok(drilldownStart >= 0 && supplierBranchRowStart > drilldownStart, "分店下钻表头必须存在");
assert.ok(productBranchRowStart > supplierBranchRowStart, "供应商分店行必须存在");
assert.ok(productBranchRowEnd > productBranchRowStart, "商品分店行必须存在");

assertSourceOrder(
  supplierHeader,
  ["styles.supplierGrowthColumn", "styles.supplierShareColumn", "styles.supplierCountColumn", "productReport.metrics.aov", "styles.grossProfitColumn", "styles.grossMarginColumn"],
  "供应商主表毛利列必须位于所有经营字段之后",
);
assertSourceOrder(
  source.slice(supplierRowStart, productRowStart),
  ["renderGrowthCell", "styles.supplierShareColumn", "styles.supplierCountColumn", "item.averageTransaction", "renderGrossProfitCell", "renderGrossMarginCell"],
  "供应商主表行必须与表头保持相同列顺序",
);
assertSourceOrder(
  source.slice(productHeaderStart, productHeaderEnd),
  ["styles.productImageColumn", "styles.productCountColumn", "styles.productAverageColumn", "styles.productGrowthColumn", "styles.grossProfitColumn", "styles.grossMarginColumn"],
  "商品主表毛利列必须位于所有经营字段之后",
);
assertSourceOrder(
  source.slice(productRowStart, mainReportReturnStart),
  ["styles.productImageColumn", "styles.productCountColumn", "styles.productAverageColumn", "renderGrowthCell", "renderGrossProfitCell", "renderGrossMarginCell"],
  "商品主表行必须与表头保持相同列顺序",
);

const drilldownHeader = source.slice(drilldownStart, supplierBranchRowStart);
const productBranchHeaderStart = drilldownHeader.indexOf('{kind === "product" ? (', drilldownHeader.indexOf("styles.tableHeaderRow"));
const supplierBranchHeaderStart = drilldownHeader.indexOf(") : (", productBranchHeaderStart);
const drilldownHeaderEnd = drilldownHeader.indexOf(")}\n", supplierBranchHeaderStart);
assert.ok(productBranchHeaderStart >= 0 && supplierBranchHeaderStart > productBranchHeaderStart, "必须能够隔离商品分店表头");
assert.ok(drilldownHeaderEnd > supplierBranchHeaderStart, "必须能够隔离供应商分店表头");
assertSourceOrder(
  drilldownHeader.slice(productBranchHeaderStart, supplierBranchHeaderStart),
  ["styles.productBranchCountColumn", "styles.productBranchMoneyColumn", "styles.productBranchAverageColumn", "styles.productBranchGrowthColumn", "styles.grossProfitColumn", "styles.grossMarginColumn"],
  "商品分店表毛利列必须位于所有经营字段之后",
);
assertSourceOrder(
  drilldownHeader.slice(supplierBranchHeaderStart, drilldownHeaderEnd),
  ["productReport.metrics.growthRate", "productReport.metrics.orders", "productReport.metrics.aov", "styles.grossProfitColumn", "styles.grossMarginColumn"],
  "供应商分店表毛利列必须位于所有经营字段之后",
);
assertSourceOrder(
  source.slice(supplierBranchRowStart, productBranchRowStart),
  ["renderGrowthCell", "styles.countColumn", "row.averageTransaction", "renderGrossProfitCell", "renderGrossMarginCell"],
  "供应商分店行必须与表头保持相同列顺序",
);
assertSourceOrder(
  source.slice(productBranchRowStart, productBranchRowEnd),
  ["styles.productBranchAverageColumn", "renderGrowthCell", "renderGrossProfitCell", "renderGrossMarginCell"],
  "商品分店行必须与表头保持相同列顺序",
);

assert.match(source, /function FrozenHorizontalTable\(/, "宽表必须共用固定列横向滚动容器");
assert.match(
  source,
  /<Animated\.ScrollView[\s\S]*?horizontal[\s\S]*?Animated\.event\([\s\S]*?useNativeDriver: true/,
  "固定列必须由原生横向滚动事件驱动",
);
assert.match(source, /function FrozenLeadingColumns\([\s\S]*?translateX: scrollX/, "固定列必须反向补偿横向位移");
assert.match(
  source.slice(supplierRowStart, productRowStart),
  /<FrozenLeadingColumns[\s\S]*?styles\.rowNumberColumn[\s\S]*?styles\.supplierNameColumn[\s\S]*?<\/FrozenLeadingColumns>/,
  "供应商主表必须固定行号和供应商列",
);
assert.match(
  source.slice(productRowStart, mainReportReturnStart),
  /<FrozenLeadingColumns[\s\S]*?styles\.rowNumberColumn[\s\S]*?styles\.productInfoColumn[\s\S]*?<\/FrozenLeadingColumns>/,
  "商品主表必须固定行号和商品列",
);
assert.match(
  supplierHeader,
  /<FrozenLeadingColumns[\s\S]*?styles\.rowNumberColumn[\s\S]*?styles\.supplierNameColumn[\s\S]*?<\/FrozenLeadingColumns>/,
  "供应商表头必须与固定列对齐",
);
assert.match(
  source.slice(productHeaderStart, productHeaderEnd),
  /<FrozenLeadingColumns[\s\S]*?styles\.rowNumberColumn[\s\S]*?styles\.productInfoColumn[\s\S]*?<\/FrozenLeadingColumns>/,
  "商品表头必须与固定列对齐",
);
assert.match(
  drilldownHeader,
  /<FrozenLeadingColumns[\s\S]*?styles\.rowNumberColumn[\s\S]*?productBranchNameColumn[\s\S]*?branchColumn[\s\S]*?<\/FrozenLeadingColumns>/,
  "分店下钻表头必须按表型固定行号和分店列",
);
assert.match(
  source.slice(supplierBranchRowStart, productBranchRowStart),
  /<FrozenLeadingColumns[\s\S]*?styles\.rowNumberColumn[\s\S]*?styles\.branchColumn[\s\S]*?<\/FrozenLeadingColumns>/,
  "供应商分店表必须固定行号和分店列",
);
assert.match(
  source.slice(productBranchRowStart, productBranchRowEnd),
  /<FrozenLeadingColumns[\s\S]*?styles\.rowNumberColumn[\s\S]*?styles\.productBranchNameColumn[\s\S]*?<\/FrozenLeadingColumns>/,
  "商品分店表必须固定行号和分店列",
);

console.log("product-report-screen-contract.test.ts: ok");
