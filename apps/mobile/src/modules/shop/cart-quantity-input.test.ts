import assert from "node:assert/strict";
import {
  canSubmitCartQuantityEdit,
  parseCartQuantityInput,
  shouldSubmitCartQuantityUpdate,
} from "./cart-quantity-input";

function run() {
  assert.equal(parseCartQuantityInput(""), null, "空值不能更新订货数量");
  assert.equal(parseCartQuantityInput("abc"), null, "非数字不能更新订货数量");
  assert.equal(parseCartQuantityInput("1.5"), null, "小数不能更新订货数量");
  assert.equal(parseCartQuantityInput("-1"), null, "负数不能更新订货数量");
  assert.equal(parseCartQuantityInput("0"), 0, "0 表示清空该商品订货数量");
  assert.equal(parseCartQuantityInput("42"), 42, "正整数可直接覆盖订货数量");
  assert.equal(
    parseCartQuantityInput(String(Number.MAX_SAFE_INTEGER + 1)),
    null,
    "超过安全整数范围不能更新订货数量"
  );
  assert.equal(
    shouldSubmitCartQuantityUpdate(0, 0),
    false,
    "0 到 0 不应提交后端更新，避免写入零数量明细"
  );
  assert.equal(
    shouldSubmitCartQuantityUpdate(0, 6),
    true,
    "0 到正整数需要提交后端新增订货数量"
  );
  assert.equal(
    shouldSubmitCartQuantityUpdate(6, 0),
    true,
    "正整数到 0 需要提交后端删除订货数量"
  );
  assert.equal(
    canSubmitCartQuantityEdit({
      currentStoreCode: "1024",
      editorStoreCode: " 1024 ",
      isPending: false,
    }),
    true,
    "同一门店且无 pending 时允许提交编辑数量"
  );
  assert.equal(
    canSubmitCartQuantityEdit({
      currentStoreCode: "2048",
      editorStoreCode: "1024",
      isPending: false,
    }),
    false,
    "门店切换后不能把旧商品数量提交到新门店"
  );
  assert.equal(
    canSubmitCartQuantityEdit({
      currentStoreCode: "1024",
      editorStoreCode: null,
      isPending: false,
    }),
    false,
    "编辑器没有记录门店时不能提交订货数量"
  );
  assert.equal(
    canSubmitCartQuantityEdit({
      currentStoreCode: "1024",
      editorStoreCode: "1024",
      isPending: true,
    }),
    false,
    "已有数量更新 pending 时不能重复提交编辑数量"
  );

  console.log("cart-quantity-input.test.ts: ok");
}

run();
