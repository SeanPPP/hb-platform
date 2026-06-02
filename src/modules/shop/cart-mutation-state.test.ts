import assert from "node:assert/strict";
import { shouldClearActiveCartMutation } from "./cart-mutation-state";

function run() {
  assert.equal(
    shouldClearActiveCartMutation("1024", "1024"),
    true,
    "同一门店的请求完成后应清理 loading 状态"
  );
  assert.equal(
    shouldClearActiveCartMutation("2048", "1024"),
    false,
    "旧门店请求迟到时不能清理当前门店 loading 状态"
  );
  assert.equal(
    shouldClearActiveCartMutation(" 1024 ", "1024"),
    true,
    "门店代码比较前应先 trim，避免空格导致 loading 卡住"
  );

  console.log("cart-mutation-state.test.ts: ok");
}

run();
