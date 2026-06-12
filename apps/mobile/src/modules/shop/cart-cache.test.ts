import { isCurrentCartStore, mergeCartQuantityIntoDynamicData } from "./cart-cache";
import type { StoreOrderCart, StoreOrderDynamicData } from "./types";

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

function cartWithItems(items: StoreOrderCart["items"]): StoreOrderCart {
  return {
    orderGUID: "cart-1",
    totalAmount: 0,
    totalQuantity: items.reduce((sum, item) => sum + item.quantity, 0),
    totalImportAmount: 0,
    totalSku: items.length,
    totalVolume: 0,
    items,
  };
}

const dynamicData: StoreOrderDynamicData[] = [
  { productCode: "P001", cartQuantity: 0 },
  { productCode: "P002", cartQuantity: 2 },
];

const afterAddingNewSku = mergeCartQuantityIntoDynamicData(
  dynamicData,
  cartWithItems([
    {
      detailGUID: "D001",
      productCode: "P001",
      quantity: 3,
      price: 0,
      amount: 0,
      importPrice: 0,
      importAmount: 0,
      minOrderQuantity: 1,
      isActive: true,
    },
  ])
);

assertEqual(afterAddingNewSku?.find((item) => item.productCode === "P001")?.cartQuantity, 3, "新增 SKU 后同步购物车数量");

const afterIncreasingExistingSku = mergeCartQuantityIntoDynamicData(
  dynamicData,
  cartWithItems([
    {
      detailGUID: "D002",
      productCode: "P002",
      quantity: 5,
      price: 0,
      amount: 0,
      importPrice: 0,
      importAmount: 0,
      minOrderQuantity: 1,
      isActive: true,
    },
  ])
);

assertEqual(
  afterIncreasingExistingSku?.find((item) => item.productCode === "P002")?.cartQuantity,
  5,
  "已有 SKU 增量后同步购物车数量"
);

const afterRemovingSku = mergeCartQuantityIntoDynamicData(dynamicData, cartWithItems([]));

assertEqual(afterRemovingSku?.find((item) => item.productCode === "P002")?.cartQuantity, 0, "数量更新为 0 后同步清空");
assertDeepEqual(dynamicData.map((item) => item.cartQuantity), [0, 2], "不直接修改原始动态数据数组");
assertEqual(isCurrentCartStore(" 1024 ", "1024"), true, "当前门店匹配时允许写全局购物车");
assertEqual(isCurrentCartStore("1024", "1001"), false, "门店切换后跳过旧请求的全局购物车写入");
