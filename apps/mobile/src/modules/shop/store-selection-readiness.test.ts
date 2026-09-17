import {
  buildStoreSelectionScopeKey,
  isStoreSelectionReadyForScope,
} from "./store-selection-readiness";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const scopeKey = buildStoreSelectionScopeKey({
  userGuid: " USER-1 ",
  useAllStores: false,
  storeCodes: ["B02", " a01 "],
});

assertEqual(scopeKey, "user-1:assigned:a01,b02", "scope key is stable and normalized");
assertEqual(
  isStoreSelectionReadyForScope({
    currentScopeKey: scopeKey,
    hydratedScopeKey: null,
    isDeviceMode: false,
    userGuid: "USER-1",
  }),
  false,
  "query success alone does not mark persisted selection as hydrated"
);
assertEqual(
  isStoreSelectionReadyForScope({
    currentScopeKey: scopeKey,
    hydratedScopeKey: scopeKey,
    isDeviceMode: false,
    userGuid: "USER-1",
  }),
  true,
  "matching completed hydration marks the selection ready"
);
assertEqual(
  isStoreSelectionReadyForScope({
    currentScopeKey: `${scopeKey}:changed`,
    hydratedScopeKey: scopeKey,
    isDeviceMode: false,
    userGuid: "USER-1",
  }),
  false,
  "a changed store scope requires a new hydration"
);
assertEqual(
  isStoreSelectionReadyForScope({
    currentScopeKey: null,
    deviceStoreCode: "S01",
    hydratedScopeKey: null,
    isDeviceMode: true,
    userGuid: null,
  }),
  true,
  "device mode is ready when its fixed store exists"
);
