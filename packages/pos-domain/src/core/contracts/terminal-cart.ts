/**
 * 一台终端只能有一个耐久购物车工作流栅栏。它不保存购物车内容，也不进入
 * outbox；只负责在挂单清车或取单成交完成前禁止普通交易穿透。
 */
export type TerminalCartScope = Readonly<{
  storeCode: string;
  deviceCode: string;
}>;

export type TerminalCartFenceKind = "HoldClear" | "RecallActive";

export type TerminalCartFence = Readonly<{
  scope: TerminalCartScope;
  kind: TerminalCartFenceKind;
  holdId: string;
  recallAttemptId: string | null;
  /** 为后续在线/混合支付草稿预留；现金取单始终为 null。 */
  boundOrderGuid: string | null;
  createdAtIso: string;
}>;

/**
 * 该 binding 只能由共享 ActivePricingCartSession 在成功恢复取单后注入。
 * 页面、路由和 CashCheckoutInput 都不能构造或覆盖它。
 */
export type RecallActiveBinding = Readonly<{
  kind: "recalled";
  scope: TerminalCartScope;
  holdId: string;
  recallAttemptId: string;
}>;

export type TerminalCheckoutContext =
  | Readonly<{ kind: "none" }>
  | RecallActiveBinding;
