import assert from "node:assert/strict";
import {
  acquireHidNativeListenerLease,
  type HidNativeListenerModule,
} from "./hid-native-listener-lifecycle";

function createNativeModule() {
  const calls: string[] = [];
  const listeners = new Set<Parameters<HidNativeListenerModule["addListener"]>[1]>();
  let throwOnAdd = false;
  let throwOnRemove = false;
  let throwOnStop = false;
  let returnSubscription = false;

  const module: HidNativeListenerModule = {
    addListener(_eventName, listener) {
      if (throwOnAdd) {
        throw new Error("add failed");
      }
      calls.push("addListener");
      listeners.add(listener);
      if (returnSubscription) {
        return {
          remove() {
            calls.push("subscription.remove");
            listeners.delete(listener);
            if (throwOnRemove) {
              throw new Error("subscription remove failed");
            }
          },
        };
      }
    },
    removeListener(_eventName, listener) {
      calls.push("removeListener");
      listeners.delete(listener);
      if (throwOnRemove) {
        throw new Error("remove failed");
      }
    },
    startListening() {
      calls.push("startListening");
    },
    stopListening() {
      calls.push("stopListening");
      if (throwOnStop) {
        throw new Error("stop failed");
      }
    },
  };

  return {
    module,
    calls,
    listeners,
    failNextAdd() {
      throwOnAdd = true;
    },
    failRemove() {
      throwOnRemove = true;
    },
    enableSubscriptionReturn() {
      returnSubscription = true;
    },
    failStop() {
      throwOnStop = true;
    },
  };
}

{
  const { module, calls, listeners, failNextAdd } = createNativeModule();
  const firstLease = acquireHidNativeListenerLease({
    module,
    enabled: true,
    nativeMode: true,
    onKeyPress: () => {},
  });

  failNextAdd();
  assert.throws(
    () =>
      acquireHidNativeListenerLease({
        module,
        enabled: true,
        nativeMode: true,
        onKeyPress: () => {},
      }),
    /add failed/,
    "第二个 owner 订阅失败时应向调用方报告错误"
  );
  assert.equal(listeners.size, 1, "第二个 owner 订阅失败不得泄漏或移除已有 owner");
  assert.deepEqual(
    calls,
    ["addListener", "startListening"],
    "第二个 owner 订阅失败不得停止已有原生监听"
  );

  firstLease.release();
  assert.deepEqual(
    calls,
    ["addListener", "startListening", "removeListener", "stopListening"],
    "已有 owner 最终释放时仍应正常停止原生监听"
  );
}

{
  const { module, calls, failStop } = createNativeModule();
  const lease = acquireHidNativeListenerLease({
    module,
    enabled: true,
    nativeMode: true,
    onKeyPress: () => {},
  });
  failStop();

  assert.doesNotThrow(
    () => lease.release(),
    "原生 stopListening 清理失败不能让 React cleanup 抛错"
  );
  assert.deepEqual(calls, ["addListener", "startListening", "removeListener", "stopListening"]);
}

{
  const { module, calls, failRemove } = createNativeModule();
  const firstLease = acquireHidNativeListenerLease({
    module,
    enabled: true,
    nativeMode: true,
    onKeyPress: () => {},
  });
  const secondLease = acquireHidNativeListenerLease({
    module,
    enabled: true,
    nativeMode: true,
    onKeyPress: () => {},
  });
  failRemove();

  assert.doesNotThrow(
    () => firstLease.release(),
    "removeListener 清理失败不能让非最后 owner 的 React cleanup 抛错"
  );
  assert.deepEqual(
    calls,
    ["addListener", "startListening", "addListener", "removeListener"],
    "非最后 owner 的 removeListener 失败不得提前 stop"
  );

  assert.doesNotThrow(
    () => secondLease.release(),
    "最后 owner 的 removeListener 清理失败也不能让 React cleanup 抛错"
  );
  assert.deepEqual(
    calls,
    ["addListener", "startListening", "addListener", "removeListener", "removeListener", "stopListening"],
    "removeListener 失败时仍应减少 owner 并在最后一个 owner 释放时 stop"
  );
}

{
  const { module, calls, listeners, failRemove, enableSubscriptionReturn } = createNativeModule();
  enableSubscriptionReturn();
  const firstLease = acquireHidNativeListenerLease({
    module,
    enabled: true,
    nativeMode: true,
    onKeyPress: () => {},
  });
  const secondLease = acquireHidNativeListenerLease({
    module,
    enabled: true,
    nativeMode: true,
    onKeyPress: () => {},
  });
  failRemove();

  assert.doesNotThrow(
    () => firstLease.release(),
    "subscription.remove 清理失败不能让非最后 owner 的 React cleanup 抛错"
  );
  assert.equal(listeners.size, 1, "subscription.remove 失败时另一个 owner 仍应保留");
  assert.deepEqual(
    calls,
    ["addListener", "startListening", "addListener", "subscription.remove"],
    "subscription.remove 失败不得提前 stop"
  );

  assert.doesNotThrow(
    () => secondLease.release(),
    "最后 owner 的 subscription.remove 失败也不能让 React cleanup 抛错"
  );
  assert.equal(listeners.size, 0, "两个 subscription.remove 执行后不应残留 listener");
  assert.deepEqual(
    calls,
    [
      "addListener",
      "startListening",
      "addListener",
      "subscription.remove",
      "subscription.remove",
      "stopListening",
    ],
    "subscription.remove 失败时仍应减少 owner 并在最后一个 owner 释放时 stop"
  );
}

{
  const { module, calls, listeners } = createNativeModule();
  const firstListener = () => {};
  const secondListener = () => {};

  const firstLease = acquireHidNativeListenerLease({
    module,
    enabled: true,
    nativeMode: true,
    onKeyPress: firstListener,
  });
  const secondLease = acquireHidNativeListenerLease({
    module,
    enabled: true,
    nativeMode: true,
    onKeyPress: secondListener,
  });

  assert.deepEqual(
    calls,
    ["addListener", "startListening", "addListener"],
    "两个 native owner 只启动一次原生监听"
  );
  assert.equal(listeners.size, 2, "两个 owner 都应订阅 onKeyPress");

  firstLease.release();
  assert.deepEqual(
    calls,
    ["addListener", "startListening", "addListener", "removeListener"],
    "释放一个 owner 不能停止仍被另一个 owner 使用的监听"
  );
  assert.equal(listeners.size, 1, "释放一个 owner 后另一个订阅仍保留");

  firstLease.release();
  assert.deepEqual(
    calls,
    ["addListener", "startListening", "addListener", "removeListener"],
    "重复 release 必须幂等，不得重复移除或减少租约"
  );

  secondLease.release();
  assert.deepEqual(
    calls,
    ["addListener", "startListening", "addListener", "removeListener", "removeListener", "stopListening"],
    "最后一个 owner 释放时才停止原生监听"
  );
  assert.equal(listeners.size, 0, "最后一个 owner 释放后不应残留订阅");

  secondLease.release();
  assert.deepEqual(
    calls,
    ["addListener", "startListening", "addListener", "removeListener", "removeListener", "stopListening"],
    "最后一个 lease 重复 release 也必须幂等"
  );
}

{
  const { module, calls } = createNativeModule();

  const disabledLease = acquireHidNativeListenerLease({
    module,
    enabled: false,
    nativeMode: true,
    onKeyPress: () => {},
  });
  const textInputLease = acquireHidNativeListenerLease({
    module,
    enabled: true,
    nativeMode: false,
    onKeyPress: () => {},
  });

  disabledLease.release();
  textInputLease.release();
  assert.deepEqual(
    calls,
    [],
    "disabled 或 TextInput fallback 实例不得订阅 native onKeyPress 或启动原生监听"
  );
}

console.log("hid-native-listener-lifecycle.test.ts: ok");
