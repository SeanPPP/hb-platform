/**
 * 网络恢复控制器（纯逻辑，不依赖 React Native 运行时，可用 node:test 直接测试）。
 *
 * 职责：
 * 1. 网络恢复检测 —— 通过后端 health 探测确认"后端真正可达"（checkBackend 端口）；
 * 2. 自动补传 —— 后端可达后按 FIFO 顺序重放离线队列中的请求，成功即出队；
 * 3. 退避重试 —— 后端不可达或补传失败时按指数退避调度下次尝试；
 * 4. 状态发布 —— 暴露 isOnline / isBackendReachable / pendingCount 供 UI 展示。
 *
 * 监听触发源（由 React 集成层注入）：
 * - App 回到前台（AppState active）→ notifyAppForeground()
 * - 定时退避到期 → 自动重试
 */
import type { EnqueueRequestInput, OfflineRequestQueue, QueuedRequest } from "./offline-queue";

/** 重试退避参数：基础 5 秒，指数增长，封顶 60 秒。 */
export const RETRY_BASE_MS = 5_000;
export const RETRY_MAX_MS = 60_000;

/** 定时器端口：生产用 setTimeout，测试注入以记录调度而不真实等待。 */
export type SchedulePort = (
  fn: () => void | Promise<void>,
  delayMs: number,
) => { cancel(): void };

/** 对外发布的恢复状态。 */
export type NetworkRecoveryState = Readonly<{
  /** 最近一次探测的网络在线状态（无独立网络监听时由后端探测推导）。 */
  isOnline: boolean;
  /** 后端 health 是否通过。 */
  isBackendReachable: boolean;
  /** 待补传请求数。 */
  pendingCount: number;
  /** 是否正在补传。 */
  isFlushing: boolean;
  /** 最近一次后端探测时间（ISO），null 表示尚未探测。 */
  lastCheckedAtIso: string | null;
}>;

export type NetworkRecoveryControllerDeps = {
  /** 离线请求队列（含持久化）。 */
  queue: OfflineRequestQueue;
  /** 后端可达性探测（内部走 checkBackendReachable 的 { ok } 结果）。 */
  checkBackend: () => Promise<boolean>;
  /** 实际发送补传请求；失败时抛错即视为该条目标记重试。 */
  send: (request: QueuedRequest) => Promise<void>;
  /** 定时器端口，默认 setTimeout 实现。 */
  schedule?: SchedulePort;
  /** 时间戳来源。 */
  nowIso?: () => string;
  /** 队列满丢弃与重试耗尽时的日志回调。 */
  onLog?: (message: string, properties?: Record<string, unknown>) => void;
};

const INITIAL_STATE: NetworkRecoveryState = {
  isOnline: false,
  isBackendReachable: false,
  pendingCount: 0,
  isFlushing: false,
  lastCheckedAtIso: null,
};

export class NetworkRecoveryController {
  private readonly queue: OfflineRequestQueue;
  private readonly checkBackend: () => Promise<boolean>;
  private readonly send: (request: QueuedRequest) => Promise<void>;
  private readonly schedule: SchedulePort;
  private readonly nowIso: () => string;
  private readonly onLog?: (message: string, properties?: Record<string, unknown>) => void;

  private state: NetworkRecoveryState = INITIAL_STATE;
  private readonly listeners = new Set<(state: NetworkRecoveryState) => void>();
  private started = false;
  private flushing = false;
  private retryHandle: { cancel(): void } | null = null;
  private consecutiveFailures = 0;

  public constructor(deps: NetworkRecoveryControllerDeps) {
    this.queue = deps.queue;
    this.checkBackend = deps.checkBackend;
    this.send = deps.send;
    this.schedule =
      deps.schedule ??
      ((fn, delayMs) => {
        const id = setTimeout(fn, delayMs);
        return { cancel: () => clearTimeout(id) };
      });
    this.nowIso = deps.nowIso ?? (() => new Date().toISOString());
    this.onLog = deps.onLog;
  }

  public getState(): NetworkRecoveryState {
    return this.state;
  }

  public subscribe(listener: (state: NetworkRecoveryState) => void): () => void {
    this.listeners.add(listener);
    listener(this.state);
    return () => this.listeners.delete(listener);
  }

  /**
   * 启动控制器：读取遗留队列并立即触发一次恢复检查。
   * 幂等；stop() 后可再次 start()。
   */
  public async start(): Promise<void> {
    if (this.started) {
      return;
    }
    this.started = true;
    await this.refreshPendingCount();
    // App 启动时处理上次离线期间遗留的队列。
    await this.triggerRecovery();
  }

  /** 停止控制器：取消退避定时器，保留队列数据。 */
  public stop(): void {
    this.started = false;
    this.retryHandle?.cancel();
    this.retryHandle = null;
    this.consecutiveFailures = 0;
  }

  /** 业务请求网络失败后入队，等待恢复后补传。 */
  public async enqueue(input: EnqueueRequestInput): Promise<void> {
    await this.queue.enqueue(input);
    await this.refreshPendingCount();
    // 上次探测在线却仍入队，多为瞬时抖动：立即补一次，减少等待。
    if (this.state.isOnline) {
      await this.triggerRecovery();
    }
  }

  /** App 回到前台：网络往往已变化，立即做一次恢复检查。 */
  public async notifyAppForeground(): Promise<void> {
    if (!this.started) {
      return;
    }
    await this.triggerRecovery();
  }

  /**
   * 恢复检查主流程：
   * 1. 后端 health 探测，不可达则调度退避重试；
   * 2. 可达则按 FIFO 逐条补传，成功出队，失败标记重试并中断本轮；
   * 3. 全部完成后若队列仍有剩余（失败项），调度下一次退避重试。
   */
  public async triggerRecovery(): Promise<void> {
    if (!this.started || this.flushing) {
      return;
    }
    this.flushing = true;
    this.patchState({ isFlushing: true });
    try {
      const reachable = await this.checkBackend();
      const checkedAtIso = this.nowIso();
      this.patchState({
        isOnline: reachable,
        isBackendReachable: reachable,
        lastCheckedAtIso: checkedAtIso,
      });

      if (!reachable) {
        // 后端不可达：不补传，按退避稍后重试。
        this.consecutiveFailures += 1;
        this.scheduleRetry();
        return;
      }
      this.consecutiveFailures = 0;

      const items = await this.queue.getAll();
      if (items.length === 0) {
        return;
      }

      for (const item of items) {
        try {
          await this.send(item);
          await this.queue.dequeue(item.id);
        } catch {
          // 单条补传失败：重试次数 +1，超限移出活跃队列，否则保留并退避。
          const updated = await this.queue.markRetry(item.id);
          if (!updated) {
            continue;
          }
          if (updated.retryCount >= updated.maxRetries) {
            await this.queue.dequeue(updated.id);
            this.onLog?.("[network-recovery] 补传重试耗尽，移出队列", {
              id: updated.id,
              url: updated.url,
              retryCount: updated.retryCount,
            });
          } else {
            this.onLog?.("[network-recovery] 补传失败，等待退避重试", {
              id: updated.id,
              url: updated.url,
              retryCount: updated.retryCount,
            });
          }
          this.consecutiveFailures += 1;
          this.scheduleRetry();
          break; // 中断本轮，避免连续失败浪费请求
        }
      }
    } finally {
      this.flushing = false;
      await this.refreshPendingCount();
      this.patchState({ isFlushing: false });
    }
  }

  /** 计算下次退避间隔：5s → 10s → 20s → 40s → 60s 封顶（首次失败为 5s）。 */
  private retryDelayMs(): number {
    const factor = Math.min(Math.max(this.consecutiveFailures - 1, 0), 10);
    return Math.min(RETRY_BASE_MS * 2 ** factor, RETRY_MAX_MS);
  }

  private scheduleRetry(): void {
    this.retryHandle?.cancel();
    this.retryHandle = this.schedule(() => {
      if (this.started && !this.flushing) {
        // 返回 Promise 以便测试与调用方可等待补传完成。
        return this.triggerRecovery();
      }
      return undefined;
    }, this.retryDelayMs());
  }

  private async refreshPendingCount(): Promise<void> {
    const pendingCount = await this.queue.size();
    this.patchState({ pendingCount });
  }

  private patchState(patch: Partial<NetworkRecoveryState>): void {
    this.state = { ...this.state, ...patch };
    for (const listener of this.listeners) {
      listener(this.state);
    }
  }
}
