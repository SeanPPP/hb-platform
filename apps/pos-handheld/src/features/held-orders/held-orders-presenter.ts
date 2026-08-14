import type { HeldOrderActionResult } from "./held-orders-domain";
import { HeldOrdersOrchestrator } from "./held-orders-orchestrator";

import type { HeldOrderSummary } from "@/core/contracts";

export type HeldOrdersPresenterState = Readonly<{
  kind: "loading" | "ready" | "unauthorized" | "failed";
  rows: readonly HeldOrderSummary[];
  busy: boolean;
  lastAction: HeldOrderActionResult | null;
}>;

/** React 无关 presenter：路由未来注入真实组合根前，屏幕不会伪造持久能力。 */
export class HeldOrdersPresenter {
  public state: HeldOrdersPresenterState = {
    kind: "loading",
    rows: [],
    busy: false,
    lastAction: null,
  };

  private readonly listeners = new Set<() => void>();
  private refreshInFlight: Promise<void> | null = null;
  private actionInFlight: Promise<HeldOrderActionResult> | null = null;
  private destroyed = false;

  public constructor(private readonly orchestrator: HeldOrdersOrchestrator) {}

  public readonly getState = (): HeldOrdersPresenterState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public destroy(): void {
    this.destroyed = true;
    this.listeners.clear();
  }

  public refresh(): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (this.refreshInFlight) return this.refreshInFlight;
    this.patch({ kind: "loading", lastAction: null });
    const operation = this.orchestrator
      .list()
      .then((rows) => {
        if (this.destroyed) return;
        this.patch({ kind: "ready", rows });
      })
      .catch((error: unknown) => {
        if (this.destroyed) return;
        this.patch({
          kind:
            error instanceof Error && error.message === "HELD_ORDER_LIST_UNAUTHORIZED"
              ? "unauthorized"
              : "failed",
          rows: [],
        });
      })
      .finally(() => {
        if (this.refreshInFlight === operation) this.refreshInFlight = null;
      });
    this.refreshInFlight = operation;
    return operation;
  }

  public hold(): Promise<HeldOrderActionResult> {
    return this.runAction(() => this.orchestrator.hold());
  }

  public recall(holdId: string): Promise<HeldOrderActionResult> {
    return this.runAction(() => this.orchestrator.recall(holdId));
  }

  public recover(holdId: string): Promise<HeldOrderActionResult> {
    return this.runAction(() => this.orchestrator.recover(holdId));
  }

  public release(holdId: string): Promise<HeldOrderActionResult> {
    return this.runAction(() => this.orchestrator.release(holdId));
  }

  private async runAction(
    action: () => Promise<HeldOrderActionResult>,
  ): Promise<HeldOrderActionResult> {
    if (this.destroyed) return { ok: false, code: "operation-in-progress" };
    if (this.actionInFlight) {
      return { ok: false, code: "operation-in-progress" };
    }
    this.patch({ busy: true, lastAction: null });
    const operation = (async () => {
      let result: HeldOrderActionResult;
      try {
        result = await action();
      } catch {
        result = { ok: false, code: "load-failed" };
      }
      if (this.destroyed) return result;
      this.patch({ busy: false, lastAction: result });
      if (shouldRefreshAfterAction(result)) {
        await this.refresh();
      }
      return result;
    })().finally(() => {
      if (this.actionInFlight === operation) this.actionInFlight = null;
    });
    this.actionInFlight = operation;
    return operation;
  }

  private patch(patch: Partial<HeldOrdersPresenterState>): void {
    this.state = { ...this.state, ...patch };
    for (const listener of [...this.listeners]) {
      try {
        listener();
      } catch {
        // 一个已卸载页面不能阻止其他订阅者看到最新耐久状态。
      }
    }
  }
}

function shouldRefreshAfterAction(result: HeldOrderActionResult): boolean {
  return (
    result.ok ||
    result.code === "hold-committed-cart-not-cleared" ||
    result.code === "hold-fence-not-cleared" ||
    result.code === "restore-failed" ||
    result.code === "rollback-failed" ||
    result.code === "release-failed"
  );
}
