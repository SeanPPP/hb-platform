import { buildNavigationDisplayTabs, isNavigationDisplayTabFocused } from "./tab-grouping";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertArrayEqual(actual: unknown[], expected: unknown[], label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

function route(name: string, index: number) {
  return { key: `${name}-key`, name, index };
}

const overflowTabs = buildNavigationDisplayTabs([
  route("home", 0),
  route("orders", 1),
  route("cart", 2),
  route("product-query", 3),
  route("local-supplier-invoices", 4),
  route("settings", 5),
]);
const overflowStoreTab = overflowTabs.find((item) => item.type === "store");

assertEqual(overflowTabs.length, 2, "overflow routes collapse into a store tab plus settings");
assertArrayEqual(
  overflowStoreTab?.type === "store" ? overflowStoreTab.children.map((item) => item.name) : [],
  ["home", "orders", "cart", "product-query", "local-supplier-invoices"],
  "store group includes product query and local supplier invoices when tabs overflow"
);
assertEqual(
  overflowStoreTab ? isNavigationDisplayTabFocused(overflowStoreTab, 4) : false,
  true,
  "store group is focused when local supplier invoices is selected"
);

const compactTabs = buildNavigationDisplayTabs([
  route("home", 0),
  route("cart", 1),
  route("product-query", 2),
  route("settings", 3),
]);

assertArrayEqual(
compactTabs.map((item) => item.type === "route" ? item.route.name : item.type),
  ["home", "cart", "product-query", "settings"],
  "compact route sets keep product query as a direct tab"
);
