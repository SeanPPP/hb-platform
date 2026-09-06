import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const currentDir = dirname(fileURLToPath(import.meta.url));
const productQuerySource = readFileSync(
  resolve(currentDir, "../../../app/(shell)/product-query.tsx"),
  "utf8"
);

const presentOperationSource = productQuerySource.match(
  /const presentHqSyncOperation = useCallback\([\s\S]*?\n  \);\n\n  useEffect/
)?.[0] ?? "";
assert.ok(presentOperationSource, "商品查询页必须集中展示 HQ 同步结果");
assert.ok(
  presentOperationSource.indexOf("if (!operation)") < presentOperationSource.indexOf(".succeed(mutation"),
  "没有 operation 的普通保存反馈必须先做只读当前性校验，不能推进 HQ 成功序号"
);
assert.match(
  presentOperationSource,
  /\.isCurrent\(mutation\)/,
  "没有 operation 的普通保存反馈必须校验 mutation 当前性"
);

const busySource = productQuerySource.match(
  /const isProductQueryBusy = useCallback\([\s\S]*?\n  \);\n  const invoiceReturnState/
)?.[0] ?? "";
for (const mutationState of [
  "saving",
  "savingItemId",
  "savingClearance",
  "productTypeSaving",
  "createProductSaving",
  "hqSyncRetrying",
]) {
  assert.match(
    busySource,
    new RegExp(mutationState),
    `商品维护 mutation 进行时必须阻止切店/查询：${mutationState}`
  );
}

assert.match(
  productQuerySource,
  /createProductDetailRequestCoordinator/,
  "详情请求必须有独立范围协调器"
);
assert.match(
  productQuerySource,
  /detailRequestCoordinatorRef\.current\?\.isCurrent\(request\)/,
  "旧详情或分页响应返回时必须复核当前范围"
);
assert.match(
  productQuerySource,
  /updateMultiCode\(target\.uuid, \{[\s\S]*?purchasePrice: target\.purchasePrice \?\? null,[\s\S]*?retailPrice: retailPrice \?\? null/,
  "历史多码零售价编辑必须以 UUID 调用 updateMultiCode 并保留当前进货价"
);
assert.match(
  productQuerySource,
  /updateMultiCode\(multiTarget\.uuid, \{[\s\S]*?barcode: trimmed,[\s\S]*?purchasePrice: multiTarget\.purchasePrice \?\? null/,
  "历史多码条码编辑必须以 UUID 调用 updateMultiCode 并保留当前进货价"
);
assert.match(
  productQuerySource,
  /retailPrice: setBackedRetailPrice!/,
  "套装条码编辑必须携带当前零售价"
);
assert.match(
  productQuerySource,
  /const retailPrice = codeType === "set" \? parseDecimalInput\(retailPriceInput\) : null;[\s\S]*?retailPrice,[\s\S]*?isActive: true,/,
  "新增套装必须收集并携带零售价"
);
assert.match(
  productQuerySource,
  /const created = await createSetCode\([\s\S]*?presentHqSyncOperation\(\n        mutation,\n        created\.hqSync/,
  "新增套装成功后必须呈现 HQ 同步状态"
);

const createSubmitSource = productQuerySource.match(
  /const handleCreateProductSubmit = useCallback\([\s\S]*?\n\n  const persistStorePrice/
)?.[0] ?? "";
assert.match(
  createSubmitSource,
  /if \(createdProductCode && selectedStoreCode\) \{[\s\S]*?setDetail\(null\);[\s\S]*?setInitialDetail\(null\);[\s\S]*?await loadDetail\(createdProductCode\);/,
  "创建新商品后的详情回读前必须清空旧商品，避免回读失败时误编辑旧详情"
);
assert.match(
  createSubmitSource,
  /catch \(error\) \{\n        setSnackbarMessage\(getErrorMessage\(error, "createProduct\.messages\.refreshFailedAfterCreate"\)\);/,
  "有分店时创建后的详情回读失败必须明确提示，不能因已有 HQ operation 而静默"
);

assert.match(
  productQuerySource,
  /const loadedDetailStoreCodeRef = useRef<string \| null>\(null\);[\s\S]*?const activeDetailProductCodeRef = useRef<string \| null>\(null\);[\s\S]*?useEffect\(\(\) => \{[\s\S]*?isProductMaintenanceStoreScopeCurrent\(loadedDetailStoreCodeRef\.current, selectedStoreCode\)[\s\S]*?discardStaleDetailForStoreChange\(activeProductCode\);/,
  "其他 tab 改变全局分店后，商品页必须使旧分店详情失效"
);
assert.match(
  productQuerySource,
  /const ensureCurrentDetailStoreScope = useCallback\([\s\S]*?discardStaleDetailForStoreChange\(sourceDetail\.productCode\);[\s\S]*?void loadDetail\(sourceDetail\.productCode, selectedStoreCode\);/,
  "分店不一致的 mutation 必须清理旧详情并按当前分店回读"
);
assert.match(
  productQuerySource,
  /if \(!ensureCurrentDetailStoreScope\(sourceDetail, sourceDetail\.storePrice\.storeCode\)\) \{\n        return null;/,
  "门店价格保存前必须确认 UUID 属于当前分店"
);
assert.match(
  productQuerySource,
  /if \(!ensureCurrentDetailStoreScope\(detail, target\.storeCode\)\) \{\n        return;/,
  "多码保存前必须确认目标行属于当前分店"
);

console.log("product-query-hq-sync-source.test.ts: ok");
