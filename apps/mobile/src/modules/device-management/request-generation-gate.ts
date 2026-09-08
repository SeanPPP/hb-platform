/** 只允许当前会话、当前请求代次的异步结果写入界面。 */
export class RequestGenerationGate {
  private generation = 0;

  begin() { return ++this.generation; }
  invalidate() { this.generation += 1; }
  isCurrent(generation: number) { return this.generation === generation; }
}

/** 未知创建结果在同一会话内必须持续阻止重复 POST。 */
export class UnknownCreateGate {
  private readonly blocked = new Map<string, "pending" | "unknown">();
  begin(contextKey: string) {
    if (this.blocked.has(contextKey)) return false;
    this.blocked.set(contextKey, "pending");
    return true;
  }
  markUnknown(contextKey: string) { this.blocked.set(contextKey, "unknown"); }
  /** 仅在明确响应或人工完成服务端核对后释放指定上下文，不随界面切换调用。 */
  clearForNewContext(contextKey: string) { this.blocked.delete(contextKey); }
  canSubmit(contextKey: string) { return !this.blocked.has(contextKey); }
  isPending(contextKey: string) { return this.blocked.get(contextKey) === "pending"; }
}

// 只保存账号/业务上下文与请求状态；不含口令，组件卸载/重进不会丢失未知结果。
export const activationCreateGate = new UnknownCreateGate();
export const emergencyCreateGate = new UnknownCreateGate();
