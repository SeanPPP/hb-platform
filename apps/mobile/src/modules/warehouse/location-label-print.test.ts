import assert from "node:assert/strict";
import {
  buildWarehouseLocationLabelPayload,
  normalizeLocationMiddlePackageQuantity,
} from "./location-label-print";
import type { WarehouseLocationDetail } from "./types";

function location(products: WarehouseLocationDetail["products"]): WarehouseLocationDetail {
  return {
    locationGuid: "loc-1",
    locationCode: "A-00-00-01",
    locationBarcode: "5544492778828",
    locationType: 1,
    status: 1,
    products,
  };
}

function run() {
  assert.equal(normalizeLocationMiddlePackageQuantity(null), 1, "空中包数必须按 1 打印");
  assert.equal(normalizeLocationMiddlePackageQuantity(undefined), 1, "缺失中包数必须按 1 打印");
  assert.equal(normalizeLocationMiddlePackageQuantity(0), 1, "0 中包数必须按 1 打印");
  assert.equal(normalizeLocationMiddlePackageQuantity(12), 12, "正数中包数必须保留原值");

  const emptyPayload = buildWarehouseLocationLabelPayload(location([]));
  assert.equal(emptyPayload.itemNumber, null, "空货位没有商品货号");
  assert.equal(emptyPayload.productName, null, "空货位没有产品描述");
  assert.equal(emptyPayload.middlePackageQuantity, 1, "空货位标签中包数按 1 打印");

  const productPayload = buildWarehouseLocationLabelPayload(location([
    {
      productCode: "P-001",
      itemNumber: "HB313-129",
      productName: "3D TOYS",
      middlePackageQuantity: 0,
    },
  ]));
  assert.equal(productPayload.itemNumber, "HB313-129", "货位标签必须带商品货号");
  assert.equal(productPayload.productName, "3D TOYS", "货位标签必须带产品描述");
  assert.equal(productPayload.middlePackageQuantity, 1, "商品中包数为 0 时必须按 1 打印");

  console.log("location-label-print.test.ts: ok");
}

run();
