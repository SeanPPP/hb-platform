import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * 商品查询页离线能力的源码契约：保证离线门禁、降级顺序与只读门禁不会在后续改动中被悄悄移除。
 */
const currentDir = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(resolve(currentDir, "../../../app/(shell)/product-query.tsx"), "utf8");

function indexOfOrFail(needle: string, message: string): number {
  const index = source.indexOf(needle);
  assert.notEqual(index, -1, message);
  return index;
}

// 资格门禁：离线模式必须同时受 offlineEligible 约束。
assert.match(
  source,
  /const offlineEligible = isOfflineProductQueryEligible\(\{[\s\S]*?sessionKind,[\s\S]*?hasStoredDeviceSession: hasStoredDeviceSession\(deviceSession\),[\s\S]*?\}\);/,
  "离线资格必须由 sessionKind 与本地设备会话共同决定",
);
assert.match(source, /const offlineMode = offlineEligible && connectivity\.offline;/, "离线模式必须受资格门禁约束");

// handleLookup：在线失败时先判定网络不可用，再进入离线并回退本地查询。
const lookupStart = indexOfOrFail("const handleLookup = useCallback(", "商品查询入口必须存在");
const lookupSource = source.slice(lookupStart, source.indexOf("const wasOfflineRef = useRef(false);", lookupStart));
assert.ok(
  lookupSource.indexOf("isNetworkUnavailableError(error)") < lookupSource.indexOf("enterOfflineMode(nextKeyword)"),
  "必须先判定网络不可用再进入离线模式",
);
assert.ok(
  lookupSource.indexOf("enterOfflineMode(nextKeyword)") < lookupSource.indexOf(".lookup(selectedStoreCode, nextKeyword)", lookupSource.indexOf("enterOfflineMode(nextKeyword)")),
  "进入离线后必须用同一关键字改走本地快照查询",
);
assert.match(lookupSource, /if \(offlineModeRef\.current\) \{[\s\S]*?\.lookup\(selectedStoreCode, nextKeyword\)/, "离线态查询必须直接读本地快照");

// processLoadedDetail：离线早退必须位于自动价评估与仓库价对账之前。
const processStart = indexOfOrFail("const processLoadedDetail = useCallback(", "详情后处理必须存在");
const processSource = source.slice(processStart, source.indexOf("processLoadedDetailRef.current = processLoadedDetail;", processStart));
const offlineReturn = processSource.indexOf("if (offlineModeRef.current) {");
assert.notEqual(offlineReturn, -1, "详情后处理必须有离线早退");
assert.ok(offlineReturn < processSource.indexOf("getWarehousePriceSyncApplicability("), "离线早退必须在仓库价对账之前");
assert.ok(offlineReturn < processSource.indexOf("maybeHandleAutoPricing(targetDetail"), "离线早退必须在自动价评估之前");
assert.doesNotMatch(processSource.slice(offlineReturn, processSource.indexOf("const applicability")), /evaluateAutoPricing|syncWarehousePrice/, "离线分支不得调用服务器");

// loadDetail：离线态不得请求 fast-detail。
const loadDetailStart = indexOfOrFail("const loadDetail = useCallback(", "详情加载必须存在");
const loadDetailSource = source.slice(loadDetailStart, source.indexOf("const discardStaleDetailForStoreChange", loadDetailStart));
assert.ok(
  loadDetailSource.indexOf("if (offlineModeRef.current) {") < loadDetailSource.indexOf("await getProductFastDetail("),
  "离线态详情必须在请求 fast-detail 之前从本地快照返回",
);

// loadProductCodes：分页码表断网时必须降级到本地快照，而不是只弹错误。
const codesStart = indexOfOrFail("const loadProductCodes = useCallback(", "码表加载必须存在");
const codesSource = source.slice(codesStart, source.indexOf("const loadDetail = useCallback(", codesStart));
const codesOfflineFallback = codesSource.indexOf("isNetworkUnavailableError(error)");
assert.notEqual(codesOfflineFallback, -1, "码表加载必须识别网络不可用错误");
assert.ok(
  codesOfflineFallback < codesSource.indexOf('t("messages.codesLoadFailed")'),
  "离线降级必须先于错误提示",
);
assert.match(
  codesSource,
  /isNetworkUnavailableError\(error\)\) \{[\s\S]*?enterOfflineMode\(\);[\s\S]*?\.getDetail\(storeCode, sourceDetail\.productCode\)/,
  "码表断网后必须进入离线模式并从本地快照补齐",
);
// 只能补码表：商品级字段与分店价必须留用实时值，否则屏显实时价而标签打快照价。
assert.match(
  codesSource,
  /const merged: ProductDetail = \{\s*\.\.\.sourceDetail,[\s\S]*?setCodes: offlineDetail\.setCodes/,
  "码表降级必须以实时详情为基底，只覆盖套码/多码",
);
assert.match(
  codesSource,
  /setDetail\(applyOffline\);[\s\S]*?setInitialDetail\(/,
  "码表降级必须写屏，否则列表停在空表且没有加载更多",
);

// 离线态错误提示必须区分「本店没有快照」与「读快照失败」。
assert.match(
  source,
  /class OfflineCatalogUnavailableError extends Error/,
  "必须有独立的无快照错误类型",
);
assert.match(
  lookupSource,
  /if \(!activeMeta\) \{\s*throw new OfflineCatalogUnavailableError\(\);/,
  "离线查询前必须先确认本店有快照，空结果才不会被误报成未找到商品",
);
assert.match(
  lookupSource,
  /if \(error instanceof OfflineCatalogUnavailableError\) \{\s*[\s\S]*?message = t\("offline\.catalogMissing"\);/,
  "只有确实缺快照才改写成「尚未下载离线数据」",
);

// 只读门禁：四个编辑卡片与底部保存栏。
assert.match(source, /<StorePriceStrategyCard[\s\S]*?readOnly=\{offlineMode\}/, "分店价卡片必须传 readOnly");
assert.match(source, /<StoreClearancePriceCard[\s\S]*?readOnly=\{offlineMode\}/, "清货价卡片必须传 readOnly");
assert.match(source, /<SetCodeCompactSection[\s\S]*?readOnly=\{offlineMode\}/, "套码列表必须传 readOnly");
assert.match(source, /<MultiCodeCompactList[\s\S]*?readOnly=\{offlineMode\}/, "多码列表必须传 readOnly");
assert.match(source, /visible=\{dirtyCount > 0 && !scannerInputBlocked && !offlineMode\}/, "离线时不得出现未保存底栏");
// 创建商品有两个入口：搜索面板上的按钮与查无结果时的横条，离线态都必须关掉。
assert.match(
  source,
  /onCreateProduct=\{[\s\S]{0,160}?access\.canCreateStoreProducts && detail && !offlineMode/,
  "离线时搜索面板不得给出创建商品入口",
);
assert.match(
  source,
  /\{access\.canCreateStoreProducts && !detail && !offlineMode \? \(/,
  "离线时查无结果横条不得显示创建商品",
);
assert.match(source, /onPressProductType=\{\s*offlineMode \? undefined : \(\) => setProductTypeDialogVisible\(true\)\s*\}/, "离线时商品类型徽标必须只读");

// 离线横幅必须显示数据更新时间；恢复在线必须立即退出并重跑查询。
assert.match(source, /\{offlineMode \? \(\s*<OfflineModeBanner/, "离线态必须渲染横幅");
assert.match(source, /useOfflineReconnectProbe\(\{\s*enabled: offlineMode && isFocused && appActive,/, "离线态必须启用快速重连探测");
assert.match(source, /setSnackbarMessage\(t\("offline\.backOnline"\)\);[\s\S]*?void handleLookup\(pendingKeyword, "refresh"\);/, "恢复在线后必须提示并重跑离线期间的查询");

console.log("product-query-offline-source.test.ts: ok");
