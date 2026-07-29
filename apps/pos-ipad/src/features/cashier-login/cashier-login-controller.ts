import type { ActiveCashierSummary } from "./cashier-login-store";

import type {
  PosCashierSessionRuntimeService,
  PosCashierSummary,
} from "@/core/runtime/production-pos-service-composition";

export type CashierLoginRuntime = Readonly<{
  state: Readonly<{
    phase: "ready" | "ready-offline" | "locked" | "failed" | "starting" | "idle" | "registration-required" | "pending-approval";
    device: "authorized-local" | "authorized-online" | "locked" | "unknown" | "registration-required" | "pending-approval";
  }>;
  services: Readonly<{
    cashierSession: PosCashierSessionRuntimeService;
  }> | null;
}>;

export type CashierLoginStorePort = Readonly<{
  setActiveCashier(cashier: ActiveCashierSummary): void;
  clearActiveCashier(): void;
}>;

export type CashierLoginSuccess = Readonly<{
  cashier: ActiveCashierSummary;
}>;

export class CashierLoginError extends Error {
  public constructor(
    public readonly code:
      | "RUNTIME_NOT_READY"
      | "DEVICE_LOCKED"
      | "BARCODE_REQUIRED",
  ) {
    super(code);
  }
}

/**
 * UI 只提交条码。终端范围、权限和票据始终由生产组合根保管。
 */
export class CashierLoginController {
  public constructor(private readonly store: CashierLoginStorePort) {}

  public async login(
    barcodeInput: string,
    runtime: CashierLoginRuntime,
  ): Promise<CashierLoginSuccess> {
    this.store.clearActiveCashier();
    const barcode = barcodeInput.trim();
    if (!barcode) throw new CashierLoginError("BARCODE_REQUIRED");
    if (runtime.state.phase === "locked" || runtime.state.device === "locked") {
      throw new CashierLoginError("DEVICE_LOCKED");
    }
    if (
      (runtime.state.phase !== "ready" && runtime.state.phase !== "ready-offline") ||
      !runtime.services ||
      (runtime.state.device !== "authorized-local" && runtime.state.device !== "authorized-online")
    ) {
      throw new CashierLoginError("RUNTIME_NOT_READY");
    }

    const cashier = toPublicCashierSummary(
      await runtime.services.cashierSession.signIn(barcode),
    );
    this.store.setActiveCashier(cashier);
    return { cashier };
  }
}

/**
 * 即使运行时实现错误地附带额外字段，UI 状态也只接受这组公开字段。
 */
function toPublicCashierSummary(
  summary: PosCashierSummary,
): ActiveCashierSummary {
  return Object.freeze({
    cashierId: summary.cashierId,
    userGuid: summary.userGuid,
    cashierName: summary.cashierName,
    storeCode: summary.storeCode,
    deviceCode: summary.deviceCode,
    permissions: Object.freeze([...summary.permissions]),
    source: summary.source,
  });
}
