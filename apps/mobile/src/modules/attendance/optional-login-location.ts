export interface OptionalLoginLocationPayload {
  locationLatitude: number;
  locationLongitude: number;
  locationAccuracy?: number;
  locationPermissionStatus: "granted";
  locationCapturedAtUtc: string;
}

export interface OptionalLoginLocationBridge {
  getForegroundPermissionsAsync: () => Promise<{ status: string }>;
  getCurrentPositionAsync: () => Promise<{
    coords: {
      latitude: number;
      longitude: number;
      accuracy?: number | null;
    };
    timestamp: number;
  }>;
}

export interface OptionalLoginLocationAttempt {
  location: OptionalLoginLocationPayload | null;
  error: unknown | null;
  timedOut: boolean;
}

interface TimedTaskSuccess<T> {
  kind: "success";
  value: T;
}

interface TimedTaskTimeout {
  kind: "timeout";
}

interface TimedTaskFailure {
  kind: "failure";
  error: unknown;
}

type TimedTaskResult<T> =
  | TimedTaskSuccess<T>
  | TimedTaskTimeout
  | TimedTaskFailure;

function runWithDeadline<T>(
  task: (isActive: () => boolean) => Promise<T>,
  timeoutMs: number,
): Promise<TimedTaskResult<T>> {
  return new Promise((resolve) => {
    let settled = false;
    const budgetMs = Math.max(0, timeoutMs);
    const deadlineAt = performance.now() + budgetMs;
    let timer: ReturnType<typeof setTimeout>;
    const expire = () => {
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(timer);
      resolve({ kind: "timeout" });
    };
    const isActive = () => !settled && performance.now() < deadlineAt;
    timer = setTimeout(expire, budgetMs);

    // 无论超时前后都挂接 reject，避免原生定位迟到失败形成未处理 Promise。
    Promise.resolve()
      .then(() => task(isActive))
      .then(
        (value) => {
          if (settled) {
            return;
          }
          if (performance.now() >= deadlineAt) {
            expire();
            return;
          }
          settled = true;
          clearTimeout(timer);
          resolve({ kind: "success", value });
        },
        (error: unknown) => {
          if (settled) {
            return;
          }
          if (performance.now() >= deadlineAt) {
            expire();
            return;
          }
          settled = true;
          clearTimeout(timer);
          resolve({ kind: "failure", error });
        },
      );
  });
}

export interface OptionalLoginLocationOptions {
  bridge?: OptionalLoginLocationBridge;
  timeoutMs?: number;
}

const DEFAULT_OPTIONAL_LOGIN_LOCATION_TIMEOUT_MS = 800;

export async function collectOptionalLoginLocationAttempt({
  bridge,
  timeoutMs = DEFAULT_OPTIONAL_LOGIN_LOCATION_TIMEOUT_MS,
}: OptionalLoginLocationOptions = {}): Promise<OptionalLoginLocationAttempt> {
  if (!bridge) {
    throw new Error("OPTIONAL_LOGIN_LOCATION_BRIDGE_REQUIRED");
  }

  const locationResult = await runWithDeadline(async (isActive) => {
    const permission = await bridge.getForegroundPermissionsAsync();
    // 权限查询若已超过总期限，禁止迟到回调启动原生定位请求。
    if (!isActive() || permission.status !== "granted") {
      return null;
    }

    const position = await bridge.getCurrentPositionAsync();
    if (!isActive()) {
      return null;
    }

    const { coords, timestamp } = position;
    return {
      locationLatitude: coords.latitude,
      locationLongitude: coords.longitude,
      locationAccuracy: coords.accuracy ?? undefined,
      locationPermissionStatus: "granted" as const,
      locationCapturedAtUtc: new Date(timestamp).toISOString(),
    };
  }, Math.max(0, timeoutMs));

  if (locationResult.kind === "timeout") {
    return { location: null, error: null, timedOut: true };
  }
  if (locationResult.kind === "failure") {
    return { location: null, error: locationResult.error, timedOut: false };
  }
  if (!locationResult.value) {
    return { location: null, error: null, timedOut: false };
  }

  return {
    location: locationResult.value,
    error: null,
    timedOut: false,
  };
}
