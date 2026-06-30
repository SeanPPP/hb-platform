import { QueryClient } from "@tanstack/react-query";
import {
  applyCartMutationResultToCart,
  applyCartAddOptimisticUpdate,
  applyCartQuantityOptimisticUpdate,
  getOptimisticCartMutationCache,
  isCurrentCartStore,
  mergeChangedCartQuantityIntoDynamicData,
  mergeCartQuantityIntoDynamicData,
  reserveCartMutationOperationId,
  resolveCartMutationCache,
  snapshotCartMutationCache,
  syncCartMutationCache,
} from "./cart-cache";
import type { StoreOrderCart, StoreOrderCartMutationResult, StoreOrderDynamicData } from "./types";

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

const afterChangedOnly = mergeChangedCartQuantityIntoDynamicData(dynamicData, "P001", 4);
assertEqual(
  afterChangedOnly?.find((item) => item.productCode === "P001")?.cartQuantity,
  4,
  "轻量响应只同步当前商品数量"
);
assertEqual(
  afterChangedOnly?.find((item) => item.productCode === "P002")?.cartQuantity,
  2,
  "轻量响应不能清零其它商品数量"
);

const originalCart = cartWithItems([
  {
    detailGUID: "D002",
    productCode: "P002",
    quantity: 2,
    price: 0,
    amount: 0,
    importPrice: 4,
    importAmount: 8,
    minOrderQuantity: 1,
    isActive: true,
  },
]);
const originalCartText = JSON.stringify(originalCart);

const optimisticNewSkuCart = applyCartAddOptimisticUpdate(
  originalCart,
  {
    productCode: "P003",
    productName: "新商品",
    oemPrice: 8,
    importPrice: 6,
    minOrderQuantity: 1,
  },
  3
);

assertEqual(optimisticNewSkuCart.items[0]?.productCode, "P003", "乐观新增 SKU 放到购物车首项");
assertEqual(optimisticNewSkuCart.items[0]?.quantity, 3, "乐观新增 SKU 数量正确");
assertEqual(optimisticNewSkuCart.totalQuantity, 5, "乐观新增 SKU 后汇总数量正确");
assertEqual(optimisticNewSkuCart.totalAmount, 24, "乐观新增 SKU 后销售金额正确");
assertEqual(optimisticNewSkuCart.totalImportAmount, 26, "乐观新增 SKU 后进口金额正确");

const optimisticExistingSkuCart = applyCartAddOptimisticUpdate(
  originalCart,
  {
    productCode: "P002",
    importPrice: 4,
    minOrderQuantity: 1,
  },
  3
);

assertEqual(optimisticExistingSkuCart.items.find((item) => item.productCode === "P002")?.quantity, 5, "乐观已有 SKU 累加数量");

const optimisticRemovedSkuCart = applyCartQuantityOptimisticUpdate(
  originalCart,
  {
    productCode: "P002",
    importPrice: 4,
    minOrderQuantity: 1,
  },
  0
);

assertEqual(optimisticRemovedSkuCart?.items.some((item) => item.productCode === "P002"), false, "乐观目标数量 0 时移除 SKU");
assertEqual(optimisticRemovedSkuCart?.totalQuantity, 0, "乐观目标数量 0 后汇总数量清零");

const optimisticFirstItemCart = applyCartAddOptimisticUpdate(
  null,
  {
    productCode: "P004",
    productName: "空车首项",
    oemPrice: 9,
    importPrice: 7,
    minOrderQuantity: 1,
  },
  2
);

assertEqual(optimisticFirstItemCart.items.length, 1, "空购物车乐观添加首项");
assertEqual(optimisticFirstItemCart.items[0]?.quantity, 2, "空购物车首项数量正确");
assertEqual(optimisticFirstItemCart.totalSku, 1, "空购物车首项 SKU 汇总正确");
assertEqual(optimisticFirstItemCart.totalAmount, 18, "空购物车首项销售金额正确");
assertDeepEqual(JSON.parse(originalCartText), originalCart, "乐观更新不原地修改快照 cart");

const mutationAdded: StoreOrderCartMutationResult = {
  productCode: "P003",
  removed: false,
  summary: {
    orderGUID: "cart-1",
    storeCode: "S001",
    totalAmount: 42,
    totalImportAmount: 30,
    totalQuantity: 5,
    totalSku: 2,
  },
  changedItem: {
    detailGUID: "D003",
    productCode: "P003",
    quantity: 3,
    price: 8,
    amount: 24,
    importPrice: 6,
    importAmount: 18,
    minOrderQuantity: 1,
    isActive: true,
  },
};
const cartAfterMutationAdded = applyCartMutationResultToCart(originalCart, mutationAdded);
assertEqual(cartAfterMutationAdded.items[0]?.productCode, "P003", "轻量新增行插入购物车");
assertEqual(cartAfterMutationAdded.totalQuantity, 5, "轻量新增行使用服务端摘要数量");
assertEqual(cartAfterMutationAdded.totalAmount, 42, "轻量新增行使用服务端摘要金额");

const mutationReplaced: StoreOrderCartMutationResult = {
  productCode: "P002",
  removed: false,
  summary: {
    orderGUID: "cart-1",
    storeCode: "S001",
    totalAmount: 20,
    totalImportAmount: 16,
    totalQuantity: 4,
    totalSku: 1,
  },
  changedItem: {
    ...originalCart.items[0],
    quantity: 4,
    amount: 0,
    importAmount: 16,
  },
};
const cartAfterMutationReplaced = applyCartMutationResultToCart(originalCart, mutationReplaced);
assertEqual(
  cartAfterMutationReplaced.items.find((item) => item.productCode === "P002")?.quantity,
  4,
  "轻量变更行替换已有 SKU"
);
assertEqual(cartAfterMutationReplaced.totalSku, 1, "轻量替换行使用服务端 SKU 汇总");

const mutationRemoved: StoreOrderCartMutationResult = {
  productCode: "P002",
  removed: true,
  summary: {
    orderGUID: "cart-1",
    storeCode: "S001",
    totalAmount: 0,
    totalImportAmount: 0,
    totalQuantity: 0,
    totalSku: 0,
  },
  changedItem: null,
};
const cartAfterMutationRemoved = applyCartMutationResultToCart(originalCart, mutationRemoved);
assertEqual(
  cartAfterMutationRemoved.items.some((item) => item.productCode === "P002"),
  false,
  "轻量删除行按 productCode 移除 SKU"
);
assertEqual(cartAfterMutationRemoved.totalQuantity, 0, "轻量删除行使用服务端摘要清零");

const queryClient = new QueryClient();
const queryStoreCode = "S001";
queryClient.setQueryData(["cartSummary", queryStoreCode], originalCart);
queryClient.setQueryData<StoreOrderDynamicData[]>(
  ["shopDynamicData", queryStoreCode, ["P002", "P003", "P004"]],
  [
    { productCode: "P002", cartQuantity: 2 },
    { productCode: "P003", cartQuantity: 0 },
    { productCode: "P004", cartQuantity: 0 },
  ]
);

const firstOperation = snapshotCartMutationCache(queryClient, queryStoreCode, {
  product: {
    productCode: "P003",
    productName: "先失败商品",
    oemPrice: 8,
    importPrice: 6,
    minOrderQuantity: 1,
  },
  quantity: 3,
  type: "add",
});
syncCartMutationCache(
  queryClient,
  queryStoreCode,
  getOptimisticCartMutationCache(queryClient, queryStoreCode),
  "P003"
);

const secondOperation = snapshotCartMutationCache(queryClient, queryStoreCode, {
  product: {
    productCode: "P004",
    productName: "后续商品",
    oemPrice: 9,
    importPrice: 7,
    minOrderQuantity: 1,
  },
  quantity: 2,
  type: "add",
});
syncCartMutationCache(
  queryClient,
  queryStoreCode,
  getOptimisticCartMutationCache(queryClient, queryStoreCode),
  "P004"
);

const afterFirstOperationFailed = resolveCartMutationCache(queryClient, queryStoreCode, firstOperation);
const dynamicDataAfterFirstOperationFailed = queryClient.getQueryData<StoreOrderDynamicData[]>([
  "shopDynamicData",
  queryStoreCode,
  ["P002", "P003", "P004"],
]);

assertEqual(
  afterFirstOperationFailed?.items.some((item) => item.productCode === "P003"),
  false,
  "并发乐观更新中失败操作只移除自己的 SKU"
);
assertEqual(
  afterFirstOperationFailed?.items.find((item) => item.productCode === "P004")?.quantity,
  2,
  "并发乐观更新中后续 SKU 不能被旧失败回滚"
);
assertEqual(
  dynamicDataAfterFirstOperationFailed?.find((item) => item.productCode === "P003")?.cartQuantity,
  0,
  "失败操作回滚后动态数量清空对应 SKU"
);
assertEqual(
  dynamicDataAfterFirstOperationFailed?.find((item) => item.productCode === "P004")?.cartQuantity,
  2,
  "失败操作回滚后保留后续 SKU 动态数量"
);

const confirmedSecondCart = cartWithItems([
  originalCart.items[0],
  {
    detailGUID: "D004",
    productCode: "P004",
    quantity: 2,
    price: 9,
    amount: 18,
    importPrice: 7,
    importAmount: 14,
    minOrderQuantity: 1,
    isActive: true,
  },
]);
const afterSecondOperationSucceeded = resolveCartMutationCache(
  queryClient,
  queryStoreCode,
  secondOperation,
  confirmedSecondCart
);

assertEqual(afterSecondOperationSucceeded?.totalQuantity, 4, "后续成功操作使用服务端购物车校准汇总数量");
queryClient.clear();

const outOfOrderClient = new QueryClient();
outOfOrderClient.setQueryData(["cartSummary", queryStoreCode], originalCart);
const olderAddOperation = snapshotCartMutationCache(outOfOrderClient, queryStoreCode, {
  product: {
    productCode: "P003",
    productName: "后到确认商品",
    oemPrice: 8,
    importPrice: 6,
    minOrderQuantity: 1,
  },
  quantity: 1,
  type: "add",
});
syncCartMutationCache(
  outOfOrderClient,
  queryStoreCode,
  getOptimisticCartMutationCache(outOfOrderClient, queryStoreCode),
  "P003"
);
const newerAddOperation = snapshotCartMutationCache(outOfOrderClient, queryStoreCode, {
  product: {
    productCode: "P004",
    productName: "先到确认商品",
    oemPrice: 9,
    importPrice: 7,
    minOrderQuantity: 1,
  },
  quantity: 1,
  type: "add",
});
syncCartMutationCache(
  outOfOrderClient,
  queryStoreCode,
  getOptimisticCartMutationCache(outOfOrderClient, queryStoreCode),
  "P004"
);
resolveCartMutationCache(outOfOrderClient, queryStoreCode, newerAddOperation, {
  productCode: "P004",
  removed: false,
  summary: {
    orderGUID: "cart-1",
    storeCode: queryStoreCode,
    totalAmount: 17,
    totalImportAmount: 13,
    totalQuantity: 3,
    totalSku: 2,
  },
  changedItem: {
    detailGUID: "D004",
    productCode: "P004",
    quantity: 1,
    price: 9,
    amount: 9,
    importPrice: 7,
    importAmount: 7,
    minOrderQuantity: 1,
    isActive: true,
  },
});
const afterOlderConfirmation = resolveCartMutationCache(outOfOrderClient, queryStoreCode, olderAddOperation, {
  productCode: "P003",
  removed: false,
  summary: {
    orderGUID: "cart-1",
    storeCode: queryStoreCode,
    totalAmount: 25,
    totalImportAmount: 19,
    totalQuantity: 4,
    totalSku: 3,
  },
  changedItem: {
    detailGUID: "D003",
    productCode: "P003",
    quantity: 1,
    price: 8,
    amount: 8,
    importPrice: 6,
    importAmount: 6,
    minOrderQuantity: 1,
    isActive: true,
  },
});
assertEqual(afterOlderConfirmation?.totalQuantity, 4, "并发加购旧响应更完整时仍推进服务端汇总数量");
outOfOrderClient.clear();

const coldOutOfOrderClient = new QueryClient();
coldOutOfOrderClient.setQueryData(["cartSummary", queryStoreCode], originalCart);
const olderColdOperationId = reserveCartMutationOperationId();
const newerColdOperationId = reserveCartMutationOperationId();
const newerColdSnapshot = snapshotCartMutationCache(
  coldOutOfOrderClient,
  queryStoreCode,
  {
    product: {
      productCode: "P004",
      productName: "先到冷扫码确认",
      oemPrice: 9,
      importPrice: 7,
      minOrderQuantity: 1,
    },
    quantity: 1,
    type: "add",
  },
  newerColdOperationId
);
resolveCartMutationCache(coldOutOfOrderClient, queryStoreCode, newerColdSnapshot, {
  productCode: "P004",
  removed: false,
  summary: {
    orderGUID: "cart-1",
    storeCode: queryStoreCode,
    totalAmount: 17,
    totalImportAmount: 15,
    totalQuantity: 4,
    totalSku: 2,
  },
  changedItem: {
    detailGUID: "D004",
    productCode: "P004",
    quantity: 1,
    price: 9,
    amount: 9,
    importPrice: 7,
    importAmount: 7,
    minOrderQuantity: 1,
    isActive: true,
  },
});
const olderColdSnapshot = snapshotCartMutationCache(
  coldOutOfOrderClient,
  queryStoreCode,
  {
    product: {
      productCode: "P003",
      productName: "后到旧冷扫码确认",
      oemPrice: 8,
      importPrice: 6,
      minOrderQuantity: 1,
    },
    quantity: 1,
    type: "add",
  },
  olderColdOperationId
);
const afterOlderColdConfirmation = resolveCartMutationCache(
  coldOutOfOrderClient,
  queryStoreCode,
  olderColdSnapshot,
  {
    productCode: "P003",
    removed: false,
    summary: {
      orderGUID: "cart-1",
      storeCode: queryStoreCode,
      totalAmount: 16,
      totalImportAmount: 14,
      totalQuantity: 3,
      totalSku: 2,
    },
    changedItem: {
      detailGUID: "D003",
      productCode: "P003",
      quantity: 1,
      price: 8,
      amount: 8,
      importPrice: 6,
      importAmount: 6,
      minOrderQuantity: 1,
      isActive: true,
    },
  }
);
assertEqual(afterOlderColdConfirmation?.totalQuantity, 4, "冷扫码旧确认不能把已显示数量回退");
assertEqual(
  afterOlderColdConfirmation?.items.some((item) => item.productCode === "P004"),
  true,
  "冷扫码旧确认不能覆盖先到的新服务端行"
);
assertEqual(
  afterOlderColdConfirmation?.items.some((item) => item.productCode === "P003"),
  true,
  "冷扫码旧确认被拒绝 summary 时仍要合并自己的变更行"
);
coldOutOfOrderClient.clear();

const staleSameSkuClient = new QueryClient();
staleSameSkuClient.setQueryData(["cartSummary", queryStoreCode], originalCart);
const olderSameSkuOperationId = reserveCartMutationOperationId();
const newerSameSkuOperationId = reserveCartMutationOperationId();
const newerSameSkuSnapshot = snapshotCartMutationCache(
  staleSameSkuClient,
  queryStoreCode,
  {
    product: {
      productCode: "P003",
      productName: "同 SKU 先到新确认",
      oemPrice: 8,
      importPrice: 6,
      minOrderQuantity: 1,
    },
    quantity: 1,
    type: "add",
  },
  newerSameSkuOperationId
);
resolveCartMutationCache(staleSameSkuClient, queryStoreCode, newerSameSkuSnapshot, {
  productCode: "P003",
  removed: false,
  summary: {
    orderGUID: "cart-1",
    storeCode: queryStoreCode,
    totalAmount: 24,
    totalImportAmount: 18,
    totalQuantity: 4,
    totalSku: 2,
  },
  changedItem: {
    detailGUID: "D003",
    productCode: "P003",
    quantity: 2,
    price: 8,
    amount: 16,
    importPrice: 6,
    importAmount: 12,
    minOrderQuantity: 1,
    isActive: true,
  },
});
const olderSameSkuSnapshot = snapshotCartMutationCache(
  staleSameSkuClient,
  queryStoreCode,
  {
    product: {
      productCode: "P003",
      productName: "同 SKU 后到旧确认",
      oemPrice: 8,
      importPrice: 6,
      minOrderQuantity: 1,
    },
    quantity: 1,
    type: "add",
  },
  olderSameSkuOperationId
);
const afterOlderSameSkuConfirmation = resolveCartMutationCache(
  staleSameSkuClient,
  queryStoreCode,
  olderSameSkuSnapshot,
  {
    productCode: "P003",
    removed: false,
    summary: {
      orderGUID: "cart-1",
      storeCode: queryStoreCode,
      totalAmount: 16,
      totalImportAmount: 12,
      totalQuantity: 3,
      totalSku: 2,
    },
    changedItem: {
      detailGUID: "D003",
      productCode: "P003",
      quantity: 1,
      price: 8,
      amount: 8,
      importPrice: 6,
      importAmount: 6,
      minOrderQuantity: 1,
      isActive: true,
    },
  }
);
assertEqual(
  afterOlderSameSkuConfirmation?.items.find((item) => item.productCode === "P003")?.quantity,
  2,
  "冷扫码同 SKU 旧确认不能把当前行数量回退"
);
staleSameSkuClient.clear();

const staleAddAfterDeleteClient = new QueryClient();
staleAddAfterDeleteClient.setQueryData(["cartSummary", queryStoreCode], originalCart);
const staleAddSnapshot = snapshotCartMutationCache(staleAddAfterDeleteClient, queryStoreCode, {
  product: {
    productCode: "P002",
    importPrice: 4,
    minOrderQuantity: 1,
  },
  quantity: 1,
  type: "add",
});
syncCartMutationCache(
  staleAddAfterDeleteClient,
  queryStoreCode,
  getOptimisticCartMutationCache(staleAddAfterDeleteClient, queryStoreCode),
  "P002"
);
const deleteSnapshot = snapshotCartMutationCache(staleAddAfterDeleteClient, queryStoreCode, {
  product: {
    productCode: "P002",
    importPrice: 4,
    minOrderQuantity: 1,
  },
  quantity: 0,
  type: "set",
});
syncCartMutationCache(
  staleAddAfterDeleteClient,
  queryStoreCode,
  getOptimisticCartMutationCache(staleAddAfterDeleteClient, queryStoreCode),
  "P002"
);
const afterDeleteConfirmation = resolveCartMutationCache(
  staleAddAfterDeleteClient,
  queryStoreCode,
  deleteSnapshot,
  mutationRemoved
);
assertEqual(afterDeleteConfirmation?.totalQuantity, 0, "后续删除成功后应移除同 SKU 旧 add 乐观操作");
const afterStaleAddConfirmation = resolveCartMutationCache(
  staleAddAfterDeleteClient,
  queryStoreCode,
  staleAddSnapshot,
  {
    productCode: "P002",
    removed: false,
    summary: {
      orderGUID: "cart-1",
      storeCode: queryStoreCode,
      totalAmount: 12,
      totalImportAmount: 12,
      totalQuantity: 3,
      totalSku: 1,
    },
    changedItem: {
      ...originalCart.items[0],
      quantity: 3,
      importAmount: 12,
    },
  }
);
assertEqual(afterStaleAddConfirmation?.totalQuantity, 0, "旧 add 晚到不能覆盖后续删除");
assertEqual(
  afterStaleAddConfirmation?.items.some((item) => item.productCode === "P002"),
  false,
  "旧 add 晚到不能把已删除 SKU 加回来"
);
staleAddAfterDeleteClient.clear();

const differentSkuAddAfterDeleteClient = new QueryClient();
const differentSkuStoreCode = "S-DIFF-SKU";
differentSkuAddAfterDeleteClient.setQueryData(["cartSummary", differentSkuStoreCode], originalCart);
const differentSkuAddSnapshot = snapshotCartMutationCache(differentSkuAddAfterDeleteClient, differentSkuStoreCode, {
  product: {
    productCode: "P003",
    importPrice: 6,
    minOrderQuantity: 1,
  },
  quantity: 1,
  type: "add",
});
const differentSkuDeleteSnapshot = snapshotCartMutationCache(differentSkuAddAfterDeleteClient, differentSkuStoreCode, {
  product: {
    productCode: "P002",
    importPrice: 4,
    minOrderQuantity: 1,
  },
  quantity: 0,
  type: "set",
});
resolveCartMutationCache(differentSkuAddAfterDeleteClient, differentSkuStoreCode, differentSkuDeleteSnapshot, {
  ...mutationRemoved,
  summary: {
    ...mutationRemoved.summary,
    storeCode: differentSkuStoreCode,
  },
});
const afterDifferentSkuAddConfirmation = resolveCartMutationCache(
  differentSkuAddAfterDeleteClient,
  differentSkuStoreCode,
  differentSkuAddSnapshot,
  {
    productCode: "P003",
    removed: false,
    summary: {
      orderGUID: "cart-1",
      storeCode: differentSkuStoreCode,
      totalAmount: 16,
      totalImportAmount: 14,
      totalQuantity: 3,
      totalSku: 2,
    },
    changedItem: {
      detailGUID: "D003",
      productCode: "P003",
      quantity: 1,
      price: 8,
      amount: 8,
      importPrice: 6,
      importAmount: 6,
      minOrderQuantity: 1,
      isActive: true,
    },
  }
);
assertEqual(
  afterDifferentSkuAddConfirmation?.items.some((item) => item.productCode === "P003"),
  true,
  "不同 SKU 的删除确认不能吞掉旧序号但有效的加购"
);
assertEqual(afterDifferentSkuAddConfirmation?.totalQuantity, 1, "旧 add 不能用删除前的整车汇总污染顶部数量");
assertEqual(afterDifferentSkuAddConfirmation?.totalSku, 1, "旧 add 不能用删除前的整车汇总污染顶部 SKU 数");
assertEqual(
  afterDifferentSkuAddConfirmation?.items.some((item) => item.productCode === "P002"),
  false,
  "不同 SKU 旧加购确认不能复活已删除商品"
);
differentSkuAddAfterDeleteClient.clear();

const staleAddAfterOutOfOrderDeleteClient = new QueryClient();
staleAddAfterOutOfOrderDeleteClient.setQueryData(["cartSummary", queryStoreCode], originalCart);
const firstAddSnapshot = snapshotCartMutationCache(staleAddAfterOutOfOrderDeleteClient, queryStoreCode, {
  product: {
    productCode: "P002",
    importPrice: 4,
    minOrderQuantity: 1,
  },
  quantity: 1,
  type: "add",
});
const middleDeleteSnapshot = snapshotCartMutationCache(staleAddAfterOutOfOrderDeleteClient, queryStoreCode, {
  product: {
    productCode: "P002",
    importPrice: 4,
    minOrderQuantity: 1,
  },
  quantity: 0,
  type: "set",
});
const laterAddSnapshot = snapshotCartMutationCache(staleAddAfterOutOfOrderDeleteClient, queryStoreCode, {
  product: {
    productCode: "P004",
    importPrice: 7,
    minOrderQuantity: 1,
  },
  quantity: 1,
  type: "add",
});
resolveCartMutationCache(staleAddAfterOutOfOrderDeleteClient, queryStoreCode, laterAddSnapshot, {
  productCode: "P004",
  removed: false,
  summary: {
    orderGUID: "cart-1",
    storeCode: queryStoreCode,
    totalAmount: 9,
    totalImportAmount: 7,
    totalQuantity: 1,
    totalSku: 1,
  },
  changedItem: {
    detailGUID: "D004",
    productCode: "P004",
    quantity: 1,
    price: 9,
    amount: 9,
    importPrice: 7,
    importAmount: 7,
    minOrderQuantity: 1,
    isActive: true,
  },
});
resolveCartMutationCache(
  staleAddAfterOutOfOrderDeleteClient,
  queryStoreCode,
  middleDeleteSnapshot,
  mutationRemoved
);
const afterOutOfOrderStaleAdd = resolveCartMutationCache(
  staleAddAfterOutOfOrderDeleteClient,
  queryStoreCode,
  firstAddSnapshot,
  {
    productCode: "P002",
    removed: false,
    summary: {
      orderGUID: "cart-1",
      storeCode: queryStoreCode,
      totalAmount: 18,
      totalImportAmount: 11,
      totalQuantity: 4,
      totalSku: 2,
    },
    changedItem: {
      ...originalCart.items[0],
      quantity: 3,
      importAmount: 12,
    },
  }
);
assertEqual(
  afterOutOfOrderStaleAdd?.items.some((item) => item.productCode === "P002"),
  false,
  "后续删除即使乱序被新响应压住，也应阻止旧 add 复活 SKU"
);
assertEqual(
  afterOutOfOrderStaleAdd?.items.some((item) => item.productCode === "P004"),
  true,
  "后续删除乱序时不能丢掉已确认的新加购"
);
staleAddAfterOutOfOrderDeleteClient.clear();

const sameSkuAddAfterOutOfOrderDeleteClient = new QueryClient();
sameSkuAddAfterOutOfOrderDeleteClient.setQueryData(["cartSummary", queryStoreCode], originalCart);
const sameSkuOldAddSnapshot = snapshotCartMutationCache(sameSkuAddAfterOutOfOrderDeleteClient, queryStoreCode, {
  product: {
    productCode: "P002",
    importPrice: 4,
    minOrderQuantity: 1,
  },
  quantity: 1,
  type: "add",
});
const sameSkuDeleteSnapshot = snapshotCartMutationCache(sameSkuAddAfterOutOfOrderDeleteClient, queryStoreCode, {
  product: {
    productCode: "P002",
    importPrice: 4,
    minOrderQuantity: 1,
  },
  quantity: 0,
  type: "set",
});
const sameSkuLaterAddSnapshot = snapshotCartMutationCache(sameSkuAddAfterOutOfOrderDeleteClient, queryStoreCode, {
  product: {
    productCode: "P002",
    importPrice: 4,
    minOrderQuantity: 1,
  },
  quantity: 1,
  type: "add",
});
resolveCartMutationCache(sameSkuAddAfterOutOfOrderDeleteClient, queryStoreCode, sameSkuLaterAddSnapshot, {
  productCode: "P002",
  removed: false,
  summary: {
    orderGUID: "cart-1",
    storeCode: queryStoreCode,
    totalAmount: 3,
    totalImportAmount: 4,
    totalQuantity: 1,
    totalSku: 1,
  },
  changedItem: {
    ...originalCart.items[0],
    quantity: 1,
    importAmount: 4,
  },
});
resolveCartMutationCache(
  sameSkuAddAfterOutOfOrderDeleteClient,
  queryStoreCode,
  sameSkuDeleteSnapshot,
  mutationRemoved
);
const afterSameSkuOutOfOrderStaleAdd = resolveCartMutationCache(
  sameSkuAddAfterOutOfOrderDeleteClient,
  queryStoreCode,
  sameSkuOldAddSnapshot,
  {
    productCode: "P002",
    removed: false,
    summary: {
      orderGUID: "cart-1",
      storeCode: queryStoreCode,
      totalAmount: 6,
      totalImportAmount: 8,
      totalQuantity: 2,
      totalSku: 1,
    },
    changedItem: {
      ...originalCart.items[0],
      quantity: 2,
      importAmount: 8,
    },
  }
);
assertEqual(
  afterSameSkuOutOfOrderStaleAdd?.items.find((item) => item.productCode === "P002")?.quantity,
  1,
  "同 SKU 后续加购已确认时，旧删除和旧 add 都不能覆盖新数量"
);
sameSkuAddAfterOutOfOrderDeleteClient.clear();

const rollbackWithoutCartClient = new QueryClient();
const rollbackStoreCode = "S002";
rollbackWithoutCartClient.setQueryData<StoreOrderDynamicData[]>(
  ["shopDynamicData", rollbackStoreCode, ["P001"]],
  [{ productCode: "P001", cartQuantity: 5 }]
);
const rollbackOperation = snapshotCartMutationCache(rollbackWithoutCartClient, rollbackStoreCode, {
  product: {
    productCode: "P001",
    importPrice: 2,
    minOrderQuantity: 1,
  },
  quantity: 6,
  type: "set",
});
syncCartMutationCache(
  rollbackWithoutCartClient,
  rollbackStoreCode,
  getOptimisticCartMutationCache(rollbackWithoutCartClient, rollbackStoreCode),
  "P001"
);
assertEqual(
  rollbackWithoutCartClient
    .getQueryData<StoreOrderDynamicData[]>(["shopDynamicData", rollbackStoreCode, ["P001"]])
    ?.find((item) => item.productCode === "P001")?.cartQuantity,
  6,
  "cartSummary 未加载时仍能先显示乐观数量"
);
resolveCartMutationCache(rollbackWithoutCartClient, rollbackStoreCode, rollbackOperation);
assertEqual(
  rollbackWithoutCartClient
    .getQueryData<StoreOrderDynamicData[]>(["shopDynamicData", rollbackStoreCode, ["P001"]])
    ?.find((item) => item.productCode === "P001")?.cartQuantity,
  5,
  "cartSummary 未加载时失败回滚恢复原动态数量"
);
rollbackWithoutCartClient.clear();
