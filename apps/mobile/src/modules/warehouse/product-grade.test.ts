import assert from "node:assert/strict";
import { normalizeWarehouseProductGrade, toggleWarehouseProductGradeSelection } from "./product-grade";

function run() {
  assert.equal(toggleWarehouseProductGradeSelection("D", "D"), "", "再次点击当前等级应清空等级");
  assert.equal(toggleWarehouseProductGradeSelection("D", "A"), "A", "点击不同等级应切换到新等级");
  assert.equal(toggleWarehouseProductGradeSelection("", "B"), "B", "空等级点击等级应设置等级");
  assert.equal(toggleWarehouseProductGradeSelection(" d ", "d"), "", "等级比较应忽略大小写和空格");
  assert.equal(normalizeWarehouseProductGrade(null), "", "空等级应归一为空字符串");

  console.log("product-grade.test.ts: ok");
}

run();
