export const UPDATE_TRANSITION_IN_PROGRESS =
  "UPDATE_TRANSITION_IN_PROGRESS";
export const UPDATE_TRANSITION_BARRIER_UNAVAILABLE =
  "UPDATE_TRANSITION_BARRIER_UNAVAILABLE";

export type UpdateTransitionBarrier = <T>(
  operation: () => Promise<T>,
) => Promise<T>;

type TransitionListener = () => void;

export type UpdateOperationLeasePort = Readonly<{
  runOperation<T>(operation: () => T | Promise<T>): Promise<T>;
}>;

/**
 * 更新切换是进程级写租约：开始时同步关闭新的普通业务 operation，
 * 进入组合根临界区后等待已有 operation，再把唯一执行权交给更新动作。
 */
export class UpdateTransitionLeaseCoordinator {
  private barrier: UpdateTransitionBarrier | null = null;
  private transitionActive = false;
  private criticalSectionActive = false;
  private activeOperations = 0;
  private readonly operationWaiters = new Set<() => void>();
  private readonly listeners = new Set<TransitionListener>();

  public bindTransitionBarrier(barrier: UpdateTransitionBarrier): void {
    if (this.barrier) {
      throw new Error("Update transition barrier is already bound.");
    }
    this.barrier = barrier;
  }

  public isTransitionActive(): boolean {
    return this.transitionActive;
  }

  /**
   * 只有此状态为 true 时，组合根才可忽略由更新自身持有的购物车 exclusive lease。
   */
  public isCriticalSectionActive(): boolean {
    return this.criticalSectionActive;
  }

  public subscribe(listener: TransitionListener): () => void {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  }

  public runOperation<T>(operation: () => T | Promise<T>): Promise<T> {
    if (this.transitionActive) {
      return Promise.reject(
        codedError(
          UPDATE_TRANSITION_IN_PROGRESS,
          "App update transition is in progress.",
        ),
      );
    }
    this.activeOperations += 1;
    let result: T | Promise<T>;
    try {
      result = operation();
    } catch (error) {
      this.releaseOperation();
      return Promise.reject(error);
    }
    return Promise.resolve(result).finally(() => {
      this.releaseOperation();
    });
  }

  public runTransition<T>(operation: () => Promise<T>): Promise<T> {
    if (this.transitionActive) {
      return Promise.reject(
        codedError(
          UPDATE_TRANSITION_IN_PROGRESS,
          "App update transition is already in progress.",
        ),
      );
    }
    if (!this.barrier) {
      return Promise.reject(
        codedError(
          UPDATE_TRANSITION_BARRIER_UNAVAILABLE,
          "App update transition barrier is unavailable.",
        ),
      );
    }

    this.transitionActive = true;
    this.notify();
    const barrier = this.barrier;
    let result: Promise<T>;
    try {
      result = this.executeTransition(barrier, operation);
    } catch (error) {
      this.releaseTransition();
      return Promise.reject(error);
    }
    return Promise.resolve(result).finally(() => {
      this.releaseTransition();
    });
  }

  private async executeTransition<T>(
    barrier: UpdateTransitionBarrier,
    operation: () => Promise<T>,
  ): Promise<T> {
    // 固定锁序：先封门并等普通 operation 清零，再申请购物车 exclusive；
    // 普通 operation 即使稍后申请购物车也不会与 transition 形成反向等待。
    await this.waitForOperations();
    return barrier(async () => {
      this.criticalSectionActive = true;
      try {
        return await operation();
      } finally {
        this.criticalSectionActive = false;
      }
    });
  }

  private waitForOperations(): Promise<void> {
    if (this.activeOperations === 0) return Promise.resolve();
    return new Promise((resolve) => {
      this.operationWaiters.add(resolve);
    });
  }

  private releaseOperation(): void {
    this.activeOperations -= 1;
    if (this.activeOperations !== 0) return;
    for (const resolve of this.operationWaiters) resolve();
    this.operationWaiters.clear();
  }

  private releaseTransition(): void {
    this.criticalSectionActive = false;
    this.transitionActive = false;
    this.notify();
  }

  private notify(): void {
    for (const listener of this.listeners) {
      try {
        listener();
      } catch {
        // 单个 UI 门禁订阅异常不能破坏全局切换租约。
      }
    }
  }
}

function codedError(code: string, message: string): Error {
  return Object.assign(new Error(message), { code });
}
