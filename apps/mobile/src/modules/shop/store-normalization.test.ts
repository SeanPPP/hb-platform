import {
  normalizeShopStores,
  normalizeShopStoresApiResponse,
  sortShopStores,
} from "./store-normalization";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const accessibleStores = normalizeShopStores({
  items: [
    {
      BranchCode: "1004",
      BranchName: "HB BRISBANE",
      BranchGUID: "branch-1004",
    },
    {
      StoreCode: "1006",
      StoreName: "HB WARE HOUSE",
      StoreGUID: "store-1006",
    },
    {
      BranchCode: "",
      BranchName: "Missing code",
    },
  ],
});

assertEqual(accessibleStores.length, 2, "stores without a code are ignored");
assertEqual(accessibleStores[0]?.storeCode, "1004", "branch code is normalized as storeCode");
assertEqual(accessibleStores[0]?.storeName, "HB BRISBANE", "branch name is normalized as storeName");
assertEqual(accessibleStores[0]?.storeGUID, "branch-1004", "branch guid is normalized as storeGUID");
assertEqual(accessibleStores[1]?.storeCode, "1006", "store code is still supported");

const sortedStores = sortShopStores([
  { storeCode: "B", storeName: "Zulu" },
  { storeCode: "A", storeName: "Alpha" },
]);

assertEqual(sortedStores[0]?.storeCode, "A", "stores are sorted by display name");

const camelCaseApiStores = normalizeShopStoresApiResponse({
  data: {
    items: [{ StoreCode: "2001", StoreName: "North" }],
  },
});

assertEqual(
  camelCaseApiStores[0]?.storeCode,
  "2001",
  "ApiResponse.data is unwrapped before store normalization"
);

const pascalCaseApiStores = normalizeShopStoresApiResponse({
  Data: {
    Items: [{ StoreCode: "2002", StoreName: "South" }],
  },
});

assertEqual(
  pascalCaseApiStores[0]?.storeCode,
  "2002",
  "ApiResponse.Data is unwrapped before store normalization"
);
