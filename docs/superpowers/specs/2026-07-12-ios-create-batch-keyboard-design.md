# iOS 创建批次数字键盘交互设计

## 背景与目标

移动端“中国采购”页面的“创建货号条码批次”弹窗使用 `number-pad` 和 `decimal-pad`。iOS 真机的数字键盘没有可用的提交键，用户输入创建数量后无法自然进入“统一零售价”，输入完成后也无法主动收起键盘查看“确认创建”。

本次只修复这个弹窗的 iOS 键盘流程，不改创建接口、字段校验、Android 行为或现有视觉体系。

## 根因

- `apps/mobile/app/(tabs)/domestic-purchase.tsx` 中两个数值输入框分别使用 `number-pad` 与 `decimal-pad`。
- 弹窗没有 `InputAccessoryView`，也没有其他显式的“下一步/完成”入口。
- 创建按钮位于输入框下方，键盘打开后会被遮挡；用户只能依赖系统手势或点击弹窗外部，流程不稳定。

## 交互设计

1. 创建数量获得焦点时，iOS 键盘顶部显示“下一步”。
2. 点击“下一步”后，焦点转到“统一零售价”，键盘继续保持打开。
3. 统一零售价获得焦点时，键盘顶部显示“完成”。
4. 点击“完成”后先让零售价输入框显式失焦，再调用 `Keyboard.dismiss()` 兜底，确保工具栏和键盘一起收起。
5. iOS 外接键盘触发提交时复用同一流程：数量进入下一字段，零售价收起键盘。
6. Android 不渲染 iOS 键盘工具栏，原行为保持不变。

## 技术方案

- 使用 React Native 0.81 自带的 `InputAccessoryView`，不增加依赖。
- 为数量和零售价分别定义稳定的 accessory ID，并通过 Paper `TextInput` 透传 `inputAccessoryViewID`。
- 使用 Paper `TextInput` 的 ref 将数量输入的“下一步”动作连接到零售价输入，并由统一完成回调执行 `blur()` 与 `Keyboard.dismiss()`。
- 在关键平台分支旁添加中文注释，解释 iOS 数字键盘为何需要工具栏。
- 补充中英文 `create.next` 与 `create.done` 文案。

## 文件边界

- `apps/mobile/app/(tabs)/domestic-purchase.tsx`：焦点、收起键盘和 accessory UI。
- `apps/mobile/src/locales/zh/screens/domesticPurchase.json`：中文“下一步/完成”。
- `apps/mobile/src/locales/en/screens/domesticPurchase.json`：英文“Next/Done”。
- `apps/mobile/src/modules/domestic-purchase/create-batch-keyboard-source.test.ts`：固定输入框与 accessory 的接线契约。
- `apps/mobile/package.json`：增加该回归测试的独立脚本。

## 验证

- TDD 红灯：先运行新增测试，确认因缺少 accessory 接线而失败。
- TDD 绿灯：实现后运行 `npm run test:domestic-purchase-keyboard`。
- 运行 `npm run test:i18n-locales` 验证中英文结构一致。
- 运行 `npx tsc --noEmit` 验证 Paper ref、React Native 属性和 JSX 类型。
- 运行 `git diff --check` 与 GitNexus `detect_changes` 核对修改边界。
- 自动化测试只能验证接线；最终 iOS 真机需要按“数量 -> 下一步 -> 零售价 -> 完成 -> 确认创建”做一次 smoke test。

## 非目标

- 不改弹窗整体布局或样式体系。
- 不引入第三方键盘库。
- 不改后端批次创建逻辑。
- 不把相同工具栏扩展到其他页面。
