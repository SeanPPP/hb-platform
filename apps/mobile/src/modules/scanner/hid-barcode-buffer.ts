export interface HidBarcodeKeyEvent {
  key?: string | null;
  character?: string | null;
}

interface HidBarcodeKeyBufferOptions {
  idleMs: number;
  minLength: number;
  onBarcode: (barcode: string) => void | Promise<void>;
  onFallbackToTextInput?: () => void;
  now?: () => number;
  setTimeoutFn?: typeof setTimeout;
  clearTimeoutFn?: typeof clearTimeout;
}

const DUPLICATE_SUPPRESS_MS = 100;
const END_KEYS = new Set(["Enter", "NumpadEnter", "Tab"]);

export function normalizeHidBarcode(rawValue: string) {
  return rawValue.replace(/[\r\n\t]/g, "").trim();
}

export function extractNewHidBarcodeSegment(rawValue: string, lastSubmittedRawValue?: string | null) {
  const barcode = normalizeHidBarcode(rawValue);
  const submittedPrefix = normalizeHidBarcode(lastSubmittedRawValue ?? "");
  if (
    !barcode ||
    !submittedPrefix ||
    barcode.length <= submittedPrefix.length ||
    !barcode.startsWith(submittedPrefix)
  ) {
    return barcode;
  }

  const tail = normalizeHidBarcode(barcode.slice(submittedPrefix.length));
  if (tail.length < submittedPrefix.length) {
    return barcode;
  }

  // 隐藏 TextInput 在部分 TC26 上会把下一次扫码追加到旧值后面；只提交新增尾段。
  return tail;
}

export function isHidNativeTextInputFallbackEvent(event: HidBarcodeKeyEvent) {
  const key = event.key ?? "";
  const character = event.character ?? "";
  return /^\d+$/.test(key) && !character;
}

function isPrintableScannerCharacter(character: string) {
  return Boolean(character) && !/[\r\n\t]/.test(character);
}

export function createHidBarcodeKeyBuffer({
  idleMs,
  minLength,
  onBarcode,
  onFallbackToTextInput,
  now = () => Date.now(),
  setTimeoutFn = setTimeout,
  clearTimeoutFn = clearTimeout,
}: HidBarcodeKeyBufferOptions) {
  let buffer = "";
  let idleTimer: ReturnType<typeof setTimeout> | null = null;
  let lastSubmitted = "";
  let lastSubmittedAt = 0;

  const clearIdleTimer = () => {
    if (!idleTimer) {
      return;
    }
    clearTimeoutFn(idleTimer);
    idleTimer = null;
  };

  const submit = (rawValue: string) => {
    const barcode = normalizeHidBarcode(rawValue);
    if (!barcode || barcode.length < minLength) {
      return;
    }

    const submittedAt = now();
    if (barcode === lastSubmitted && submittedAt - lastSubmittedAt < DUPLICATE_SUPPRESS_MS) {
      return;
    }

    lastSubmitted = barcode;
    lastSubmittedAt = submittedAt;
    void onBarcode(barcode);
  };

  const flush = () => {
    clearIdleTimer();
    const nextValue = buffer;
    buffer = "";
    submit(nextValue);
  };

  const scheduleIdleFlush = () => {
    clearIdleTimer();
    // HID 扫码枪通常会快速连续输入；没有 Enter/Tab 后缀时，用短暂停顿作为一次扫码结束。
    idleTimer = setTimeoutFn(flush, idleMs);
  };

  return {
    handleKeyPress(event: HidBarcodeKeyEvent) {
      const key = event.key ?? "";
      const character = event.character ?? "";

      if (END_KEYS.has(key)) {
        flush();
        return;
      }

      if (isHidNativeTextInputFallbackEvent(event)) {
        clearIdleTimer();
        buffer = "";
        onFallbackToTextInput?.();
        return;
      }

      if (!isPrintableScannerCharacter(character)) {
        return;
      }

      buffer += character;
      scheduleIdleFlush();
    },
    flush,
    dispose() {
      clearIdleTimer();
      buffer = "";
      lastSubmitted = "";
      lastSubmittedAt = 0;
    },
  };
}
