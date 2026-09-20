import assert from "node:assert/strict";
import test from "node:test";
import { buildNavigationMenuScopeKey, resolveCachedNavigationMenu } from "./menu-cache";

const items = [
  { routeName: "product-query", titleKey: "tabs.productQuery", icon: "barcode", permission: null, order: 1 },
];

test("scopeKey 只对设备类会话生成，并按会话类型与账号隔离", () => {
  assert.equal(buildNavigationMenuScopeKey({ sessionKind: "device", hardwareId: "hw-1", userGuid: null }), "device:hw-1");
  assert.equal(
    buildNavigationMenuScopeKey({ sessionKind: "deviceAccount", hardwareId: "hw-1", userGuid: "u-1" }),
    "deviceAccount:hw-1:u-1",
  );
  assert.equal(buildNavigationMenuScopeKey({ sessionKind: "account", hardwareId: "hw-1", userGuid: "u-1" }), null);
  assert.equal(buildNavigationMenuScopeKey({ sessionKind: "device", hardwareId: " ", userGuid: null }), null);
});

test("缓存只在 scopeKey 匹配且非空时命中", () => {
  const cached = { scopeKey: "device:hw-1", items, savedAtIso: "2026-09-17T00:00:00.000Z" };
  assert.deepEqual(resolveCachedNavigationMenu(cached, "device:hw-1"), items);
  assert.equal(resolveCachedNavigationMenu(cached, "device:hw-2"), null);
  assert.equal(resolveCachedNavigationMenu(cached, null), null);
  assert.equal(resolveCachedNavigationMenu({ ...cached, items: [] }, "device:hw-1"), null);
  assert.equal(resolveCachedNavigationMenu(null, "device:hw-1"), null);
});
