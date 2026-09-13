export interface AttendanceLocationFix {
  timestamp: number;
  coords: { latitude: number; longitude: number; accuracy?: number | null };
}

interface LocationSubscription { remove(): void }

// 只保留当前扫码会话内的实时定位；关闭、超时及迟到回调均不能把旧坐标带到下一次打卡。
export function createAttendanceLocationCapture(options: {
  watch: (
    onPosition: (position: AttendanceLocationFix) => void,
    onError: (error: unknown) => void,
    isActive: () => boolean,
  ) => Promise<LocationSubscription>;
  now?: () => number;
  timeoutMs?: number;
}) {
  const now = options.now ?? Date.now;
  const startedAt = now();
  const timeoutMs = options.timeoutMs ?? 10_000;
  let latest: AttendanceLocationFix | undefined;
  let started = false;
  let terminalError: unknown;
  let subscription: LocationSubscription | undefined;
  const waiters = new Set<{
    resolve: (position: AttendanceLocationFix) => void;
    reject: (error: unknown) => void;
    timer: ReturnType<typeof setTimeout>;
  }>();

  const stop = () => {
    const current = subscription;
    subscription = undefined;
    current?.remove();
  };
  const fail = (error: unknown) => {
    if (terminalError) return;
    terminalError = error;
    latest = undefined;
    stop();
    for (const waiter of waiters) {
      clearTimeout(waiter.timer);
      waiter.reject(error);
    }
    waiters.clear();
  };
  const fresh = (position: AttendanceLocationFix) => {
    const age = now() - position.timestamp;
    return Number.isFinite(position.timestamp)
      && position.timestamp >= startedAt
      && age >= 0 && age <= 10_000
      && Number.isFinite(position.coords.latitude) && Math.abs(position.coords.latitude) <= 90
      && Number.isFinite(position.coords.longitude) && Math.abs(position.coords.longitude) <= 180;
  };
  const prewarm = () => {
    if (started || terminalError) return;
    started = true;
    try {
      void options.watch((position) => {
        if (terminalError || !fresh(position)) return;
        latest = position;
        for (const waiter of waiters) {
          clearTimeout(waiter.timer);
          waiter.resolve(position);
        }
        waiters.clear();
      }, fail, () => !terminalError).then((value) => {
        // 原生注册可能晚于页面关闭或超时，此时立即注销，不能遗留前台定位订阅。
        if (terminalError) value.remove();
        else subscription = value;
      }, fail);
    } catch (error) {
      fail(error);
    }
  };
  return {
    prewarm,
    capture(): Promise<AttendanceLocationFix> {
      if (terminalError) return Promise.reject(terminalError);
      if (latest && fresh(latest)) return Promise.resolve(latest);
      const pending = new Promise<AttendanceLocationFix>((resolve, reject) => {
        const timer = setTimeout(() => fail(Object.assign(
          new Error("LOCATION_TIMEOUT"), { code: "LOCATION_TIMEOUT" },
        )), timeoutMs);
        waiters.add({ resolve, reject, timer });
      });
      prewarm();
      return pending;
    },
    dispose() {
      fail(Object.assign(new Error("LOCATION_CANCELLED"), { code: "LOCATION_CANCELLED" }));
    },
  };
}
