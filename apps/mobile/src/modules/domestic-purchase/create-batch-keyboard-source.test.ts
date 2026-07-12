import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const currentDir = dirname(fileURLToPath(import.meta.url));
const screenSource = readFileSync(
  resolve(currentDir, "../../../app/(tabs)/domestic-purchase.tsx"),
  "utf8"
);
const zhLocale = JSON.parse(
  readFileSync(resolve(currentDir, "../../locales/zh/screens/domesticPurchase.json"), "utf8")
);
const enLocale = JSON.parse(
  readFileSync(resolve(currentDir, "../../locales/en/screens/domesticPurchase.json"), "utf8")
);

const compact = (source: string) => source.replace(/\s+/g, " ").trim();

function assertContains(source: string, expected: string, message: string) {
  assert.ok(compact(source).includes(compact(expected)), message);
}

function extractInput(labelKey: string) {
  const labelIndex = screenSource.indexOf(`label={t("${labelKey}")}`);
  const startIndex = screenSource.lastIndexOf("<TextInput", labelIndex);
  const endIndex = screenSource.indexOf("/>", labelIndex);
  assert.ok(labelIndex >= 0 && startIndex >= 0 && endIndex > labelIndex, `应能读取 ${labelKey} 输入框`);
  return screenSource.slice(startIndex, endIndex + 2);
}

function extractAccessory(accessoryName: string) {
  const idIndex = screenSource.indexOf(`nativeID={${accessoryName}}`);
  const startIndex = screenSource.lastIndexOf("<InputAccessoryView", idIndex);
  const closingTag = "</InputAccessoryView>";
  const endIndex = screenSource.indexOf(closingTag, idIndex);
  assert.ok(idIndex >= 0 && startIndex >= 0 && endIndex > idIndex, `应能读取 ${accessoryName} 工具栏`);
  return screenSource.slice(startIndex, endIndex + closingTag.length);
}

function extractCallback(callbackName: string) {
  const startMarker = `const ${callbackName} = useCallback(() => {`;
  const startIndex = screenSource.indexOf(startMarker);
  const endMarker = "}, []);";
  const endIndex = screenSource.indexOf(endMarker, startIndex);
  assert.ok(startIndex >= 0 && endIndex > startIndex, `应能读取 ${callbackName} 回调`);
  return screenSource.slice(startIndex, endIndex + endMarker.length);
}

function extractAccessoryId(constantName: string) {
  const match = screenSource.match(
    new RegExp(`const\\s+${constantName}\\s*=\\s*["']([^"']*)["']`)
  );
  assert.ok(match, `应声明 ${constantName}`);
  return match[1];
}

function extractStyle(styleName: string) {
  const startIndex = screenSource.indexOf(`${styleName}: {`);
  const endIndex = screenSource.indexOf("},", startIndex);
  assert.ok(startIndex >= 0 && endIndex > startIndex, `应能读取 ${styleName} 样式`);
  return screenSource.slice(startIndex, endIndex + 2);
}

const countInput = extractInput("create.count");
const priceInput = extractInput("create.privateLabelPrice");
const countAccessory = extractAccessory("CREATE_COUNT_ACCESSORY_ID");
const priceAccessory = extractAccessory("CREATE_PRICE_ACCESSORY_ID");
const focusPriceHandler = extractCallback("focusPrivateLabelPriceInput");
const finishPriceHandler = extractCallback("finishPrivateLabelPriceEditing");
const countAccessoryId = extractAccessoryId("CREATE_COUNT_ACCESSORY_ID");
const priceAccessoryId = extractAccessoryId("CREATE_PRICE_ACCESSORY_ID");

assert.ok(countAccessoryId.trim() && priceAccessoryId.trim(), "两个 accessory ID 都不能为空");
assert.notEqual(countAccessoryId, priceAccessoryId, "数量和价格 accessory ID 必须不同");

assertContains(countInput, 'returnKeyType={Platform.OS === "ios" ? "next" : undefined}', "数量提交键必须由 iOS gate 控制");
assertContains(countInput, 'inputAccessoryViewID={Platform.OS === "ios" ? CREATE_COUNT_ACCESSORY_ID : undefined}', "数量输入必须连接 COUNT ID");
assertContains(countInput, 'onSubmitEditing={Platform.OS === "ios" ? focusPrivateLabelPriceInput : undefined}', "数量提交必须复用 focus handler");
assertContains(priceInput, "privateLabelPriceInputRef.current = input", "价格输入必须真实绑定价格 ref");
assertContains(priceInput, 'returnKeyType={Platform.OS === "ios" ? "done" : undefined}', "价格提交键必须由 iOS gate 控制");
assertContains(priceInput, 'inputAccessoryViewID={Platform.OS === "ios" ? CREATE_PRICE_ACCESSORY_ID : undefined}', "价格输入必须连接 PRICE ID");
assertContains(priceInput, 'onSubmitEditing={Platform.OS === "ios" ? finishPrivateLabelPriceEditing : undefined}', "价格提交必须复用 finish handler");

const accessoryBranch = compact(screenSource).match(
  /\{Platform\.OS === "ios" \? \( <> <InputAccessoryView[\s\S]*<\/InputAccessoryView> <InputAccessoryView[\s\S]*<\/InputAccessoryView> <\/> \) : null\}/
)?.[0];
assert.ok(accessoryBranch, "两个工具栏必须完整位于 iOS 平台分支内");
assertContains(accessoryBranch, countAccessory, "iOS 分支必须包含数量工具栏");
assertContains(accessoryBranch, priceAccessory, "iOS 分支必须包含价格工具栏");

assertContains(countAccessory, "nativeID={CREATE_COUNT_ACCESSORY_ID}", "数量工具栏必须使用 COUNT ID");
assertContains(countAccessory, 't("create.next")', "数量工具栏必须显示下一步");
assertContains(countAccessory, "onPress={focusPrivateLabelPriceInput}", "数量工具栏必须复用 focus handler");
assertContains(priceAccessory, "nativeID={CREATE_PRICE_ACCESSORY_ID}", "价格工具栏必须使用 PRICE ID");
assertContains(priceAccessory, 't("create.done")', "价格工具栏必须显示完成");
assertContains(priceAccessory, "onPress={finishPrivateLabelPriceEditing}", "价格工具栏必须复用 finish handler");

for (const accessory of [countAccessory, priceAccessory]) {
  assertContains(accessory, "contentStyle={styles.keyboardAccessoryButton}", "工具栏按钮必须使用 44 点内容样式");
  assertContains(accessory, 'textColor="#0958D9"', "工具栏按钮必须使用高对比文字色");
}
assertContains(extractStyle("keyboardAccessoryButton"), "minHeight: 44", "工具栏按钮内容高度至少为 44 点");
assertContains(focusPriceHandler, "privateLabelPriceInputRef.current?.focus()", "focus handler 必须聚焦价格输入");
assertContains(finishPriceHandler, "privateLabelPriceInputRef.current?.blur()", "finish handler 必须主动 blur 价格输入");
assertContains(finishPriceHandler, "Keyboard.dismiss()", "finish handler 必须收起键盘");

assert.equal(zhLocale.create.next, "下一步");
assert.equal(zhLocale.create.done, "完成");
assert.equal(enLocale.create.next, "Next");
assert.equal(enLocale.create.done, "Done");

console.log("create-batch-keyboard-source.test.ts: ok");
