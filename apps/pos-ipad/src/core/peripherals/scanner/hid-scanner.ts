import type { ScanEvent, ScannerContext, ScannerPort } from "../../contracts/scanner";

import type {
  RoutedScanEvent,
  ScannerCaptureContext,
  ScannerCaptureStatus,
  ScannerScanCategory,
  TextInputCaptureResult,
} from "./types";

export type HidScannerRouterOptions = {
  idleMs?: number;
  maxLength?: number;
  now?: () => number;
  onDiagnostic?: (diagnostic: "idle-reset" | "max-length") => void;
};

const DEFAULT_IDLE_MS = 80;
const DEFAULT_MAX_LENGTH = 256;

/**
 * iPad 只可在当前 TextInput 焦点接收 HID 键盘输入。本路由器不尝试安装全局键盘 hook，
 * 上下文、弹窗和失焦都会清空半段数据，避免条码跨界拼接。
 */
export class HidScannerRouter implements ScannerPort {
  private readonly idleMs: number;
  private readonly maxLength: number;
  private readonly now: () => number;
  private readonly onDiagnostic?: HidScannerRouterOptions["onDiagnostic"];
  private readonly scannerListeners = new Set<(event: ScanEvent) => void>();
  private readonly routedListeners = new Set<(event: RoutedScanEvent) => void>();
  private readonly contextStack: {
    context: ScannerCaptureContext;
    leaseId: number | null;
  }[] = [{ context: "product", leaseId: null }];
  private nextContextLeaseId = 1;
  private buffer = "";
  private textInputValue = "";
  private lastInputAt: number | null = null;
  private captureActive = false;
  private cameraActive = false;

  constructor(options: HidScannerRouterOptions = {}) {
    this.idleMs = options.idleMs ?? DEFAULT_IDLE_MS;
    this.maxLength = options.maxLength ?? DEFAULT_MAX_LENGTH;
    this.now = options.now ?? Date.now;
    this.onDiagnostic = options.onDiagnostic;
  }

  setContext(context: ScannerContext): void {
    this.replaceContext(context);
  }

  setCaptureContext(context: ScannerCaptureContext): void {
    this.replaceContext(context);
  }

  pushContext(context: ScannerCaptureContext): void {
    this.resetPartial();
    this.contextStack.push({ context, leaseId: null });
  }

  popContext(): void {
    if (this.contextStack.length > 1) {
      this.contextStack.pop();
    }
    this.resetPartial();
  }

  /**
   * 路由和全局弹窗的 effect 清理顺序并不保证后进先出。
   * lease 按自身身份释放，避免页面卸载时误弹出仍在显示的授权弹窗 context。
   */
  acquireContext(context: ScannerCaptureContext): () => void {
    const leaseId = this.nextContextLeaseId;
    this.nextContextLeaseId += 1;
    this.resetPartial();
    this.contextStack.push({ context, leaseId });
    let released = false;
    return () => {
      if (released) return;
      released = true;
      const index = this.contextStack.findIndex(
        (entry) => entry.leaseId === leaseId,
      );
      if (index >= 0) {
        this.contextStack.splice(index, 1);
        this.resetPartial();
      }
    };
  }

  setCaptureActive(active: boolean): void {
    if (this.captureActive === active) {
      return;
    }
    this.captureActive = active;
    this.resetPartial();
  }

  getCaptureStatus(): ScannerCaptureStatus {
    if (!this.captureActive) {
      return "inactive";
    }
    return this.cameraActive ? "camera" : "capturing";
  }

  acceptHidText(fragment: string, receivedAt = this.now()): TextInputCaptureResult {
    if (!this.captureActive) {
      return { valueToRender: "", emitted: false, overflowed: false };
    }
    this.resetIfStale(receivedAt);
    return this.appendFragment(fragment, "hid", receivedAt);
  }

  acceptTextInputValue(nextValue: string, receivedAt = this.now()): TextInputCaptureResult {
    if (!this.captureActive) {
      this.textInputValue = "";
      return { valueToRender: "", emitted: false, overflowed: false };
    }
    this.resetIfStale(receivedAt);
    const fragment = nextValue.startsWith(this.textInputValue)
      ? nextValue.slice(this.textInputValue.length)
      : nextValue;
    this.textInputValue = nextValue;
    const result = this.appendFragment(fragment, "hid", receivedAt);
    if (result.emitted || result.overflowed) {
      this.textInputValue = "";
    }
    return { ...result, valueToRender: this.textInputValue };
  }

  submitTextInput(value: string, receivedAt = this.now()): TextInputCaptureResult {
    const result = this.acceptTextInputValue(value, receivedAt);
    if (!this.captureActive || result.emitted || result.overflowed) {
      return result;
    }
    const emitted = this.flush("hid", receivedAt);
    this.textInputValue = "";
    return { valueToRender: "", emitted, overflowed: false };
  }

  resetPartialIfIdle(receivedAt = this.now()): boolean {
    if (!this.buffer || this.lastInputAt === null || receivedAt - this.lastInputAt <= this.idleMs) {
      return false;
    }
    this.resetPartial();
    this.onDiagnostic?.("idle-reset");
    return true;
  }

  async startCamera(): Promise<void> {
    this.cameraActive = true;
    this.resetPartial();
  }

  async stopCamera(): Promise<void> {
    this.cameraActive = false;
  }

  acceptCameraText(value: string, receivedAt = this.now()): boolean {
    if (!this.cameraActive) {
      return false;
    }
    const normalized = normalizeScanValue(value);
    if (!normalized) {
      return false;
    }
    return this.emit(normalized, "camera", receivedAt);
  }

  subscribe(listener: (event: ScanEvent) => void): () => void {
    this.scannerListeners.add(listener);
    return () => this.scannerListeners.delete(listener);
  }

  subscribeRouted(listener: (event: RoutedScanEvent) => void): () => void {
    this.routedListeners.add(listener);
    return () => this.routedListeners.delete(listener);
  }

  private replaceContext(context: ScannerCaptureContext): void {
    this.contextStack.splice(0, this.contextStack.length, {
      context,
      leaseId: null,
    });
    this.resetPartial();
  }

  private appendFragment(
    fragment: string,
    source: "hid",
    receivedAt: number,
  ): TextInputCaptureResult {
    let emitted = false;
    let overflowed = false;
    for (const character of fragment) {
      if (isTerminator(character)) {
        emitted = this.flush(source, receivedAt) || emitted;
        continue;
      }
      if (!isPrintable(character)) {
        continue;
      }
      if (this.buffer.length >= this.maxLength) {
        // 本片段余下内容一概丢弃；下次输入从全新缓冲开始，禁止残码污染。
        this.resetPartial();
        this.onDiagnostic?.("max-length");
        overflowed = true;
        break;
      }
      this.buffer += character;
      this.lastInputAt = receivedAt;
    }
    return { valueToRender: this.textInputValue, emitted, overflowed };
  }

  private flush(source: "hid", receivedAt: number): boolean {
    const value = normalizeScanValue(this.buffer);
    this.resetPartial();
    if (!value) {
      return false;
    }
    return this.emit(value, source, receivedAt);
  }

  private resetIfStale(receivedAt: number): void {
    this.resetPartialIfIdle(receivedAt);
  }

  private resetPartial(): void {
    this.buffer = "";
    this.textInputValue = "";
    this.lastInputAt = null;
  }

  private emit(value: string, source: "hid" | "camera", receivedAt: number): boolean {
    const context = this.currentContext();
    if (context === "device-activation") {
      // 开通码只交给相机弹窗的直接回调或当前输入框，禁止进入任何共享扫码订阅。
      return true;
    }
    if (hasDeviceActivationPrefix(value)) {
      // 即使未进入专用上下文，也绝不能把疑似设备秘密当商品或员工码广播。
      return false;
    }
    const event: RoutedScanEvent = {
      value,
      source,
      context,
      category: categoryForContext(context),
      receivedAtIso: new Date(receivedAt).toISOString(),
    };
    this.routedListeners.forEach((listener) => listener(event));
    if (context !== "emergency-qr") {
      const scannerEvent: ScanEvent = {
        value: event.value,
        source: event.source,
        context,
        receivedAtIso: event.receivedAtIso,
      };
      this.scannerListeners.forEach((listener) => listener(scannerEvent));
    }
    return true;
  }

  private currentContext(): ScannerCaptureContext {
    return this.contextStack.at(-1)?.context ?? "product";
  }
}

export function normalizeScanValue(value: string): string {
  return value.replace(/[\r\n]/g, "").trim();
}

function hasDeviceActivationPrefix(value: string): boolean {
  const expected = "HBDEV1-";
  let matched = 0;
  for (const character of value) {
    const code = character.charCodeAt(0);
    if (code > 0x7f) return false;
    if (code === 0x20 || (code >= 0x09 && code <= 0x0d)) {
      continue;
    }
    const normalized =
      code >= 0x61 && code <= 0x7a
        ? String.fromCharCode(code - 0x20)
        : character;
    if (normalized !== expected[matched]) return false;
    matched += 1;
    if (matched === expected.length) return true;
  }
  return false;
}

function isTerminator(character: string): boolean {
  return character === "\r" || character === "\n" || character === "\t";
}

function isPrintable(character: string): boolean {
  return character >= " " && character !== "\u007f";
}

function categoryForContext(context: ScannerCaptureContext): ScannerScanCategory {
  switch (context) {
    case "cashier-login":
      return "cashier-code";
    case "supervisor-authorization":
      return "supervisor-code";
    case "dialog":
      return "dialog-code";
    case "emergency-qr":
      return "emergency-qr";
    case "device-activation":
      return "device-activation";
    case "product":
    case "product-search":
      return "product-code";
  }
}
