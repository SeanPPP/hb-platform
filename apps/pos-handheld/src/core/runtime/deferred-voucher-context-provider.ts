import type {
  VoucherPaymentContext,
  VoucherPaymentContextProvider,
} from "../../features/payments/voucher";
import type { PaymentAttempt } from "../contracts";

/**
 * 解除异步 provider registry 与组合根可信收银员会话之间的构造环。
 * bind 前调用会失败关闭；delegate 只允许绑定一次。
 */
export class DeferredVoucherContextProvider {
  private delegate: VoucherPaymentContextProvider | null = null;

  public bind(delegate: VoucherPaymentContextProvider): void {
    if (this.delegate) {
      throw new Error("Voucher context provider is already bound.");
    }
    this.delegate = delegate;
  }

  public readonly provide: VoucherPaymentContextProvider = (
    attempt: PaymentAttempt,
  ): Promise<VoucherPaymentContext> => {
    if (!this.delegate) {
      return Promise.reject(
        Object.assign(
          new Error("Voucher context provider is not initialized."),
          { code: "VOUCHER_CONTEXT_NOT_PREPARED" },
        ),
      );
    }
    return this.delegate(attempt);
  };
}
