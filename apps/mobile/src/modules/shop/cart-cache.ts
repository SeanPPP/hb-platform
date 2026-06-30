import type { QueryClient } from "@tanstack/react-query";
import type {
  StoreOrderCart,
  StoreOrderCartItem,
  StoreOrderCartMutationResult,
  StoreOrderDynamicData,
} from "@/modules/shop/types";

interface CartCacheProduct {
  barcode?: string;
  grade?: string;
  importPrice?: number;
  itemNumber?: string;
  minOrderQuantity?: number;
  oemPrice?: number;
  price?: number;
  productCode: string;
  productImage?: string;
  productName?: string;
}

export interface CartMutationCacheSnapshot {
  operationId: number;
  previousDynamicCartQuantity?: number;
  productCode: string;
  storeKey: string;
}

type CartOptimisticOperation =
  | {
      id: number;
      product: CartCacheProduct;
      quantity: number;
      type: "add";
    }
  | {
      id: number;
      product: CartCacheProduct;
      quantity: number;
      type: "set";
    };

interface CartOptimisticState {
  baseCart: StoreOrderCart | null;
  lastConfirmedOperationId: number;
  operations: CartOptimisticOperation[];
}

let nextOptimisticOperationId = 1;
const optimisticStates = new Map<string, CartOptimisticState>();

function normalizeStoreCode(storeCode?: string | null) {
  const normalized = storeCode?.trim();
  return normalized ? normalized : null;
}

function getStoreKey(storeCode: string) {
  return normalizeStoreCode(storeCode) ?? storeCode;
}

export function isCurrentCartStore(currentStoreCode?: string | null, mutationStoreCode?: string | null) {
  return normalizeStoreCode(currentStoreCode) === normalizeStoreCode(mutationStoreCode);
}

export function mergeCartQuantityIntoDynamicData(
  currentData: StoreOrderDynamicData[] | undefined,
  cart: StoreOrderCart | null
): StoreOrderDynamicData[] | undefined {
  if (!currentData) {
    return currentData;
  }

  const quantityByProductCode = new Map(
    (cart?.items ?? []).map((item) => [item.productCode, item.quantity])
  );

  return currentData.map((item) => ({
    ...item,
    cartQuantity: quantityByProductCode.get(item.productCode) ?? 0,
  }));
}

export function mergeChangedCartQuantityIntoDynamicData(
  currentData: StoreOrderDynamicData[] | undefined,
  productCode: string,
  quantity: number
): StoreOrderDynamicData[] | undefined {
  if (!currentData) {
    return currentData;
  }

  return currentData.map((item) =>
    item.productCode === productCode
      ? {
          ...item,
          cartQuantity: quantity,
        }
      : item
  );
}

export function isCartMutationResult(
  value: StoreOrderCart | StoreOrderCartMutationResult | null | undefined
): value is StoreOrderCartMutationResult {
  return Boolean(value && "summary" in value && "productCode" in value);
}

function getCurrentDynamicCartQuantity(
  queryClient: QueryClient,
  storeCode: string,
  productCode: string
) {
  const dynamicDataQueries = queryClient.getQueriesData<StoreOrderDynamicData[]>({
    queryKey: ["shopDynamicData", storeCode],
  });

  for (const [, currentData] of dynamicDataQueries) {
    const currentItem = currentData?.find((item) => item.productCode === productCode);
    if (currentItem) {
      return currentItem.cartQuantity;
    }
  }

  return undefined;
}

function getFiniteNumber(value: unknown, fallback = 0) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function createOptimisticCartItem(product: CartCacheProduct, quantity: number): StoreOrderCartItem {
  const importPrice = getFiniteNumber(product.importPrice);
  const price = getFiniteNumber(product.price, getFiniteNumber(product.oemPrice));

  return {
    detailGUID: `optimistic-${product.productCode}`,
    productCode: product.productCode,
    itemNumber: product.itemNumber,
    barcode: product.barcode,
    grade: product.grade,
    productName: product.productName,
    productImage: product.productImage,
    price,
    quantity,
    amount: price * quantity,
    importPrice,
    importAmount: importPrice * quantity,
    minOrderQuantity: product.minOrderQuantity && product.minOrderQuantity > 0 ? product.minOrderQuantity : 1,
    isActive: true,
    updatedAt: new Date().toISOString(),
  };
}

function withQuantityTotals(cart: StoreOrderCart, items: StoreOrderCartItem[]): StoreOrderCart {
  const totalQuantity = items.reduce((sum, item) => sum + item.quantity, 0);
  const totalAmount = items.reduce(
    (sum, item) => sum + getFiniteNumber(item.amount, getFiniteNumber(item.price) * item.quantity),
    0
  );
  const totalImportAmount = items.reduce(
    (sum, item) => sum + getFiniteNumber(item.importAmount, getFiniteNumber(item.importPrice) * item.quantity),
    0
  );

  return {
    ...cart,
    items,
    totalAmount,
    totalQuantity,
    totalImportAmount,
    totalSku: new Set(items.map((item) => item.productCode)).size,
  };
}

export function applyCartMutationResultToCart(
  currentCart: StoreOrderCart | null | undefined,
  result: StoreOrderCartMutationResult
): StoreOrderCart {
  const productCode = result.productCode || result.changedItem?.productCode || "";
  let items = [...(currentCart?.items ?? [])];

  if (result.removed) {
    items = items.filter((item) => item.productCode !== productCode);
  } else if (result.changedItem) {
    let found = false;
    items = items.map((item) => {
      const isSameDetail =
        result.changedItem?.detailGUID && item.detailGUID === result.changedItem.detailGUID;
      const isSameProduct = item.productCode === result.changedItem?.productCode;
      if (!isSameDetail && !isSameProduct) {
        return item;
      }

      found = true;
      return result.changedItem!;
    });

    if (!found) {
      items.unshift(result.changedItem);
    }
  }

  // 服务端轻量摘要是权威汇总；本地只负责合并当前变更行，避免等待整车明细 reload。
  return {
    ...(currentCart ?? {
      orderGUID: "",
      totalAmount: 0,
      totalQuantity: 0,
      totalImportAmount: 0,
      totalSku: 0,
      totalVolume: 0,
      items: [],
    }),
    orderGUID: result.summary.orderGUID || currentCart?.orderGUID || "",
    storeCode: result.summary.storeCode ?? currentCart?.storeCode,
    totalAmount: result.summary.totalAmount,
    totalQuantity: result.summary.totalQuantity,
    totalImportAmount: result.summary.totalImportAmount,
    totalSku: result.summary.totalSku,
    items,
  };
}

function getOrCreateOptimisticState(storeCode: string, currentCart: StoreOrderCart | null | undefined) {
  const storeKey = getStoreKey(storeCode);
  const existingState = optimisticStates.get(storeKey);
  if (existingState) {
    return { state: existingState, storeKey };
  }

  const state: CartOptimisticState = {
    baseCart: currentCart ?? null,
    lastConfirmedOperationId: 0,
    operations: [],
  };
  optimisticStates.set(storeKey, state);
  return { state, storeKey };
}

function applyCartOptimisticOperations(baseCart: StoreOrderCart | null, operations: CartOptimisticOperation[]) {
  return operations.reduce<StoreOrderCart | null>((cart, operation) => {
    if (operation.type === "add") {
      return applyCartAddOptimisticUpdate(cart, operation.product, operation.quantity);
    }

    return applyCartQuantityOptimisticUpdate(cart, operation.product, operation.quantity) ?? null;
  }, baseCart);
}

export function applyCartAddOptimisticUpdate(
  currentCart: StoreOrderCart | null | undefined,
  product: CartCacheProduct,
  quantity: number
): StoreOrderCart {
  const currentItems = currentCart?.items ?? [];
  let found = false;
  const items = currentItems.map((item) => {
    if (item.productCode !== product.productCode) {
      return item;
    }

    found = true;
    const nextQuantity = item.quantity + quantity;
    return {
      ...item,
      quantity: nextQuantity,
      amount: getFiniteNumber(item.price) * nextQuantity,
      importAmount: getFiniteNumber(item.importPrice, getFiniteNumber(product.importPrice)) * nextQuantity,
      updatedAt: new Date().toISOString(),
    };
  });

  if (!found) {
    items.unshift(createOptimisticCartItem(product, quantity));
  }

  // 乐观加购要能覆盖空购物车首项，因此没有服务端 cart 时创建最小 cart 形状。
  return withQuantityTotals(
    currentCart ?? {
      orderGUID: "",
      totalAmount: 0,
      totalQuantity: 0,
      totalImportAmount: 0,
      totalSku: 0,
      totalVolume: 0,
      items: [],
    },
    items
  );
}

export function applyCartQuantityOptimisticUpdate(
  currentCart: StoreOrderCart | null | undefined,
  product: CartCacheProduct,
  nextQuantity: number
): StoreOrderCart | null | undefined {
  if (!currentCart) {
    return nextQuantity > 0 ? applyCartAddOptimisticUpdate(currentCart, product, nextQuantity) : currentCart;
  }

  let found = false;
  const items = currentCart.items
    .map((item) => {
      if (item.productCode !== product.productCode) {
        return item;
      }

      found = true;
      return {
        ...item,
        quantity: nextQuantity,
        amount: getFiniteNumber(item.price) * nextQuantity,
        importAmount: getFiniteNumber(item.importPrice, getFiniteNumber(product.importPrice)) * nextQuantity,
        updatedAt: new Date().toISOString(),
      };
    })
    .filter((item) => item.quantity > 0);

  if (!found && nextQuantity > 0) {
    items.unshift(createOptimisticCartItem(product, nextQuantity));
  }

  return withQuantityTotals(currentCart, items);
}

export function snapshotCartMutationCache(
  queryClient: QueryClient,
  storeCode: string,
  operation: Omit<CartOptimisticOperation, "id">
): CartMutationCacheSnapshot {
  const currentCart = queryClient.getQueryData<StoreOrderCart | null>(["cartSummary", storeCode]);
  const { state, storeKey } = getOrCreateOptimisticState(storeCode, currentCart);
  const previousDynamicCartQuantity = getCurrentDynamicCartQuantity(
    queryClient,
    storeCode,
    operation.product.productCode
  );
  const snapshot: CartMutationCacheSnapshot = {
    operationId: nextOptimisticOperationId++,
    productCode: operation.product.productCode,
    storeKey,
  };
  if (previousDynamicCartQuantity !== undefined) {
    // 失败回滚时 cartSummary 可能尚未加载，需保留列表当前商品原有数量。
    snapshot.previousDynamicCartQuantity = previousDynamicCartQuantity;
  }

  state.operations.push({
    ...operation,
    id: snapshot.operationId,
  });

  return snapshot;
}

export function resolveCartMutationCache(
  queryClient: QueryClient,
  storeCode: string,
  snapshot: CartMutationCacheSnapshot,
  confirmedCart?: StoreOrderCart | StoreOrderCartMutationResult | null
) {
  const { state, storeKey } = getOrCreateOptimisticState(storeCode, undefined);
  state.operations = state.operations.filter((operation) => operation.id !== snapshot.operationId);
  // 同门店购物车 mutation 已用 TanStack scope 串行；这里仅防止异常旧响应覆盖较新的确认基准。
  if (confirmedCart !== undefined && snapshot.operationId >= state.lastConfirmedOperationId) {
    state.baseCart = isCartMutationResult(confirmedCart)
      ? applyCartMutationResultToCart(state.baseCart, confirmedCart)
      : confirmedCart;
    state.lastConfirmedOperationId = snapshot.operationId;
  }

  const nextCart = applyCartOptimisticOperations(state.baseCart, state.operations);
  if (state.operations.length === 0) {
    optimisticStates.delete(storeKey);
  }
  const changedProductCode = isCartMutationResult(confirmedCart)
    ? confirmedCart.productCode
    : confirmedCart === undefined
      ? snapshot.productCode
      : undefined;
  const hasPendingSameProductOperation = state.operations.some(
    (operation) => operation.product.productCode === snapshot.productCode
  );
  const changedProductQuantity =
    confirmedCart === undefined && !hasPendingSameProductOperation
      ? snapshot.previousDynamicCartQuantity
      : undefined;
  syncCartMutationCache(
    queryClient,
    storeCode,
    nextCart,
    changedProductCode,
    changedProductQuantity
  );
  return nextCart;
}

export function getOptimisticCartMutationCache(
  queryClient: QueryClient,
  storeCode: string
) {
  const { state } = getOrCreateOptimisticState(storeCode, queryClient.getQueryData(["cartSummary", storeCode]));
  return applyCartOptimisticOperations(state.baseCart, state.operations);
}

export function syncCartMutationCache(
  queryClient: QueryClient,
  storeCode: string,
  cart: StoreOrderCart | null,
  changedProductCode?: string,
  changedProductQuantity?: number
) {
  queryClient.setQueryData(["cartSummary", storeCode], cart);
  if (changedProductCode) {
    const changedQuantity =
      changedProductQuantity
      ?? cart?.items.find((item) => item.productCode === changedProductCode)?.quantity
      ?? 0;
    queryClient.setQueriesData<StoreOrderDynamicData[]>(
      { queryKey: ["shopDynamicData", storeCode] },
      (currentData) =>
        mergeChangedCartQuantityIntoDynamicData(currentData, changedProductCode, changedQuantity)
    );
    return;
  }

  queryClient.setQueriesData<StoreOrderDynamicData[]>(
    { queryKey: ["shopDynamicData", storeCode] },
    (currentData) => mergeCartQuantityIntoDynamicData(currentData, cart)
  );
}
