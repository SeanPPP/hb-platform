import type { SettingsScannerTestResult } from "../../features/settings/settings-presenter";
import type { HidScannerRouter } from "../peripherals/scanner/hid-scanner";

const DEFAULT_TIMEOUT_MS = 30_000;

/**
 * 设置页只等待下一次 `dialog` 上下文扫码。监听器、Abort 处理器和超时器在所有
 * 终态都会同步释放，避免换页后继续截获商品码或员工码。
 */
export class SettingsScannerTestCoordinator {
  private pending = false;
  private readonly timeoutMs: number;

  public constructor(
    public readonly scanner: HidScannerRouter,
    options: Readonly<{ timeoutMs?: number }> = {},
  ) {
    this.timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  }

  public hasPendingTest(): boolean {
    return this.pending;
  }

  public test(signal: AbortSignal): Promise<SettingsScannerTestResult> {
    if (this.pending) {
      return Promise.reject(
        new Error("Scanner test is already in progress."),
      );
    }
    if (signal.aborted) {
      return Promise.reject(abortError());
    }
    this.pending = true;

    return new Promise<SettingsScannerTestResult>((resolve, reject) => {
      let settled = false;
      const finish = (
        outcome:
          | Readonly<{
              ok: true;
              value: SettingsScannerTestResult;
            }>
          | Readonly<{ ok: false; error: Error }>,
      ) => {
        if (settled) return;
        settled = true;
        clearTimeout(timeout);
        unsubscribe();
        signal.removeEventListener("abort", onAbort);
        this.pending = false;
        if (outcome.ok) resolve(outcome.value);
        else reject(outcome.error);
      };
      const unsubscribe = this.scanner.subscribeRouted((event) => {
        if (event.context !== "dialog") return;
        finish({
          ok: true,
          value: Object.freeze({
            source: event.source,
            value: event.value,
          }),
        });
      });
      const onAbort = () => {
        finish({ ok: false, error: abortError() });
      };
      const timeout = setTimeout(() => {
        finish({
          ok: false,
          error: new Error("Scanner test timed out."),
        });
      }, boundedTimeout(this.timeoutMs));
      signal.addEventListener("abort", onAbort, { once: true });
    });
  }
}

function boundedTimeout(value: number): number {
  if (!Number.isSafeInteger(value) || value < 100 || value > 60_000) {
    throw new Error("Scanner test timeout is invalid.");
  }
  return value;
}

function abortError(): Error {
  return Object.assign(new Error("Scanner test aborted."), {
    name: "AbortError",
  });
}
