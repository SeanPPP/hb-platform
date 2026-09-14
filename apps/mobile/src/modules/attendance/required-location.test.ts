import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { collectOptionalLoginLocationAttempt } from "./optional-login-location";

type Deferred<T> = {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (error: unknown) => void;
};

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });
  return { promise, resolve, reject };
}

function helper() {
  return collectOptionalLoginLocationAttempt;
}

function position() {
  return {
    coords: {
      latitude: -27.4698,
      longitude: 153.0251,
      accuracy: 12,
    },
    timestamp: 1_735_000_000_000,
  };
}

test("可选定位挂起时有界返回，不阻塞账号密码登录", async () => {
  const collect = helper();
  const result = await collect({
    timeoutMs: 20,
    bridge: {
      getForegroundPermissionsAsync: async () => ({ status: "granted" }),
      getCurrentPositionAsync: async () => new Promise(() => undefined),
    },
  });

  assert.equal(result.location, null);
  assert.equal(result.error, null);
  assert.equal(result.timedOut, true);
});

test("前台定位权限未授予时不启动原生位置请求", async () => {
  const collect = helper();
  let positionCalls = 0;
  const result = await collect({
    timeoutMs: 20,
    bridge: {
      getForegroundPermissionsAsync: async () => ({ status: "denied" }),
      getCurrentPositionAsync: async () => {
        positionCalls += 1;
        return position();
      },
    },
  });

  assert.equal(result.location, null);
  assert.equal(result.timedOut, false);
  assert.equal(positionCalls, 0);
});

test("权限检查超过期限后不再启动原生位置请求", async () => {
  const collect = helper();
  const permission = deferred<{ status: string }>();
  let positionCalls = 0;
  const resultPromise = collect({
    timeoutMs: 20,
    bridge: {
      getForegroundPermissionsAsync: () => permission.promise,
      getCurrentPositionAsync: async () => {
        positionCalls += 1;
        return position();
      },
    },
  });

  const result = await resultPromise;
  permission.resolve({ status: "granted" });
  await Promise.resolve();

  assert.equal(result.location, null);
  assert.equal(result.timedOut, true);
  assert.equal(positionCalls, 0);
});

test("计时器尚未执行但单调时间已过期时也不启动定位", async () => {
  const collect = helper();
  const permission = deferred<{ status: string }>();
  let positionCalls = 0;
  const resultPromise = collect({
    timeoutMs: 20,
    bridge: {
      getForegroundPermissionsAsync: () => permission.promise,
      getCurrentPositionAsync: async () => {
        positionCalls += 1;
        return position();
      },
    },
  });

  // 让权限等待已经开始，再在权限解析后阻塞 JS 线程；此时 deadline 已过但 timer 回调尚未执行。
  await Promise.resolve();
  await Promise.resolve();
  permission.resolve({ status: "granted" });
  const blockedUntil = performance.now() + 50;
  while (performance.now() < blockedUntil) {
    // intentional synchronous block to exercise the timer/microtask race
  }

  const result = await resultPromise;
  assert.equal(positionCalls, 0);
  assert.equal(result.location, null);
  assert.equal(result.timedOut, true);
});

test("快速定位返回完整坐标", async () => {
  const collect = helper();
  let positionArguments: unknown[] | undefined;
  const result = await collect({
    timeoutMs: 100,
    bridge: {
      getForegroundPermissionsAsync: async () => ({ status: "granted" }),
      getCurrentPositionAsync: async (...args) => {
        positionArguments = args;
        return position();
      },
    },
  });

  assert.deepEqual(result.location, {
    locationLatitude: -27.4698,
    locationLongitude: 153.0251,
    locationAccuracy: 12,
    locationPermissionStatus: "granted",
    locationCapturedAtUtc: "2024-12-24T00:26:40.000Z",
  });
  assert.equal(result.error, null);
  assert.equal(result.timedOut, false);
  assert.deepEqual(positionArguments, []);
});

test("定位异常和超时后的迟到 reject 都被收敛", async () => {
  const collect = helper();
  const positionRequest = deferred<ReturnType<typeof position>>();
  let unhandled = false;
  const onUnhandled = () => {
    unhandled = true;
  };
  process.on("unhandledRejection", onUnhandled);

  const timedOut = await collect({
    timeoutMs: 20,
    bridge: {
      getForegroundPermissionsAsync: async () => ({ status: "granted" }),
      getCurrentPositionAsync: () => positionRequest.promise,
    },
  });
  positionRequest.reject(new Error("late native location failure"));
  await new Promise((resolve) => setImmediate(resolve));
  process.off("unhandledRejection", onUnhandled);

  assert.equal(timedOut.location, null);
  assert.equal(timedOut.timedOut, true);
  assert.equal(unhandled, false);

  const failed = await collect({
    timeoutMs: 100,
    bridge: {
      getForegroundPermissionsAsync: async () => ({ status: "granted" }),
      getCurrentPositionAsync: async () => {
        throw new Error("native location failed");
      },
    },
  });
  assert.equal(failed.location, null);
  assert.equal(failed.timedOut, false);
  assert.ok(failed.error instanceof Error);
});

test("超时后的迟到坐标不会回写可选采集结果", async () => {
  const collect = helper();
  const positionRequest = deferred<ReturnType<typeof position>>();
  const timedOut = await collect({
    timeoutMs: 20,
    bridge: {
      getForegroundPermissionsAsync: async () => ({ status: "granted" }),
      getCurrentPositionAsync: () => positionRequest.promise,
    },
  });

  positionRequest.resolve(position());
  await new Promise((resolve) => setImmediate(resolve));

  assert.equal(timedOut.location, null);
  assert.equal(timedOut.timedOut, true);
});

test("定位坐标转换异常也继续账号密码登录", async () => {
  const collect = helper();
  const result = await collect({
    timeoutMs: 100,
    bridge: {
      getForegroundPermissionsAsync: async () => ({ status: "granted" }),
      getCurrentPositionAsync: async () => ({
        ...position(),
        timestamp: Number.NaN,
      }),
    },
  });

  assert.equal(result.location, null);
  assert.equal(result.timedOut, false);
  assert.ok(result.error instanceof RangeError);
});

test("可选定位失败时保留登录所需设备上下文", () => {
  const source = readFileSync(
    join(dirname(fileURLToPath(import.meta.url)), "required-location.ts"),
    "utf8",
  );
  assert.match(source, /const \[context, locationResult\] = await Promise\.all/);
  assert.match(source, /return context;/);
  assert.match(
    source,
    /Location\.getCurrentPositionAsync\(\{ accuracy: Location\.Accuracy\.High \}\)/,
  );
});
