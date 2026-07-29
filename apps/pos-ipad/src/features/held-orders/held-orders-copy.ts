export const heldOrdersEnglishCopy = {
  title: "Held sales",
  subtitle: "Durable local holds for this terminal only.",
  "action.back": "Back to sales",
  "action.refresh": "Refresh",
  "action.hold": "Hold current sale",
  "action.recall": "Recall sale",
  "action.recover": "Recover sale",
  "action.release": "Return to pending",
  "list.title": "Pending holds",
  "list.sequence": "Local #{{sequence}}",
  "list.items": "{{count}} items",
  "list.amount": "Amount",
  "list.heldAt": "Held {{time}}",
  "status.Pending": "Ready to recall",
  "status.Recalling": "Recall needs recovery",
  "empty.title": "No held sales on this terminal",
  "empty.hint": "Holds are local to this store and device.",
  "loading": "Loading held sales…",
  "unauthorized.title": "Recall access is not available",
  "unauthorized.hint": "Ask a supervisor if you need to recall a sale.",
  "failed.title": "Held sales could not be loaded",
  "failed.hint": "The encrypted ledger was not changed. Try again when ready.",
  "result.held": "Sale held locally.",
  "result.recalled": "Held sale restored.",
  "result.recovered": "Held sale restored after recovery.",
  "result.released": "The recovered sale was returned to pending.",
  "result.authorization-denied": "This action is not authorized.",
  "result.sale-mode-required": "Only sale carts can be held or recalled.",
  "result.cart-empty": "Add an item before holding this sale.",
  "result.cart-not-empty": "Finish or clear the current sale before recalling.",
  "result.operation-in-progress": "Another hold action is still in progress.",
  "result.hold-failed": "The sale was not held.",
  "result.hold-committed-cart-not-cleared": "The sale was held; verify the cart before continuing.",
  "result.hold-fence-not-cleared": "The sale was held and the terminal remains blocked until cart recovery completes.",
  "result.terminal-fence-blocked": "Finish the active held-sale recovery before continuing.",
  "result.claim-failed": "This hold is no longer available to recall.",
  "result.restore-failed": "The hold was released without restoring the cart.",
  "result.complete-failed": "The hold was released without restoring the cart.",
  "result.rollback-failed": "Recall needs supervisor recovery before continuing.",
  "result.release-failed": "Recall remains in recovery. Use Recover hold explicitly.",
  "result.load-failed": "Held sales could not be loaded.",
} as const;

export type HeldOrdersCopyKey = keyof typeof heldOrdersEnglishCopy;

const heldOrdersChineseCopy = {
  title: "挂单",
  subtitle: "仅限本门店、本终端的耐久本地挂单。",
  "action.back": "返回收银",
  "action.refresh": "刷新",
  "action.hold": "挂起当前销售",
  "action.recall": "取回销售",
  "action.recover": "恢复取单",
  "action.release": "退回待取",
  "list.title": "待取挂单",
  "list.sequence": "本地序号 #{{sequence}}",
  "list.items": "{{count}} 件",
  "list.amount": "金额",
  "list.heldAt": "挂单于 {{time}}",
  "status.Pending": "可取回",
  "status.Recalling": "需要恢复",
  "empty.title": "此终端没有挂单",
  "empty.hint": "挂单仅属于当前门店和设备。",
  loading: "正在读取挂单…",
  "unauthorized.title": "无取单权限",
  "unauthorized.hint": "如需取回销售，请联系主管。",
  "failed.title": "无法读取挂单",
  "failed.hint": "加密账本未被修改，可稍后重试。",
  "result.held": "销售已本地挂单。",
  "result.recalled": "已恢复挂单销售。",
  "result.recovered": "已恢复挂单销售。",
  "result.released": "已将恢复中的销售退回待取。",
  "result.authorization-denied": "当前操作未获授权。",
  "result.sale-mode-required": "仅销售购物车可挂单或取单。",
  "result.cart-empty": "请先添加商品再挂单。",
  "result.cart-not-empty": "请先完成或清空当前销售再取单。",
  "result.operation-in-progress": "另一个挂单操作仍在进行。",
  "result.hold-failed": "销售未挂单。",
  "result.hold-committed-cart-not-cleared": "销售已挂单，请确认购物车后再继续。",
  "result.hold-fence-not-cleared": "销售已挂单，完成购物车恢复前此终端保持交易锁定。",
  "result.terminal-fence-blocked": "请先完成当前挂单恢复流程。",
  "result.claim-failed": "此挂单已无法取回。",
  "result.restore-failed": "挂单已释放，但未恢复购物车。",
  "result.complete-failed": "挂单已释放，但未恢复购物车。",
  "result.rollback-failed": "取单需要主管恢复后才能继续。",
  "result.release-failed": "取单仍在恢复状态，请明确点击恢复挂单。",
  "result.load-failed": "无法读取挂单。",
} as const satisfies Record<HeldOrdersCopyKey, string>;

const heldOrdersCopy = { en: heldOrdersEnglishCopy, zh: heldOrdersChineseCopy } as const;

export type HeldOrdersLocale = keyof typeof heldOrdersCopy;

export function resolveHeldOrdersLocale(language?: string): HeldOrdersLocale {
  return language?.toLowerCase().startsWith("zh") ? "zh" : "en";
}

export function heldOrdersText(
  locale: HeldOrdersLocale,
  key: HeldOrdersCopyKey,
  values?: Readonly<Record<string, string | number>>,
): string {
  const template = heldOrdersCopy[locale][key];
  if (!values) return template;
  return template.replace(/\{\{(\w+)\}\}/g, (placeholder, name: string) => {
    const value = values[name];
    return value === undefined ? placeholder : String(value);
  });
}
