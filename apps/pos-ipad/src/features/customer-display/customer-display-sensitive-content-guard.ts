import type { CustomerDisplayPublishResult } from "./customer-display-publisher";

import type { ExternalCustomerDisplayPort } from "@/core/contracts";

type SensitiveContentPublisherPort = Readonly<{
  clearSensitiveContent(): Promise<CustomerDisplayPublishResult>;
}>;

type NativeSafeBlankCapability = Readonly<{
  forceBlank(): Promise<void>;
}>;

type NativeSafetyDisableCapability = Readonly<{
  disableForSafety(): Promise<void>;
}>;

export type CustomerDisplaySensitiveContentGuardOptions = Readonly<{
  publishAttempts?: number;
  operationTimeoutMs?: number;
}>;

const defaultPublishAttempts = 3;
const defaultOperationTimeoutMs = 200;

/**
 * 会话失效先尝试发布无交易 idle 快照；桥接连续失败或挂起时，最终要求原生
 * surface 清空。旧原生版本没有 forceBlank 时降级为隐藏外屏，避免保留上一单。
 */
export class CustomerDisplaySensitiveContentGuard {
  private readonly operationTimeoutMs: number;
  private readonly publishAttempts: number;

  public constructor(
    private readonly publisher: SensitiveContentPublisherPort,
    private readonly display: ExternalCustomerDisplayPort,
    options: CustomerDisplaySensitiveContentGuardOptions = {},
  ) {
    this.publishAttempts =
      options.publishAttempts ?? defaultPublishAttempts;
    this.operationTimeoutMs =
      options.operationTimeoutMs ?? defaultOperationTimeoutMs;
    if (
      !Number.isSafeInteger(this.publishAttempts) ||
      this.publishAttempts < 1
    ) {
      throw new TypeError("Customer display publish attempts are invalid.");
    }
    if (
      !Number.isSafeInteger(this.operationTimeoutMs) ||
      this.operationTimeoutMs < 1
    ) {
      throw new TypeError("Customer display operation timeout is invalid.");
    }
  }

  public async clearSensitiveContent(): Promise<CustomerDisplayPublishResult> {
    let lastFailure: CustomerDisplayPublishResult | null = null;

    for (let attempt = 0; attempt < this.publishAttempts; attempt += 1) {
      try {
        const result = await withTimeout(
          this.publisher.clearSensitiveContent(),
          this.operationTimeoutMs,
        );
        if (result.status !== "failed") return result;
        lastFailure = result;
      } catch {
        // 继续下一次有界尝试；清屏最终由原生 forceBlank/disable 收口。
      }
    }

    await this.forceSafeBlank();
    return (
      lastFailure ??
      Object.freeze({
        status: "failed" as const,
        revision: 0,
        errorCode: "DISPLAY_PUBLISH_FAILED" as const,
      })
    );
  }

  private async forceSafeBlank(): Promise<void> {
    const capability = nativeSafeBlankCapability(this.display);
    if (capability) {
      try {
        await withTimeout(
          capability.forceBlank(),
          this.operationTimeoutMs,
        );
        return;
      } catch {
        // forceBlank 不可达时继续隐藏 window；不能让公共外屏停留在上一单。
      }
    }

    const safetyDisable = nativeSafetyDisableCapability(this.display);
    if (safetyDisable) {
      await withTimeout(
        safetyDisable.disableForSafety(),
        this.operationTimeoutMs,
      );
      return;
    }

    // 非原生 port 的兼容路径无法核验返回状态，但异常仍必须传播给调用方，
    // 由上层会话失效边界决定记录或降级，guard 本身不能静默报告成功。
    await withTimeout(
      this.display.setEnabled(false),
      this.operationTimeoutMs,
    );
  }
}

function nativeSafeBlankCapability(
  display: ExternalCustomerDisplayPort,
): NativeSafeBlankCapability | null {
  const candidate = display as ExternalCustomerDisplayPort &
    Partial<NativeSafeBlankCapability>;
  return typeof candidate.forceBlank === "function"
    ? Object.freeze({
        forceBlank: candidate.forceBlank.bind(display),
      })
    : null;
}

function nativeSafetyDisableCapability(
  display: ExternalCustomerDisplayPort,
): NativeSafetyDisableCapability | null {
  const candidate = display as ExternalCustomerDisplayPort &
    Partial<NativeSafetyDisableCapability>;
  return typeof candidate.disableForSafety === "function"
    ? Object.freeze({
        disableForSafety: candidate.disableForSafety.bind(display),
      })
    : null;
}

function withTimeout<T>(operation: Promise<T>, timeoutMs: number): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timeout = setTimeout(
      () => reject(new Error("CUSTOMER_DISPLAY_OPERATION_TIMEOUT")),
      timeoutMs,
    );
    operation.then(
      (result) => {
        clearTimeout(timeout);
        resolve(result);
      },
      (error: unknown) => {
        clearTimeout(timeout);
        reject(error);
      },
    );
  });
}
