// src/pages/System/Users/userPermissions.ts
var POS_TERMINAL_PREFIX = "Permissions.PosTerminal.";
var POS_MODULE_ORDER = ["Sales", "Payment", "Returns", "SpecialProducts", "Admin", "Device"];
var POS_MODULE_FALLBACK_NAMES = {
  Sales: "POS \u9500\u552E",
  Payment: "POS \u6536\u6B3E",
  Returns: "POS \u9000\u8D27",
  SpecialProducts: "POS \u7279\u6B8A\u5546\u54C1",
  Admin: "POS \u7BA1\u7406",
  Device: "POS \u8BBE\u5907"
};
var LINE_DISCOUNT_PERMISSION_CODES = [
  "Permissions.PosTerminal.Sales.LineManualDiscount",
  "Permissions.PosTerminal.Sales.LineQuickDiscount10Percent",
  "Permissions.PosTerminal.Sales.LineQuickDiscount20Percent",
  "Permissions.PosTerminal.Sales.LineQuickDiscount30Percent",
  "Permissions.PosTerminal.Sales.LineQuickDiscount40Percent",
  "Permissions.PosTerminal.Sales.LineQuickDiscount50Percent"
];
var ORDER_DISCOUNT_PERMISSION_CODES = [
  "Permissions.PosTerminal.Sales.OrderManualDiscount",
  "Permissions.PosTerminal.Sales.OrderQuickDiscount10Percent",
  "Permissions.PosTerminal.Sales.OrderQuickDiscount20Percent",
  "Permissions.PosTerminal.Sales.OrderQuickDiscount30Percent",
  "Permissions.PosTerminal.Sales.OrderQuickDiscount40Percent",
  "Permissions.PosTerminal.Sales.OrderQuickDiscount50Percent"
];
var LINE_DISCOUNT_ORDER = new Map(LINE_DISCOUNT_PERMISSION_CODES.map((code, index) => [code, index]));
var ORDER_DISCOUNT_ORDER = new Map(ORDER_DISCOUNT_PERMISSION_CODES.map((code, index) => [code, index]));
function getPosPermissionModule(permissionCode) {
  if (!permissionCode.startsWith(POS_TERMINAL_PREFIX)) return "Other";
  return permissionCode.slice(POS_TERMINAL_PREFIX.length).split(".")[0] || "Other";
}
function sortPermissionsByDisplayName(permissions) {
  return [...permissions].sort(
    (left, right) => left.name.localeCompare(right.name, "zh-CN", { numeric: true })
  );
}
function sortDiscountPermissions(permissions, order) {
  return [...permissions].sort(
    (left, right) => (order.get(left.code) ?? Number.MAX_SAFE_INTEGER) - (order.get(right.code) ?? Number.MAX_SAFE_INTEGER)
  );
}
function buildPosPermissionSections(assignablePermissions) {
  const modules = /* @__PURE__ */ new Map();
  assignablePermissions.forEach((permission) => {
    const module = getPosPermissionModule(permission.code);
    modules.set(module, [...modules.get(module) ?? [], permission]);
  });
  return Array.from(modules.entries()).sort(([left], [right]) => {
    const leftIndex = POS_MODULE_ORDER.indexOf(left);
    const rightIndex = POS_MODULE_ORDER.indexOf(right);
    return (leftIndex < 0 ? Number.MAX_SAFE_INTEGER : leftIndex) - (rightIndex < 0 ? Number.MAX_SAFE_INTEGER : rightIndex) || left.localeCompare(right);
  }).map(([module, permissions]) => {
    const lineDiscounts = permissions.filter((permission) => LINE_DISCOUNT_ORDER.has(permission.code));
    const orderDiscounts = permissions.filter((permission) => ORDER_DISCOUNT_ORDER.has(permission.code));
    const discounts = new Set([...lineDiscounts, ...orderDiscounts].map((permission) => permission.code));
    const regularPermissions = permissions.filter((permission) => !discounts.has(permission.code));
    const groups = [];
    if (regularPermissions.length) {
      groups.push({
        key: `${module}:regular`,
        displayName: module === "Sales" ? "\u9500\u552E\u64CD\u4F5C" : "\u6A21\u5757\u6743\u9650",
        permissions: sortPermissionsByDisplayName(regularPermissions)
      });
    }
    if (lineDiscounts.length) {
      groups.push({
        key: `${module}:line-discounts`,
        displayName: "\u5355\u884C\u6298\u6263",
        permissions: sortDiscountPermissions(lineDiscounts, LINE_DISCOUNT_ORDER)
      });
    }
    if (orderDiscounts.length) {
      groups.push({
        key: `${module}:order-discounts`,
        displayName: "\u6574\u5355\u6298\u6263",
        permissions: sortDiscountPermissions(orderDiscounts, ORDER_DISCOUNT_ORDER)
      });
    }
    return {
      module,
      displayName: permissions[0]?.group || POS_MODULE_FALLBACK_NAMES[module] || module,
      groups
    };
  });
}
function getEditablePosPermissionCodes(permissionCodes, assignablePermissions) {
  const assignableCodes = new Set(assignablePermissions.map((permission) => permission.code));
  return uniquePermissionCodes(permissionCodes.filter((permissionCode) => assignableCodes.has(permissionCode)));
}
function buildGrantedPosPermissionCodes(selectedPermissionCodes, assignablePermissions) {
  return getEditablePosPermissionCodes(selectedPermissionCodes, assignablePermissions);
}
function setPosPermissionGroupSelection(selectedPermissionCodes, groupPermissionCodes, checked) {
  const next = new Set(selectedPermissionCodes);
  groupPermissionCodes.forEach((permissionCode) => {
    if (checked) next.add(permissionCode);
    else next.delete(permissionCode);
  });
  return Array.from(next);
}
function getPosPermissionGroupSelectionState(selectedPermissionCodes, groupPermissionCodes) {
  const selectedPermissionSet = new Set(selectedPermissionCodes);
  const selectedCount = groupPermissionCodes.filter(
    (permissionCode) => selectedPermissionSet.has(permissionCode)
  ).length;
  const checked = groupPermissionCodes.length > 0 && selectedCount === groupPermissionCodes.length;
  return {
    checked,
    indeterminate: selectedCount > 0 && !checked
  };
}
function isInheritedPosPermissionMode(mode) {
  const normalizedMode = mode?.trim().toLowerCase();
  return normalizedMode === "inherited" || normalizedMode === "inherit";
}
function shouldEnablePosPermissionSave(mode, hasChanges) {
  return isInheritedPosPermissionMode(mode) || hasChanges;
}
function isCurrentPosPermissionRequest(request, currentRequest) {
  return Boolean(
    currentRequest && request.sequence === currentRequest.sequence && request.userGuid === currentRequest.userGuid && request.storeGuid === currentRequest.storeGuid
  );
}
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
function createPosPermission(code, name, group = "POS \u9500\u552E") {
  return { code, name, group, description: "" };
}
var posSalesPrefix = "Permissions.PosTerminal.Sales.";
var assignableDiscountPermissions = [
  createPosPermission(`${posSalesPrefix}OrderQuickDiscount50Percent`, "\u6574\u5355 50% \u6298\u6263"),
  createPosPermission(`${posSalesPrefix}LineQuickDiscount20Percent`, "\u5355\u884C 20% \u6298\u6263"),
  createPosPermission(`${posSalesPrefix}LineManualDiscount`, "\u5355\u884C\u624B\u52A8\u6298\u6263"),
  createPosPermission(`${posSalesPrefix}OrderManualDiscount`, "\u6574\u5355\u624B\u52A8\u6298\u6263"),
  createPosPermission(`${posSalesPrefix}LineQuickDiscount10Percent`, "\u5355\u884C 10% \u6298\u6263"),
  createPosPermission(`${posSalesPrefix}LineQuickDiscount30Percent`, "\u5355\u884C 30% \u6298\u6263"),
  createPosPermission(`${posSalesPrefix}LineQuickDiscount40Percent`, "\u5355\u884C 40% \u6298\u6263"),
  createPosPermission(`${posSalesPrefix}LineQuickDiscount50Percent`, "\u5355\u884C 50% \u6298\u6263"),
  createPosPermission(`${posSalesPrefix}OrderQuickDiscount10Percent`, "\u6574\u5355 10% \u6298\u6263"),
  createPosPermission(`${posSalesPrefix}OrderQuickDiscount20Percent`, "\u6574\u5355 20% \u6298\u6263"),
  createPosPermission(`${posSalesPrefix}OrderQuickDiscount30Percent`, "\u6574\u5355 30% \u6298\u6263"),
  createPosPermission(`${posSalesPrefix}OrderQuickDiscount40Percent`, "\u6574\u5355 40% \u6298\u6263")
];
var posSections = buildPosPermissionSections(assignableDiscountPermissions);
assertArrayEqual(
  posSections.flatMap((section) => section.groups.map((group) => group.displayName)),
  ["\u5355\u884C\u6298\u6263", "\u6574\u5355\u6298\u6263"],
  "POS discount permissions should be presented as clear line and order groups"
);
assertArrayEqual(
  posSections[0]?.groups[0]?.permissions.map((permission) => permission.code) ?? [],
  [
    `${posSalesPrefix}LineManualDiscount`,
    `${posSalesPrefix}LineQuickDiscount10Percent`,
    `${posSalesPrefix}LineQuickDiscount20Percent`,
    `${posSalesPrefix}LineQuickDiscount30Percent`,
    `${posSalesPrefix}LineQuickDiscount40Percent`,
    `${posSalesPrefix}LineQuickDiscount50Percent`
  ],
  "Line discount permissions should keep manual then 10 to 50 percent order"
);
assertArrayEqual(
  posSections[0]?.groups[1]?.permissions.map((permission) => permission.code) ?? [],
  [
    `${posSalesPrefix}OrderManualDiscount`,
    `${posSalesPrefix}OrderQuickDiscount10Percent`,
    `${posSalesPrefix}OrderQuickDiscount20Percent`,
    `${posSalesPrefix}OrderQuickDiscount30Percent`,
    `${posSalesPrefix}OrderQuickDiscount40Percent`,
    `${posSalesPrefix}OrderQuickDiscount50Percent`
  ],
  "Order discount permissions should keep manual then 10 to 50 percent order"
);
assertArrayEqual(
  buildGrantedPosPermissionCodes(
    [
      `${posSalesPrefix}LineManualDiscount`,
      "Permissions.PosTerminal.Admin.Manage",
      `${posSalesPrefix}LineManualDiscount`
    ],
    assignableDiscountPermissions
  ),
  [`${posSalesPrefix}LineManualDiscount`],
  "POS save payload should only contain unique API-assignable permission codes"
);
assertArrayEqual(
  getEditablePosPermissionCodes(
    [
      `${posSalesPrefix}LineManualDiscount`,
      "Permissions.PosTerminal.Admin.Hidden"
    ],
    assignableDiscountPermissions
  ),
  [`${posSalesPrefix}LineManualDiscount`],
  "POS effective selection should not expose permissions outside the API whitelist"
);
assertArrayEqual(
  setPosPermissionGroupSelection(
    ["Permissions.PosTerminal.Payment.View", `${posSalesPrefix}LineManualDiscount`],
    [
      `${posSalesPrefix}LineManualDiscount`,
      `${posSalesPrefix}LineQuickDiscount10Percent`,
      `${posSalesPrefix}LineQuickDiscount10Percent`
    ],
    true
  ),
  [
    "Permissions.PosTerminal.Payment.View",
    `${posSalesPrefix}LineManualDiscount`,
    `${posSalesPrefix}LineQuickDiscount10Percent`
  ],
  "Selecting a POS permission group should add unique group codes and preserve other groups"
);
assertArrayEqual(
  setPosPermissionGroupSelection(
    [
      "Permissions.PosTerminal.Payment.View",
      `${posSalesPrefix}LineManualDiscount`,
      `${posSalesPrefix}LineQuickDiscount10Percent`
    ],
    [
      `${posSalesPrefix}LineManualDiscount`,
      `${posSalesPrefix}LineQuickDiscount10Percent`
    ],
    false
  ),
  ["Permissions.PosTerminal.Payment.View"],
  "Clearing a POS permission group should remove only that group and preserve other groups"
);
assertEqual(
  JSON.stringify(getPosPermissionGroupSelectionState([], ["pos.view", "pos.edit"])),
  JSON.stringify({ checked: false, indeterminate: false }),
  "A POS permission group without selected permissions should be unchecked"
);
assertEqual(
  JSON.stringify(getPosPermissionGroupSelectionState(["pos.view"], ["pos.view", "pos.edit"])),
  JSON.stringify({ checked: false, indeterminate: true }),
  "A partially selected POS permission group should be indeterminate"
);
assertEqual(
  JSON.stringify(getPosPermissionGroupSelectionState(["pos.view", "pos.edit"], ["pos.view", "pos.edit"])),
  JSON.stringify({ checked: true, indeterminate: false }),
  "A fully selected POS permission group should be checked"
);
assertEqual(
  isInheritedPosPermissionMode("Inherited"),
  true,
  "Inherited POS mode should be recognized case-insensitively"
);
assertEqual(
  isInheritedPosPermissionMode("Override"),
  false,
  "Override POS mode should not be treated as inherited"
);
assertEqual(
  shouldEnablePosPermissionSave("Inherited", false),
  true,
  "Inherited POS mode should allow saving an unchanged effective snapshot"
);
assertEqual(
  shouldEnablePosPermissionSave("Override", false),
  false,
  "Unchanged override mode should keep save disabled"
);
assertEqual(
  shouldEnablePosPermissionSave("Override", true),
  true,
  "Changed override mode should allow saving"
);
assertEqual(
  isCurrentPosPermissionRequest(
    { sequence: 3, userGuid: "user-a", storeGuid: "store-b" },
    { sequence: 3, userGuid: "user-a", storeGuid: "store-b" }
  ),
  true,
  "Matching POS permission request target should be current"
);
assertEqual(
  isCurrentPosPermissionRequest(
    { sequence: 2, userGuid: "user-a", storeGuid: "store-a" },
    { sequence: 3, userGuid: "user-a", storeGuid: "store-b" }
  ),
  false,
  "Older POS permission response must not overwrite the latest store request"
);
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
