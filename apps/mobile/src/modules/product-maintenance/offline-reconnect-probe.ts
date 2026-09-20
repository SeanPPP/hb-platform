/**
 * 离线态的快速重连探测（纯逻辑，可注入定时器与探测函数）。
 *
 * 网络恢复控制器采用 5s→60s 指数退避，对「检测到在线立刻退出离线」太慢；
 * 因此离线期间另起固定间隔探测：启动立即探测一次，之后每 intervalMs 一次，
 * 任何时候都可 `probeNow()` 插入一次即时探测（不重置固定节奏）。探测单飞，
 * 首次成功即回调 `onReachable` 并自动停止。
 */
export interface OfflineReconnectProbeOptions {
  /** 探测后端是否可达；实现应自带超时并且永不抛错。 */
  checkBackend: () => Promise<boolean>;
  /** 探测成功回调（只会触发一次，随后探测器自动停止）。 */
  onReachable: (checkedAtMs: number) => void;
  intervalMs?: number;
  now?: () => number;
  schedule?: (fn: () => void, delayMs: number) => { cancel(): void };
}

export interface OfflineReconnectProbe {
  start(): void;
  stop(): void;
  /** 立即探测一次；正在探测时不叠加。 */
  probeNow(): Promise<void>;
  readonly running: boolean;
}

export const OFFLINE_RECONNECT_PROBE_INTERVAL_MS = 5_000;
export const OFFLINE_RECONNECT_PROBE_TIMEOUT_MS = 3_000;

export function createOfflineReconnectProbe(
  options: OfflineReconnectProbeOptions,
): OfflineReconnectProbe {
  const intervalMs = options.intervalMs ?? OFFLINE_RECONNECT_PROBE_INTERVAL_MS;
  const now = options.now ?? (() => Date.now());
  const schedule =
    options.schedule ??
    ((fn, delayMs) => {
      const id = setTimeout(fn, delayMs);
      return { cancel: () => clearTimeout(id) };
    });

  let running = false;
  let inFlight: Promise<void> | null = null;
  let timer: { cancel(): void } | null = null;
  // 每次 start 递增代次；stop 后迟到的探测结果不得再触发回调。
  let generation = 0;

  const scheduleNext = (currentGeneration: number) => {
    timer?.cancel();
    timer = schedule(() => {
      timer = null;
      if (!running || generation !== currentGeneration) {
        return;
      }
      void probe(currentGeneration).finally(() => {
        if (running && generation === currentGeneration) {
          scheduleNext(currentGeneration);
        }
      });
    }, intervalMs);
  };

  const probe = (currentGeneration: number): Promise<void> => {
    if (inFlight) {
      return inFlight;
    }
    inFlight = (async () => {
      let reachable = false;
      try {
        reachable = await options.checkBackend();
      } catch {
        reachable = false;
      }
      if (!running || generation !== currentGeneration) {
        return;
      }
      if (reachable) {
        const checkedAtMs = now();
        stop();
        options.onReachable(checkedAtMs);
      }
    })().finally(() => {
      inFlight = null;
    });
    return inFlight;
  };

  const start = () => {
    if (running) {
      return;
    }
    running = true;
    generation += 1;
    const currentGeneration = generation;
    // 进入离线态立即探测一次，再进入固定节奏。
    void probe(currentGeneration).finally(() => {
      if (running && generation === currentGeneration && !timer) {
        scheduleNext(currentGeneration);
      }
    });
  };

  const stop = () => {
    running = false;
    generation += 1;
    timer?.cancel();
    timer = null;
  };

  return {
    start,
    stop,
    probeNow: () => (running ? probe(generation) : Promise.resolve()),
    get running() {
      return running;
    },
  };
}
