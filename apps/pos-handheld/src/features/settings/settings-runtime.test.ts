import assert from "node:assert/strict";
import test from "node:test";

import {
  resolveSettingsRuntimeFactory,
  type SettingsRuntimeFactory,
} from "./settings-runtime";

test("Settings 路由只接受零参数 createPresenter 工厂", () => {
  const presenter = { destroy() {} };
  const factory: SettingsRuntimeFactory = {
    createPresenter: () => presenter as never,
  };

  assert.equal(
    resolveSettingsRuntimeFactory({ settings: factory }),
    factory,
  );
});

test("缺失或形状错误的 settings 服务返回 null", () => {
  assert.equal(resolveSettingsRuntimeFactory({}), null);
  assert.equal(resolveSettingsRuntimeFactory({ settings: null }), null);
  assert.equal(
    resolveSettingsRuntimeFactory({
      settings: { createPresenter: "not-a-function" },
    }),
    null,
  );
});
