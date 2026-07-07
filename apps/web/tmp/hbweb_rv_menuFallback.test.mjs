// src/router/menuFallback.ts
function isMenuNodeLike(item) {
  return typeof item === "object" && item !== null;
}
function hasMenuChildren(item) {
  if (!isMenuNodeLike(item)) {
    return false;
  }
  return Array.isArray(item.children) && item.children.length > 0;
}
function countMenuLeaves(items) {
  if (!items?.length) {
    return 0;
  }
  return items.reduce((count, item) => {
    if (!isMenuNodeLike(item)) {
      return count;
    }
    if (hasMenuChildren(item)) {
      return count + countMenuLeaves(item.children);
    }
    return count + 1;
  }, 0);
}
function getMenuLeafKey(item) {
  const key = item.key ?? item.path;
  if (typeof key === "string" || typeof key === "number") {
    return String(key);
  }
  return void 0;
}
function collectMenuLeafKeys(items) {
  if (!items?.length) {
    return [];
  }
  return items.flatMap((item) => {
    if (!isMenuNodeLike(item)) {
      return [];
    }
    if (hasMenuChildren(item)) {
      return collectMenuLeafKeys(item.children);
    }
    const key = getMenuLeafKey(item);
    return key ? [key] : [];
  });
}
function chooseNavigationMenus(localMenus, backendMenus) {
  if (!backendMenus?.length) {
    return localMenus;
  }
  const localLeafKeys = collectMenuLeafKeys(localMenus);
  const backendLeafKeys = new Set(collectMenuLeafKeys(backendMenus));
  const backendMissesLocalLeaf = localLeafKeys.some((key) => !backendLeafKeys.has(key));
  return backendMissesLocalLeaf || countMenuLeaves(backendMenus) === 0 ? localMenus : backendMenus;
}

// src/router/menuFallback.test.ts
function assertSameReference(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(message);
  }
}
var localStoreManagerMenus = [
  { key: "/dashboard" },
  {
    key: "/pos-admin",
    children: [
      { key: "/pos-admin/store-product-price" },
      { key: "/pos-admin/schedule-attendance" },
      { key: "/pos-admin/sales-orders" },
      { key: "/pos-admin/local-supplier-invoices" }
    ]
  }
];
var staleBackendMenus = [{ key: "/dashboard" }];
var completeBackendMenus = [
  { key: "/dashboard" },
  {
    key: "/pos-admin",
    children: [
      { key: "/pos-admin/store-product-price" },
      { key: "/pos-admin/schedule-attendance" },
      { key: "/pos-admin/sales-orders" },
      { key: "/pos-admin/local-supplier-invoices" }
    ]
  }
];
var warehouseStaffLocalMenus = [
  {
    key: "/warehouse",
    children: [{ key: "/warehouse/store-orders" }]
  }
];
var warehouseStaffBackendMenus = [
  {
    key: "/warehouse",
    children: [{ key: "/warehouse/store-orders" }]
  }
];
var warehouseStaffStaleBackendMenus = [{ key: "/dashboard" }];
assertSameReference(
  chooseNavigationMenus(localStoreManagerMenus, staleBackendMenus),
  localStoreManagerMenus,
  "Stale backend navigation should not hide locally authorized StoreManager menus"
);
assertSameReference(
  chooseNavigationMenus(localStoreManagerMenus, completeBackendMenus),
  completeBackendMenus,
  "Complete backend navigation should remain authoritative"
);
assertSameReference(
  chooseNavigationMenus(warehouseStaffLocalMenus, warehouseStaffBackendMenus),
  warehouseStaffBackendMenus,
  "Backend navigation with fewer authorized leaves should remain authoritative when it covers all local leaves"
);
assertSameReference(
  chooseNavigationMenus(warehouseStaffLocalMenus, warehouseStaffStaleBackendMenus),
  warehouseStaffLocalMenus,
  "Backend navigation missing a locally authorized leaf should still use the local fallback"
);
console.log("menuFallback.test: ok");
