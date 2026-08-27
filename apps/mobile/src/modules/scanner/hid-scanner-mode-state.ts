export type HidScannerModeListener = (forceTextInput: boolean) => void;

export interface HidScannerModeStateController {
  get(): boolean | null;
  set(nextForceTextInput: boolean): void;
  setIfUnset(nextForceTextInput: boolean): boolean;
  subscribe(listener: HidScannerModeListener): () => void;
}

export function createHidScannerModeState(): HidScannerModeStateController {
  let forceTextInputState: boolean | null = null;
  const modeListeners = new Set<HidScannerModeListener>();

  const set = (nextForceTextInput: boolean) => {
    if (forceTextInputState === nextForceTextInput) {
      return;
    }

    forceTextInputState = nextForceTextInput;
    // 使用快照通知，避免某个订阅者在回调中退订影响其他已挂载 owner。
    for (const listener of [...modeListeners]) {
      listener(nextForceTextInput);
    }
  };

  return {
    get() {
      return forceTextInputState;
    },
    set,
    setIfUnset(nextForceTextInput: boolean) {
      if (forceTextInputState !== null) {
        return false;
      }
      set(nextForceTextInput);
      return true;
    },
    subscribe(listener: HidScannerModeListener) {
      modeListeners.add(listener);

      if (forceTextInputState !== null) {
        listener(forceTextInputState);
      }

      return () => {
        modeListeners.delete(listener);
      };
    },
  };
}

const defaultHidScannerModeState = createHidScannerModeState();

export function getHidScannerForceTextInput() {
  return defaultHidScannerModeState.get();
}

export function setHidScannerForceTextInput(nextForceTextInput: boolean) {
  defaultHidScannerModeState.set(nextForceTextInput);
}

export function setHidScannerForceTextInputIfUnset(nextForceTextInput: boolean) {
  return defaultHidScannerModeState.setIfUnset(nextForceTextInput);
}

export function subscribeHidScannerMode(listener: HidScannerModeListener) {
  return defaultHidScannerModeState.subscribe(listener);
}
