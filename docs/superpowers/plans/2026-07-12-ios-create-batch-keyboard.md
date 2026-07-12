# iOS 创建批次数字键盘 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 iOS 真机在创建批次弹窗中可以从创建数量点击“下一步”进入统一零售价，并点击“完成”收起键盘后继续创建。

**Architecture:** 保留现有 `DomesticPurchaseScreen` 与 React Native Paper 弹窗结构，使用 React Native 原生 `InputAccessoryView` 为两个数字输入分别提供平台专属工具栏。通过一个输入框 ref 完成焦点转移，Android 不挂载 accessory；源代码契约测试固定关键接线，中英文 locale parity 继续由现有脚本验证。

**Tech Stack:** Expo 54、React Native 0.81、React 19、React Native Paper 5、TypeScript 5、Node `assert` + `tsx`

## Global Constraints

- 只修改 `apps/mobile` 中与创建批次键盘交互直接相关的文件。
- 不增加依赖，不修改后端、原生配置、OTA 或 APK 发布链路。
- Android 行为保持不变，`InputAccessoryView` 只在 `Platform.OS === "ios"` 时接线和渲染。
- 数量工具栏文字为中文“下一步”/英文“Next”，点击后聚焦统一零售价。
- 零售价工具栏文字为中文“完成”/英文“Done”，点击后先执行输入框 `blur()`，再调用 `Keyboard.dismiss()` 兜底。
- 关键平台逻辑必须有清晰中文注释。
- 先看到回归测试因缺少实现而失败，再写生产代码。
- 不提交 git commit；由用户决定后续提交边界。

---

### Task 1: 创建批次 iOS 数字键盘流程

**Files:**
- Create: `apps/mobile/src/modules/domestic-purchase/create-batch-keyboard-source.test.ts`
- Modify: `apps/mobile/app/(tabs)/domestic-purchase.tsx:1-40, 121-220, 613-638, 748-860`
- Modify: `apps/mobile/src/locales/zh/screens/domesticPurchase.json:20-30`
- Modify: `apps/mobile/src/locales/en/screens/domesticPurchase.json:20-30`
- Modify: `apps/mobile/package.json:4-20`

**Interfaces:**
- Consumes: React Native `InputAccessoryView`、`Keyboard`、`Platform`；Paper `TextInput` ref 的 `focus()`。
- Produces: `CREATE_COUNT_ACCESSORY_ID`、`CREATE_PRICE_ACCESSORY_ID`、`focusPrivateLabelPriceInput()`、`finishPrivateLabelPriceEditing()` 和 `test:domestic-purchase-keyboard` 脚本。

- [ ] **Step 1: 写入失败的接线回归测试**

```ts
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

function extractInput(labelKey: string) {
  const labelIndex = screenSource.indexOf(`label={t("${labelKey}")}`);
  assert.ok(labelIndex >= 0, `应存在 ${labelKey} 输入框`);
  const startIndex = screenSource.lastIndexOf("<TextInput", labelIndex);
  const endIndex = screenSource.indexOf("/>", labelIndex);
  assert.ok(startIndex >= 0 && endIndex > labelIndex, `应能读取 ${labelKey} 输入框源码`);
  return screenSource.slice(startIndex, endIndex + 2);
}

const countInput = extractInput("create.count");
const priceInput = extractInput("create.privateLabelPrice");

assert.ok(countInput.includes("CREATE_COUNT_ACCESSORY_ID"), "创建数量应连接 iOS 下一步工具栏");
assert.ok(
  countInput.includes('returnKeyType={Platform.OS === "ios" ? "next" : undefined}'),
  "创建数量只应在 iOS 声明下一步提交键"
);
assert.ok(
  countInput.includes("onSubmitEditing={Platform.OS === \"ios\" ? focusPrivateLabelPriceInput : undefined}"),
  "创建数量只应在 iOS 提交时聚焦零售价"
);
assert.ok(priceInput.includes("CREATE_PRICE_ACCESSORY_ID"), "零售价应连接 iOS 完成工具栏");
assert.ok(
  priceInput.includes('returnKeyType={Platform.OS === "ios" ? "done" : undefined}'),
  "零售价只应在 iOS 声明完成提交键"
);
assert.ok(
  priceInput.includes("onSubmitEditing={Platform.OS === \"ios\" ? finishPrivateLabelPriceEditing : undefined}"),
  "零售价只应在 iOS 提交时执行统一完成回调"
);
assert.ok(screenSource.includes('t("create.next")'), "数量工具栏应显示下一步");
assert.ok(screenSource.includes('t("create.done")'), "零售价工具栏应显示完成");
assert.ok(screenSource.includes("onPress={focusPrivateLabelPriceInput}"), "下一步按钮应聚焦零售价");
assert.ok(screenSource.includes("onPress={finishPrivateLabelPriceEditing}"), "完成按钮应执行统一完成回调");
assert.equal(zhLocale.create.next, "下一步");
assert.equal(zhLocale.create.done, "完成");
assert.equal(enLocale.create.next, "Next");
assert.equal(enLocale.create.done, "Done");

console.log("create-batch-keyboard-source.test.ts: ok");
```

- [ ] **Step 2: 运行测试并确认红灯原因正确**

Run: `cd apps/mobile && npx --yes tsx src/modules/domestic-purchase/create-batch-keyboard-source.test.ts`

Expected: FAIL，首个失败信息为“创建数量应连接 iOS 下一步工具栏”或缺少对应 locale；失败必须来自 accessory 功能尚未实现，而不是路径或语法错误。

- [ ] **Step 3: 实现最小 iOS accessory 接线**

在页面顶部增加 React/React Native 导入和稳定 ID：

```tsx
import { useCallback, useEffect, useMemo, useRef, useState, type ElementRef } from "react";
import {
  FlatList,
  InputAccessoryView,
  Keyboard,
  Platform,
  RefreshControl,
  ScrollView,
  StyleSheet,
  View,
} from "react-native";

const CREATE_COUNT_ACCESSORY_ID = "domestic-purchase-create-count-accessory";
const CREATE_PRICE_ACCESSORY_ID = "domestic-purchase-create-price-accessory";
```

在 `DomesticPurchaseScreen` 中增加零售价 ref 与焦点动作：

```tsx
const privateLabelPriceInputRef = useRef<ElementRef<typeof TextInput>>(null);

const focusPrivateLabelPriceInput = useCallback(() => {
  privateLabelPriceInputRef.current?.focus();
}, []);

const finishPrivateLabelPriceEditing = useCallback(() => {
  privateLabelPriceInputRef.current?.blur();
  Keyboard.dismiss();
}, []);
```

给两个输入框增加 iOS accessory、外接键盘提交键和 ref：

```tsx
<TextInput
  mode="outlined"
  label={t("create.count")}
  value={createCount}
  keyboardType="number-pad"
  returnKeyType={Platform.OS === "ios" ? "next" : undefined}
  inputAccessoryViewID={Platform.OS === "ios" ? CREATE_COUNT_ACCESSORY_ID : undefined}
  onSubmitEditing={Platform.OS === "ios" ? focusPrivateLabelPriceInput : undefined}
  onChangeText={setCreateCount}
  style={styles.input}
/>
<TextInput
  ref={privateLabelPriceInputRef}
  mode="outlined"
  label={t("create.privateLabelPrice")}
  value={privateLabelPrice}
  keyboardType="decimal-pad"
  returnKeyType={Platform.OS === "ios" ? "done" : undefined}
  inputAccessoryViewID={Platform.OS === "ios" ? CREATE_PRICE_ACCESSORY_ID : undefined}
  onSubmitEditing={Platform.OS === "ios" ? finishPrivateLabelPriceEditing : undefined}
  onChangeText={setPrivateLabelPrice}
  style={styles.input}
/>
```

在创建弹窗中只为 iOS 渲染两个工具栏，并在平台分支前添加中文注释：

```tsx
{/* iOS 数字键盘没有提交键，补充“下一步/完成”保证创建流程可继续。 */}
{Platform.OS === "ios" ? (
  <>
    <InputAccessoryView nativeID={CREATE_COUNT_ACCESSORY_ID}>
      <View style={styles.keyboardAccessory}>
        <Button
          compact
          textColor="#0958D9"
          contentStyle={styles.keyboardAccessoryButton}
          onPress={focusPrivateLabelPriceInput}
        >
          {t("create.next")}
        </Button>
      </View>
    </InputAccessoryView>
    <InputAccessoryView nativeID={CREATE_PRICE_ACCESSORY_ID}>
      <View style={styles.keyboardAccessory}>
        <Button
          compact
          textColor="#0958D9"
          contentStyle={styles.keyboardAccessoryButton}
          onPress={finishPrivateLabelPriceEditing}
        >
          {t("create.done")}
        </Button>
      </View>
    </InputAccessoryView>
  </>
) : null}
```

样式保持现有浅色体系：

```tsx
keyboardAccessory: {
  minHeight: 44,
  paddingHorizontal: 8,
  alignItems: "flex-end",
  justifyContent: "center",
  borderTopWidth: StyleSheet.hairlineWidth,
  borderTopColor: "#D0D5DD",
  backgroundColor: "#F6F7F9",
},
keyboardAccessoryButton: {
  minHeight: 44,
},
```

在中英文 `create` 下分别增加：

```json
"next": "下一步",
"done": "完成"
```

```json
"next": "Next",
"done": "Done"
```

在 `apps/mobile/package.json` 的 scripts 中增加：

```json
"test:domestic-purchase-keyboard": "npx --yes tsx src/modules/domestic-purchase/create-batch-keyboard-source.test.ts"
```

- [ ] **Step 4: 运行定向测试并确认绿灯**

Run: `cd apps/mobile && npm run test:domestic-purchase-keyboard`

Expected: PASS，输出 `create-batch-keyboard-source.test.ts: ok`。

- [ ] **Step 5: 运行类型、中英文和补丁验证**

Run: `cd apps/mobile && npx tsc --noEmit`

Expected: exit 0，无 TypeScript 错误。

Run: `cd apps/mobile && npm run test:i18n-locales`

Expected: PASS，输出 `Locale parity check passed.`。

Run: `git diff --check`

Expected: exit 0，无空白错误。

- [ ] **Step 6: 核对影响范围，不提交**

Run: `node .gitnexus/run.cjs detect_changes --repo hb-platform`

Expected: 仅包含创建批次页面、对应 locale、定向测试和 package script；风险不高于 LOW/MEDIUM，且不涉及 backend/native/OTA。

Run: `git status --short`

Expected: 只列出本计划 Files 中的文件以及设计/计划文档；保留未提交状态等待用户决定。
