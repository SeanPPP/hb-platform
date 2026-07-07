// src/pages/System/Users/userPermissions.ts
function uniquePermissionCodes(permissionCodes) {
  return Array.from(new Set(permissionCodes));
}
function getCheckedPermissionKeys(permissionState2) {
  if (!permissionState2) return [];
  return uniquePermissionCodes([
    ...permissionState2.inheritedPermissionCodes,
    ...permissionState2.directPermissionCodes
  ]);
}
function buildFallbackUserPermissionState({
  userGuid,
  permissions
}) {
  const effectivePermissionCodes = uniquePermissionCodes(permissions ?? []);
  return {
    userGuid,
    inheritedPermissionCodes: effectivePermissionCodes,
    directPermissionCodes: [],
    effectivePermissionCodes,
    inheritedSources: []
  };
}
function buildDirectPermissionPayload(permissionCodes) {
  return uniquePermissionCodes(permissionCodes);
}
function toggleDirectPermission({
  currentDirectPermissions,
  inheritedPermissionCodes,
  permissionCode,
  checked
}) {
  const next = new Set(currentDirectPermissions);
  if (checked) {
    next.add(permissionCode);
  } else {
    next.delete(permissionCode);
  }
  if (!checked && inheritedPermissionCodes.includes(permissionCode)) {
    next.delete(permissionCode);
  }
  return buildDirectPermissionPayload(Array.from(next));
}
function deriveDirectPermissionKeysFromChecked({
  checkedPermissionKeys,
  allPermissionCodes,
  inheritedPermissionCodes,
  currentDirectPermissionCodes
}) {
  const checkedSet = new Set(checkedPermissionKeys);
  const inheritedSet = new Set(inheritedPermissionCodes);
  const currentDirectSet = new Set(currentDirectPermissionCodes);
  return buildDirectPermissionPayload(
    allPermissionCodes.filter(
      (permissionCode) => checkedSet.has(permissionCode) && (!inheritedSet.has(permissionCode) || currentDirectSet.has(permissionCode))
    )
  );
}
function buildPermissionSourceMap(inheritedSources) {
  const sourceMap2 = {};
  inheritedSources.forEach((source) => {
    source.permissionCodes.forEach((permissionCode) => {
      sourceMap2[permissionCode] = [...sourceMap2[permissionCode] ?? [], source.roleName];
    });
  });
  return sourceMap2;
}

// src/pages/System/Users/userPermissions.test.ts
function assertArrayEqual(actual, expected, message) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${message}. Expected: ${expectedText}, received: ${actualText}`);
  }
}
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
var permissionState = {
  userGuid: "user-a-guid",
  inheritedPermissionCodes: ["Orders.View", "StoreProducts.View"],
  directPermissionCodes: ["Reports.Export"],
  effectivePermissionCodes: ["Orders.View", "StoreProducts.View", "Reports.Export"],
  inheritedSources: [
    { roleName: "StoreManager", permissionCodes: ["Orders.View", "StoreProducts.View"] }
  ]
};
assertArrayEqual(
  getCheckedPermissionKeys(permissionState),
  ["Orders.View", "StoreProducts.View", "Reports.Export"],
  "Checked permission keys should include inherited and direct permissions"
);
assertArrayEqual(
  toggleDirectPermission({
    currentDirectPermissions: permissionState.directPermissionCodes,
    inheritedPermissionCodes: permissionState.inheritedPermissionCodes,
    permissionCode: "Orders.View",
    checked: false
  }),
  ["Reports.Export"],
  "Inherited permissions should not be removed from direct permission payload"
);
assertArrayEqual(
  toggleDirectPermission({
    currentDirectPermissions: permissionState.directPermissionCodes,
    inheritedPermissionCodes: permissionState.inheritedPermissionCodes,
    permissionCode: "Orders.Create",
    checked: true
  }),
  ["Reports.Export", "Orders.Create"],
  "Unchecked permissions should be addable as direct permissions"
);
assertArrayEqual(
  toggleDirectPermission({
    currentDirectPermissions: ["Reports.Export", "Orders.View"],
    inheritedPermissionCodes: permissionState.inheritedPermissionCodes,
    permissionCode: "Orders.View",
    checked: false
  }),
  ["Reports.Export"],
  "Permission with both inherited and direct sources should keep only the direct payload removable"
);
assertArrayEqual(
  buildDirectPermissionPayload(["Reports.Export", "Orders.Create", "Reports.Export"]),
  ["Reports.Export", "Orders.Create"],
  "Saved payload should include unique direct permissions only"
);
assertArrayEqual(
  deriveDirectPermissionKeysFromChecked({
    checkedPermissionKeys: ["Orders.View"],
    allPermissionCodes: ["Orders.View", "Orders.Create", "Reports.Export"],
    inheritedPermissionCodes: ["Orders.View"],
    currentDirectPermissionCodes: []
  }),
  [],
  "Unchecking an inherited-only permission should not add it to the direct payload"
);
assertArrayEqual(
  deriveDirectPermissionKeysFromChecked({
    checkedPermissionKeys: ["Orders.View"],
    allPermissionCodes: ["Orders.View", "Orders.Create", "Reports.Export"],
    inheritedPermissionCodes: ["Orders.View"],
    currentDirectPermissionCodes: ["Orders.View", "Reports.Export"]
  }),
  ["Orders.View"],
  "Unrelated Tree changes should preserve direct permission payloads that are also inherited"
);
assertArrayEqual(
  deriveDirectPermissionKeysFromChecked({
    checkedPermissionKeys: ["Orders.Create", "Reports.Export"],
    allPermissionCodes: ["Orders.View", "Orders.Create", "Reports.Export"],
    inheritedPermissionCodes: ["Orders.View"],
    currentDirectPermissionCodes: []
  }),
  ["Orders.Create", "Reports.Export"],
  "Checking a category should add only non-inherited permissions as direct permissions"
);
assertArrayEqual(
  deriveDirectPermissionKeysFromChecked({
    checkedPermissionKeys: [],
    allPermissionCodes: ["Orders.View", "Orders.Create", "Reports.Export"],
    inheritedPermissionCodes: ["Orders.View"],
    currentDirectPermissionCodes: ["Orders.View", "Orders.Create", "Reports.Export"]
  }),
  [],
  "Clearing a category should remove direct permissions while inherited permissions stay effective elsewhere"
);
var sourceMap = buildPermissionSourceMap(permissionState.inheritedSources);
assertArrayEqual(
  sourceMap["Orders.View"],
  ["StoreManager"],
  "Permission source map should expose role sources by permission code"
);
assertEqual(
  sourceMap["Reports.Export"],
  void 0,
  "Direct-only permissions should not have inherited role sources"
);
var fallbackPermissionState = buildFallbackUserPermissionState({
  userGuid: "user-b-guid",
  permissions: ["Orders.View", "Reports.Export", "Orders.View"]
});
assertArrayEqual(
  fallbackPermissionState.inheritedPermissionCodes,
  ["Orders.View", "Reports.Export"],
  "Fallback permission state should expose user detail permissions as effective inherited permissions"
);
assertArrayEqual(
  fallbackPermissionState.directPermissionCodes,
  [],
  "Fallback permission state should not create direct permissions without a user permission API"
);
assertArrayEqual(
  fallbackPermissionState.effectivePermissionCodes,
  ["Orders.View", "Reports.Export"],
  "Fallback permission state should deduplicate effective permission codes"
);
