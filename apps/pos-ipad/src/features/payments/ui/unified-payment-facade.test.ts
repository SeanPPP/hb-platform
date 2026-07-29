import assert from "node:assert/strict";
import test from "node:test";

import { UnifiedPaymentFacade } from "./unified-payment-facade";

test("普通与分期账本同时阻塞时优先恢复普通支付并保留分期信号", async () => {
  const facade = facadeWithRecovery(true, true);
  assert.deepEqual(await facade.resolveRecovery(), {
    kind: "ready",
    entry: { kind: "recovery", ledger: "regular" },
    deferredLedger: "installment",
  });
});

test("只有一个账本阻塞时生成显式 recovery entry", async () => {
  assert.deepEqual(await facadeWithRecovery(true, false).resolveRecovery(), {
    kind: "ready",
    entry: { kind: "recovery", ledger: "regular" },
  });
  assert.deepEqual(await facadeWithRecovery(false, true).resolveRecovery(), {
    kind: "ready",
    entry: { kind: "recovery", ledger: "installment" },
  });
});

test("普通账本阻塞时分期探测异常不覆盖普通恢复入口", async () => {
  assert.deepEqual(
    await facadeWithRecovery(
      true,
      new Error("INSTALLMENT_RECOVERY_PROBE_FAILED"),
    ).resolveRecovery(),
    {
      kind: "ready",
      entry: { kind: "recovery", ledger: "regular" },
    },
  );
});

test("普通账本未阻塞时分期探测异常继续 fail closed", async () => {
  await assert.rejects(
    facadeWithRecovery(
      false,
      new Error("INSTALLMENT_RECOVERY_PROBE_FAILED"),
    ).resolveRecovery(),
    /INSTALLMENT_RECOVERY_PROBE_FAILED/,
  );
});

function facadeWithRecovery(
  regular: boolean,
  installment: boolean | Error,
): UnifiedPaymentFacade {
  return new UnifiedPaymentFacade({
    regular: {
      createPresenter: () => {
        throw new Error("unused");
      },
      hasRecoveryRequired: async () => regular,
    },
    installments: {
      prepareCreateCheckout: () => {
        throw new Error("unused");
      },
      createCheckoutPresenter: () => {
        throw new Error("unused");
      },
      hasRecoveryRequired: async () => {
        if (installment instanceof Error) {
          throw installment;
        }
        return installment;
      },
    },
  });
}
