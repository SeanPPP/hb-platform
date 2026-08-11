import type {
  SharedHeldOrderPublicationRunResult,
  SharedHeldOrderPublicationWorker,
} from "./shared-held-order-publication-worker";

export type SharedHeldOrderPublicationSchedulerPort = Readonly<{
  every(intervalMs: number, task: () => void): () => void;
}>;

export type SharedHeldOrderPublicationLoopOptions = Readonly<{
  worker: Pick<SharedHeldOrderPublicationWorker, "runOnce">;
  scheduler: SharedHeldOrderPublicationSchedulerPort;
  intervalMs?: number;
}>;

const DEFAULT_PUBLICATION_INTERVAL_MS = 10_000;

/**
 * 登录期间周期唤醒耐久发布队列。循环只负责单飞和生命周期；失败事实仍由队列
 * worker 持久化，组合根退出时则等待已开始的一轮结束后再关闭数据库。
 */
export class SharedHeldOrderPublicationLoop {
  private readonly intervalMs: number;
  private cancelTimer: (() => void) | null = null;
  private inFlight: Promise<SharedHeldOrderPublicationRunResult> | null = null;
  private shutdownStarted = false;

  public constructor(
    private readonly options: SharedHeldOrderPublicationLoopOptions,
  ) {
    this.intervalMs = options.intervalMs ?? DEFAULT_PUBLICATION_INTERVAL_MS;
  }

  public resume(): void {
    if (this.shutdownStarted) {
      throw new Error("SHARED_HELD_ORDER_PUBLICATION_LOOP_SHUTDOWN");
    }
    if (this.cancelTimer) return;

    this.cancelTimer = this.options.scheduler.every(this.intervalMs, () => {
      void this.runNow().catch(() => undefined);
    });
    // 登录成功立即尝试一次；后台失败不能回滚可信收银员会话。
    void this.runNow().catch(() => undefined);
  }

  public pause(): void {
    this.cancelTimer?.();
    this.cancelTimer = null;
  }

  public runNow(): Promise<SharedHeldOrderPublicationRunResult> {
    if (this.shutdownStarted) {
      return Promise.reject(
        new Error("SHARED_HELD_ORDER_PUBLICATION_LOOP_SHUTDOWN"),
      );
    }
    if (this.inFlight) return this.inFlight;

    const run = this.options.worker.runOnce().finally(() => {
      if (this.inFlight === run) this.inFlight = null;
    });
    this.inFlight = run;
    return run;
  }

  public async shutdown(): Promise<void> {
    if (!this.shutdownStarted) {
      this.shutdownStarted = true;
      this.pause();
    }
    try {
      await this.inFlight;
    } catch {
      // 关闭只等待数据库访问退出；耐久队列保留失败事实供下次 runtime 重试。
    }
  }
}
