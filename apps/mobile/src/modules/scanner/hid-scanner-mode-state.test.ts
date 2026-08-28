import assert from "node:assert/strict";
import {
  getHidScannerForceTextInput,
  setHidScannerForceTextInput,
  subscribeHidScannerMode,
} from "./hid-scanner-mode-state";

setHidScannerForceTextInput(false);

const firstSubscriberValues: boolean[] = [];
const secondSubscriberValues: boolean[] = [];
const unsubscribeFirst = subscribeHidScannerMode((forceTextInput) => {
  firstSubscriberValues.push(forceTextInput);
});
const unsubscribeSecond = subscribeHidScannerMode((forceTextInput) => {
  secondSubscriberValues.push(forceTextInput);
});

setHidScannerForceTextInput(true);
assert.equal(getHidScannerForceTextInput(), true, "mode state 应保存 fallback 后的 TextInput 模式");
assert.equal(firstSubscriberValues.at(-1), true, "已挂载的 A owner 应同步切换 TextInput 模式");
assert.equal(secondSubscriberValues.at(-1), true, "已挂载的 B owner 应同步切换 TextInput 模式");

const lateSubscriberValues: boolean[] = [];
const unsubscribeLate = subscribeHidScannerMode((forceTextInput) => {
  lateSubscriberValues.push(forceTextInput);
});
assert.equal(getHidScannerForceTextInput(), true, "晚订阅者应能读取当前 TextInput 模式");
assert.equal(lateSubscriberValues.at(-1), true, "晚订阅者订阅时应立即看到当前模式");

setHidScannerForceTextInput(false);
assert.equal(firstSubscriberValues.at(-1), false, "A owner 应同步恢复 native 模式");
assert.equal(secondSubscriberValues.at(-1), false, "B owner 应同步恢复 native 模式");
assert.equal(lateSubscriberValues.at(-1), false, "晚订阅者也应收到后续模式变更");

unsubscribeFirst();
unsubscribeSecond();
unsubscribeLate();

console.log("hid-scanner-mode-state.test.ts: ok");
