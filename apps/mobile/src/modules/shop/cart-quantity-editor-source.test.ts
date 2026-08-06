import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const cartSource = readFileSync(resolve(process.cwd(), "app/(tabs)/cart.tsx"), "utf8");

function assertIncludes(source: string, expected: string, message: string) {
  if (!source.includes(expected)) {
    throw new Error(message);
  }
}

assertIncludes(
  cartSource,
  "onEditQuantity: (item: StoreOrderCartItem) => void",
  "购物车商品卡片必须暴露自定义数量编辑入口"
);
assertIncludes(
  cartSource,
  'accessibilityLabel={t("common:labels.editCartQuantity", { quantity: item.quantity })}',
  "数量编辑入口必须提供当前数量的可访问标签"
);
assertIncludes(
  cartSource,
  "canSubmitCartQuantityEdit",
  "购物车数量编辑必须复用门店与 pending 提交守卫"
);
assertIncludes(
  cartSource,
  "parseCartQuantityInput",
  "购物车数量编辑必须复用非负整数解析规则"
);
assertIncludes(
  cartSource,
  "shouldSubmitCartQuantityUpdate",
  "购物车数量未变化时不得发起后端写入"
);
assertIncludes(
  cartSource,
  "selectTextOnFocus",
  "打开数量编辑器后必须便于直接覆盖当前数量"
);
assertIncludes(
  cartSource,
  "submitDialogVisible || Boolean(quantityEditorItem)",
  "数量编辑期间必须与订单备注弹窗一样暂停隐藏扫码焦点"
);
assertIncludes(
  cartSource,
  "cartMutationPendingRef.current || quantityEditorSubmittingRef.current",
  "数量编辑入口和确认必须同步阻止其他购物车写操作及快速重复提交"
);
assertIncludes(
  cartSource,
  "resolveCurrentCartQuantityItem(cartQuery.items, editorItem)",
  "确认数量时必须通过已测试的解析函数读取当前购物车商品"
);
assertIncludes(
  cartSource,
  "canDismissCartQuantityEditor({",
  "数量编辑器必须复用已测试的 pending 与同步提交锁关闭规则"
);
assertIncludes(
  cartSource,
  'accessibilityLiveRegion="polite"',
  "数量校验错误必须主动通知屏幕阅读器"
);

console.log("cart-quantity-editor-source.test.ts: ok");
