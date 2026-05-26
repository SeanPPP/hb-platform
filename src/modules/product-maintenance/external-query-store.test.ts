import { resolveExternalQueryStore } from "./external-query-store";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const stores = [
  { storeCode: "S01", storeName: "Sydney" },
  { storeCode: "S02", storeName: "Brisbane" },
];

assertEqual(
  resolveExternalQueryStore({
    targetStoreCode: "S02",
    selectedStoreCode: "S01",
    stores,
    storesLoading: false,
  }).type,
  "select-store",
  "different known target store asks caller to switch stores"
);

assertEqual(
  resolveExternalQueryStore({
    targetStoreCode: "S03",
    selectedStoreCode: "S01",
    stores,
    storesLoading: false,
  }).type,
  "store-not-found",
  "missing target store must not fall through to current store"
);

assertEqual(
  resolveExternalQueryStore({
    targetStoreCode: "S01",
    selectedStoreCode: "S01",
    stores,
    storesLoading: false,
  }).type,
  "ready",
  "matching selected store can query immediately"
);

assertEqual(
  resolveExternalQueryStore({
    selectedStoreCode: null,
    stores,
    storesLoading: false,
  }).type,
  "wait",
  "query without target store waits until a store is selected"
);

assertEqual(
  resolveExternalQueryStore({
    targetStoreCode: "S02",
    selectedStoreCode: "S01",
    stores: [],
    storesLoading: true,
  }).type,
  "wait",
  "loading stores waits instead of reporting the target store as missing"
);
