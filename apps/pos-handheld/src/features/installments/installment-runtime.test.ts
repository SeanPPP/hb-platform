import assert from "node:assert/strict";
import test from "node:test";

import { resolveInstallmentsRuntimeFactory } from "./installment-runtime";

test("只接受同时提供管理、统一支付与恢复能力的分期工厂", () => {
  const createPresenter = () => ({});
  const prepareCreateCheckout = () => ({});
  const createCheckoutPresenter = () => ({});
  const hasRecoveryRequired = async () => false;
  const factory = {
    createPresenter,
    prepareCreateCheckout,
    createCheckoutPresenter,
    hasRecoveryRequired,
  };
  assert.deepEqual(
    resolveInstallmentsRuntimeFactory({
      installments: factory,
    }),
    factory,
  );
  assert.equal(resolveInstallmentsRuntimeFactory({}), null);
  assert.equal(
    resolveInstallmentsRuntimeFactory({
      installments: { createPresenter: "unsafe" },
    }),
    null,
  );
  assert.equal(
    resolveInstallmentsRuntimeFactory({
      installments: { createPresenter },
    }),
    null,
  );
});
