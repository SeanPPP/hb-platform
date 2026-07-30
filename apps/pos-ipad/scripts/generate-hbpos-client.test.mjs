import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";

const appRoot = new URL("../", import.meta.url);
const snapshotPath = new URL("openapi/hbpos.openapi.json", appRoot);
const generatedPath = new URL("src/generated/hbpos/schema.d.ts", appRoot);

assert.equal(existsSync(snapshotPath), true, "缺少由 Hbpos.Api 测试宿主导出的 OpenAPI 快照");
assert.equal(existsSync(generatedPath), true, "缺少由 openapi-typescript 生成的 DTO 类型");

const document = JSON.parse(readFileSync(snapshotPath, "utf8"));
for (const route of [
  "/api/v1/devices/register",
  "/api/v1/devices/verify",
  "/api/v1/app-updates/pos-ipad",
  "/api/v1/orders/sync",
  "/api/v1/catalog/sellable-items",
  "/api/v1/catalog/sellable-items/page",
  "/api/v1/catalog/sync-plan",
  "/api/v1/catalog/delta/page",
  "/api/v1/cashiers/barcode-login",
  "/api/v1/square/checkouts",
  "/api/v1/linkly/cloud-backend/transactions",
  "/api/v1/vouchers/lock",
  "/api/v1/installments",
  "/api/v1/operation-audits",
  "/api/v1/operation-audits/{eventId}",
  "/api/v1/operation-audits/batch"
]) {
  assert.ok(document.paths?.[route], `OpenAPI 快照缺少 POS 路由：${route}`);
}

assert.ok(document.components?.schemas?.DeviceRegisterRequest?.properties?.deviceSystem);
assert.ok(document.components?.schemas?.DeviceVerifyRequest?.properties?.deviceSystem);
assert.ok(document.components?.schemas?.CatalogSyncPageResponse?.properties?.catalogVersion);
assert.ok(document.components?.schemas?.CatalogSyncPageResponse?.properties?.pageChecksum);
assert.ok(document.components?.schemas?.CatalogSyncPlanResponse?.properties?.downloadLeaseId);
assert.ok(document.components?.schemas?.CatalogSyncPlanResponse?.properties?.deltaOperationCount);
assert.deepEqual(
  new Set(
    document.paths?.["/api/v1/catalog/sellable-items/page"]?.get?.parameters
      ?.map(parameter => parameter.name)
  ).has("downloadLeaseId"),
  true,
  "full page 必须暴露可选下载租约参数"
);
assert.deepEqual(
  new Set(
    document.paths?.["/api/v1/catalog/delta/page"]?.get?.parameters
      ?.map(parameter => parameter.name)
  ).has("downloadLeaseId"),
  true,
  "delta page 必须暴露可选下载租约参数"
);
assert.equal(
  document.components?.schemas?.LinklyCloudBackendSessionResponse?.properties?.cardTransaction?.$ref,
  "#/components/schemas/LinklyCloudBackendCardTransactionDto"
);
assert.deepEqual(
  Object.keys(document.components?.schemas?.LinklyCloudBackendCardTransactionDto?.properties ?? {}),
  [
    "txnRef",
    "rfn",
    "authCode",
    "cardType",
    "maskedCardNumber",
    "merchantId",
    "responseCode",
    "responseText",
    "stan",
    "bankDateTime",
    "amountCents"
  ]
);
assert.deepEqual(
  Object.keys(document.components?.schemas?.OperationAuditReadRecordDto?.properties ?? {}),
  [
    "eventId",
    "occurredAtIso",
    "operationType",
    "outcome",
    "cashierName",
    "storeCode",
    "deviceCode",
    "orderGuid",
    "receiptNumber",
    "correlationId",
    "safeMessage",
    "paymentAmountCents",
    "productCount",
    "primaryProduct",
    "uploadState",
    "items"
  ],
  "远程审计合同只能暴露白名单安全投影"
);

const generated = readFileSync(generatedPath, "utf8");
assert.match(generated, /AUTO-GENERATED/);
assert.match(generated, /LinklyCloudBackendCardTransactionDto:/);
assert.match(generated, /OperationAuditReadListDto:/);
assert.match(generated, /OperationAuditReadRecordDto:/);
assert.match(
  generated,
  /cardTransaction\?: components\["schemas"\]\["LinklyCloudBackendCardTransactionDto"\]/
);

console.log("hbpos OpenAPI snapshot and generated DTOs: ok");
