import assert from "node:assert/strict";
import { createHiddenInputFocusController } from "./hid-hidden-input-focus";

function run() {
  let focusCount = 0;
  const controller = createHiddenInputFocusController(() => {
    focusCount += 1;
  });

  controller.focusIfAllowed();
  assert.equal(focusCount, 1, "默认允许隐藏扫码输入框获取焦点");

  controller.pauseHiddenInputFocus();
  controller.focusIfAllowed();
  assert.equal(focusCount, 1, "用户编辑可见输入框时不应抢回隐藏输入焦点");

  controller.resumeHiddenInputFocus();
  assert.equal(focusCount, 2, "恢复扫码输入时应重新聚焦隐藏输入框");

  controller.pauseHiddenInputFocus();
  controller.focusIfAllowed();
  controller.resumeHiddenInputFocus({ refocus: false });
  assert.equal(focusCount, 2, "显式跳过恢复聚焦时只恢复状态，不立即抢焦点");

  controller.focusIfAllowed();
  assert.equal(focusCount, 3, "恢复状态后后续扫码聚焦仍然可用");

  console.log("hid-hidden-input-focus.test.ts: ok");
}

run();
