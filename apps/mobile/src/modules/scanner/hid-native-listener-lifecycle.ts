import type { HidBarcodeKeyEvent } from "./hid-barcode-buffer";

export type HidNativeKeyListener = (event: HidBarcodeKeyEvent) => void;

export interface HidNativeListenerModule {
  addListener(
    eventName: "onKeyPress",
    listener: HidNativeKeyListener
  ): { remove?: () => void } | void;
  removeListener?(eventName: "onKeyPress", listener: HidNativeKeyListener): void;
  startListening(): void;
  stopListening(): void;
}

interface HidNativeListenerLeaseOptions {
  module: HidNativeListenerModule | null | undefined;
  enabled: boolean;
  nativeMode: boolean;
  onKeyPress: HidNativeKeyListener;
}

interface ModuleLeaseState {
  ownerCount: number;
}

const moduleLeaseStates = new WeakMap<HidNativeListenerModule, ModuleLeaseState>();
const noopLease = Object.freeze({
  release() {},
});

/**
 * 仅让启用中的 native owner 订阅事件，并用模块级租约保护共享原生监听。
 */
export function acquireHidNativeListenerLease({
  module,
  enabled,
  nativeMode,
  onKeyPress,
}: HidNativeListenerLeaseOptions) {
  if (!module || !enabled || !nativeMode) {
    return noopLease;
  }

  let state = moduleLeaseStates.get(module);
  if (!state) {
    state = { ownerCount: 0 };
    moduleLeaseStates.set(module, state);
  }

  const isFirstOwner = state.ownerCount === 0;
  let subscription: { remove?: () => void } | void = undefined;
  let listenerAdded = false;
  let ownerAcquired = false;
  try {
    subscription = module.addListener("onKeyPress", onKeyPress);
    listenerAdded = true;
    state.ownerCount += 1;
    ownerAcquired = true;
    if (isFirstOwner) {
      module.startListening();
    }
  } catch (error) {
    try {
      if (listenerAdded && subscription?.remove) {
        subscription.remove();
      } else if (listenerAdded) {
        module.removeListener?.("onKeyPress", onKeyPress);
      }
    } catch {
      // 原生订阅回滚属于 best effort，保留原始 acquire 错误。
    } finally {
      if (ownerAcquired) {
        state.ownerCount -= 1;
      }
      if (state.ownerCount === 0) {
        moduleLeaseStates.delete(module);
      }
    }
    throw error;
  }

  let released = false;
  return {
    release() {
      // React effect cleanup 可能重复执行；同一 owner 只能释放一次租约。
      if (released) {
        return;
      }
      released = true;

      try {
        if (subscription?.remove) {
          subscription.remove();
        } else {
          module.removeListener?.("onKeyPress", onKeyPress);
        }
      } catch {
        // 原生 listener 清理属于 best effort，不能让 React effect cleanup 抛错。
      } finally {
        const currentState = moduleLeaseStates.get(module);
        if (!currentState) {
          return;
        }

        currentState.ownerCount -= 1;
        if (currentState.ownerCount <= 0) {
          moduleLeaseStates.delete(module);
          // 只有最后一个 owner 释放时才能停止共享原生监听。
          try {
            module.stopListening();
          } catch {
            // 原生清理属于 best effort，不能让 React effect cleanup 抛错。
          }
        }
      }
    },
  };
}
