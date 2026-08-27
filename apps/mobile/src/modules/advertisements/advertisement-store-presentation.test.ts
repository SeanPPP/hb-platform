import {
  buildAdvertisementStoreNameMap,
  buildAdvertisementStoreSummary,
  formatAdvertisementStoreLabel,
  hasSameAdvertisementStoreCodes,
  resolveAdvertisementDraftStoreScopes,
} from "./advertisement-store-presentation";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

const localNames = new Map([
  ["1002", "Local Sunnybank"],
  ["1003", "Garden City"],
  ["1004", "1004"],
]);

assertEqual(
  formatAdvertisementStoreLabel(
    { storeCode: "1002", storeName: " API Sunnybank " },
    localNames
  ),
  "API Sunnybank（1002）",
  "API name wins over the local store directory"
);
assertEqual(
  formatAdvertisementStoreLabel({ storeCode: "1003" }, localNames),
  "Garden City（1003）",
  "local store directory is the compatibility fallback"
);
assertEqual(
  formatAdvertisementStoreLabel({ storeCode: "1004", storeName: "1004" }, localNames),
  "1004",
  "a name equal to the code is not duplicated"
);
assertEqual(
  formatAdvertisementStoreLabel({ storeCode: "1099", storeName: "  " }, localNames),
  "1099",
  "unknown store safely falls back to its code"
);
assertEqual(
  formatAdvertisementStoreLabel(
    { storeCode: " s04 " },
    buildAdvertisementStoreNameMap([{ storeCode: "S04", storeName: "Inactive Store" }])
  ),
  "Inactive Store（s04）",
  "local directory fallback ignores store-code case and surrounding whitespace"
);

assertDeepEqual(
  resolveAdvertisementDraftStoreScopes(
    ["s04", "S05", "S99"],
    [
      { storeCode: "S04", storeName: "Inactive Store" },
      { storeCode: "S05" },
      { storeCode: "S99" },
    ],
    buildAdvertisementStoreNameMap([{ storeCode: "S05", storeName: "Deleted Store Cache" }])
  ),
  [
    { storeCode: "s04", storeName: "Inactive Store" },
    { storeCode: "S05", storeName: "Deleted Store Cache" },
    { storeCode: "S99", storeName: undefined },
  ],
  "editor presentation preserves every response scope with response, directory, then code fallback"
);

assertDeepEqual(
  buildAdvertisementStoreSummary([], localNames),
  { isAllStores: true, items: [], remainingCount: 0 },
  "empty scopes keep the all-stores semantic"
);

const oneStore = buildAdvertisementStoreSummary([{ storeCode: "1002", storeName: "Sunnybank" }], localNames);
assertEqual(oneStore.items.length, 1, "one store stays visible");
assertEqual(oneStore.remainingCount, 0, "one store has no remainder");

const threeStores = buildAdvertisementStoreSummary(
  ["1002", "1003", "1004"].map((storeCode) => ({ storeCode })),
  localNames
);
assertDeepEqual(
  threeStores.items.map((item) => item.storeCode),
  ["1002", "1003", "1004"],
  "three stores preserve their original order"
);
assertEqual(threeStores.remainingCount, 0, "three stores fit without a remainder");

const fourStores = buildAdvertisementStoreSummary(
  ["1002", "1003", "1004", "1005"].map((storeCode) => ({ storeCode })),
  localNames
);
assertEqual(fourStores.items.length, 3, "four stores are capped at three visible items");
assertEqual(fourStores.remainingCount, 1, "four stores expose a +1 remainder");

const thirtyFiveStores = buildAdvertisementStoreSummary(
  Array.from({ length: 35 }, (_, index) => ({ storeCode: String(1001 + index) })),
  localNames
);
assertEqual(thirtyFiveStores.items.length, 3, "thirty-five stores are capped at three visible items");
assertEqual(thirtyFiveStores.remainingCount, 32, "thirty-five stores expose a +32 remainder");

assertEqual(
  hasSameAdvertisementStoreCodes(["A", " historical-x "], ["a", "B"]),
  false,
  "equal-length scopes with different members are not treated as all selected"
);
assertEqual(
  hasSameAdvertisementStoreCodes([" A ", "b"], ["a", " B "]),
  true,
  "all-selected comparison ignores code case, order, and surrounding whitespace"
);
