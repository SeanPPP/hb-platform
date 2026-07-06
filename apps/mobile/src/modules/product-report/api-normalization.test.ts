import assert from "node:assert/strict";
import { normalizeProductBranchRows, normalizeProductPage } from "./api";

const productPage = normalizeProductPage({
  data: [
    {
      productCode: "P1",
      itemNumber: "HB001",
      productName: "商品一",
      quantity: 2,
      quantityLY: 3,
      salesAmount: 40,
      salesAmountLY: 60,
      orderCount: 1,
      orderCountLY: 2,
    },
  ],
  total: 1,
  pageIndex: 1,
  pageSize: 20,
});

assert.equal(productPage.rows[0]?.quantity, 2);
assert.equal(productPage.rows[0]?.compareQuantity, 3);
assert.equal(productPage.rows[0]?.salesAmount, 40);
assert.equal(productPage.rows[0]?.compareSalesAmount, 60);
assert.equal(productPage.rows[0]?.orderCount, 1);
assert.equal(productPage.rows[0]?.compareOrderCount, 2);

const productBranchRows = normalizeProductBranchRows([
  {
    branchCode: "S1",
    branchName: "分店一",
    quantity: 2,
    compareQuantity: 3,
    salesAmount: 40,
    compareSalesAmount: 60,
    averageUnitPrice: 20,
    compareAverageUnitPrice: 20,
  },
]);

assert.equal(productBranchRows[0]?.quantity, 2);
assert.equal(productBranchRows[0]?.compareQuantity, 3);
assert.equal(productBranchRows[0]?.salesAmount, 40);
assert.equal(productBranchRows[0]?.compareSalesAmount, 60);
assert.equal(productBranchRows[0]?.averageUnitPrice, 20);
assert.equal(productBranchRows[0]?.compareAverageUnitPrice, 20);
