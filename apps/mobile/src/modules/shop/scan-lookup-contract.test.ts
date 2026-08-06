import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const currentDirectory = dirname(fileURLToPath(import.meta.url));
const apiSource = readFileSync(
  resolve(currentDirectory, "./api.ts"),
  "utf8",
);
const orderTypesSource = readFileSync(
  resolve(currentDirectory, "../orders/types.ts"),
  "utf8",
);

for (const matchType of [
  "barcode",
  "fallback",
  "productBarcode",
  "itemNumber",
  "productCode",
  "locationBarcode",
  "locationCode",
]) {
  assert.match(
    orderTypesSource,
    new RegExp(`\\"${matchType}\\"`),
    `扫码类型应兼容 ${matchType}`,
  );
}

assert.match(orderTypesSource, /interface StoreOrderScanLookupResult[\s\S]*matchType\?:/);
assert.match(apiSource, /normalizeStoreOrderScanLookupResult/);
assert.match(apiSource, /matchType: getStringValue\(payload\.matchType, payload\.MatchType\)/);
assert.match(
  apiSource,
  /return normalizeStoreOrderScanLookupResult\(response\.data, barcode\)/,
  "扫码 API 必须把后端 matchType 交给统一 normalizer",
);

console.log("scan-lookup-contract.test.ts: ok");
