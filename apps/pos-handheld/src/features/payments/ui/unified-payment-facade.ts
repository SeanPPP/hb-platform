import type { PaymentScreenPresenter } from "./payment-presenter";
import {
  installmentRepaymentPaymentEntry,
  paymentRecoveryEntry,
  type InstallmentCreatePaymentEntry,
  type InstallmentRepaymentPaymentEntry,
  type RegularPaymentEntry,
  type UnifiedPaymentEntry,
} from "./unified-payment-entry";

export type UnifiedPaymentRecoveryResolution =
  | Readonly<{ kind: "none" }>
  | Readonly<{
      kind: "ready";
      entry: Extract<UnifiedPaymentEntry, { kind: "recovery" }>;
      /** 普通支付优先时保留仍待后续处理的分期账本信号。 */
      deferredLedger?: "installment";
    }>;

export type UnifiedPaymentFacadeDependencies = Readonly<{
  regular: Readonly<{
    createPresenter(entry: RegularPaymentEntry | null): PaymentScreenPresenter;
    hasRecoveryRequired(): Promise<boolean>;
  }>;
  installments: Readonly<{
    prepareCreateCheckout(): InstallmentCreatePaymentEntry;
    createCheckoutPresenter(
      entry:
        | InstallmentCreatePaymentEntry
        | InstallmentRepaymentPaymentEntry
        | null,
    ): PaymentScreenPresenter;
    hasRecoveryRequired(): Promise<boolean>;
  }>;
}>;

/**
 * 支付路由唯一依赖面。两套账本都存在阻塞动作时按冻结业务规则先恢复普通
 * payment ledger；分期阻塞通过 deferredLedger 保持可观测，普通恢复结束后再导流。
 */
export class UnifiedPaymentFacade {
  public constructor(
    private readonly dependencies: UnifiedPaymentFacadeDependencies,
  ) {}

  public prepareInstallmentCreate(): InstallmentCreatePaymentEntry {
    return this.dependencies.installments.prepareCreateCheckout();
  }

  public prepareInstallmentRepayment(
    installmentGuid: string,
  ): InstallmentRepaymentPaymentEntry {
    return installmentRepaymentPaymentEntry(installmentGuid);
  }

  public createPresenter(entry: UnifiedPaymentEntry): PaymentScreenPresenter {
    if (entry.kind === "regular") {
      return this.dependencies.regular.createPresenter(entry);
    }
    if (entry.kind === "installment-create") {
      return this.dependencies.installments.createCheckoutPresenter(entry);
    }
    if (entry.kind === "installment-repayment") {
      return this.dependencies.installments.createCheckoutPresenter(entry);
    }
    return entry.ledger === "regular"
      ? this.dependencies.regular.createPresenter(null)
      : this.dependencies.installments.createCheckoutPresenter(null);
  }

  public async resolveRecovery(): Promise<UnifiedPaymentRecoveryResolution> {
    const regular = await this.dependencies.regular.hasRecoveryRequired();
    if (regular) {
      try {
        const installment =
          await this.dependencies.installments.hasRecoveryRequired();
        return Object.freeze({
          kind: "ready",
          entry: paymentRecoveryEntry("regular"),
          ...(installment
            ? ({ deferredLedger: "installment" } as const)
            : undefined),
        });
      } catch {
        // 普通账本已经明确阻塞时，分期探测故障不能覆盖可恢复的普通支付入口。
        return Object.freeze({
          kind: "ready",
          entry: paymentRecoveryEntry("regular"),
        });
      }
    }

    const installment =
      await this.dependencies.installments.hasRecoveryRequired();
    if (installment) {
      return Object.freeze({
        kind: "ready",
        entry: paymentRecoveryEntry("installment"),
      });
    }
    return Object.freeze({ kind: "none" });
  }
}
