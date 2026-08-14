import assert from "node:assert/strict";
import test from "node:test";

import { resolveLocalHistoryPresenterFactory } from "./local-history-runtime";

test("runtime resolver 只接受 localHistory 下的零参数 presenter factory", () => {
  const factory = {
    createPresenter() {
      throw new Error("test-only");
    },
  };

  assert.equal(
    resolveLocalHistoryPresenterFactory({ localHistory: factory }),
    factory,
  );
  assert.equal(resolveLocalHistoryPresenterFactory({}), null);
  assert.equal(resolveLocalHistoryPresenterFactory(null), null);
  assert.equal(
    resolveLocalHistoryPresenterFactory({
      localHistory: { createPresenter: "not-a-function" },
    }),
    null,
  );
});
