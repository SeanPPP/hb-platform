import assert from "node:assert/strict";
import test from "node:test";

import { resolveInstallmentsRuntimeFactory } from "./installment-runtime";

test("只接受 services.installments 的零参数 presenter 工厂", () => {
  const createPresenter = () => ({});
  assert.deepEqual(
    resolveInstallmentsRuntimeFactory({
      installments: { createPresenter },
    }),
    { createPresenter },
  );
  assert.equal(resolveInstallmentsRuntimeFactory({}), null);
  assert.equal(
    resolveInstallmentsRuntimeFactory({
      installments: { createPresenter: "unsafe" },
    }),
    null,
  );
});
