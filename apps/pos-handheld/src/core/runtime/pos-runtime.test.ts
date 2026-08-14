import assert from "node:assert/strict";
import test from "node:test";

import {
  PosRuntimeController,
  type PosRuntimeServices,
} from "./pos-runtime";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });
  return { promise, resolve, reject };
}

test("runtime does not report offline or backend readiness before real initialization", async () => {
  const pending = deferred<PosRuntimeServices>();
  const controller = new PosRuntimeController(() => pending.promise);

  const start = controller.start();

  assert.deepEqual(controller.getState(), {
    phase: "starting",
    database: "opening",
    backend: "unverified",
    device: "unknown",
  });

  pending.resolve({
    shutdown: async () => undefined,
    backend: "unverified",
    device: "registration-required",
  });
  await start;

  assert.deepEqual(controller.getState(), {
    phase: "registration-required",
    database: "ready",
    backend: "unverified",
    device: "registration-required",
  });
});

test("runtime initialization is single-flight and closes the database exactly once", async () => {
  let creates = 0;
  let closes = 0;
  const controller = new PosRuntimeController(async () => {
    creates += 1;
    return {
      shutdown: async () => {
        closes += 1;
      },
      backend: "offline",
      device: "authorized-local",
    };
  });

  await Promise.all([controller.start(), controller.start(), controller.start()]);
  assert.equal(creates, 1);
  assert.equal(controller.getState().phase, "ready-offline");

  await Promise.all([controller.stop(), controller.stop()]);
  assert.equal(closes, 1);
  assert.equal(controller.getState().phase, "idle");
});

test("runtime failure keeps the app in an explicit non-transactional state", async () => {
  const controller = new PosRuntimeController(async () => {
    throw new Error("SQLCipher key rejected");
  });

  await assert.rejects(controller.start(), /SQLCipher key rejected/);
  assert.deepEqual(controller.getState(), {
    phase: "failed",
    database: "failed",
    backend: "unverified",
    device: "unknown",
    error: "SQLCipher key rejected",
  });
});

test("runtime reconciles online device approval without reopening SQLCipher", async () => {
  let creates = 0;
  const controller = new PosRuntimeController(async () => {
    creates += 1;
    return {
      shutdown: async () => undefined,
      backend: "unverified",
      device: "registration-required",
    };
  });
  await controller.start();

  controller.updateOperationalState({
    backend: "reachable",
    device: "authorized-online",
  });

  assert.equal(creates, 1);
  assert.deepEqual(controller.getState(), {
    phase: "ready",
    database: "ready",
    backend: "reachable",
    device: "authorized-online",
  });
});
