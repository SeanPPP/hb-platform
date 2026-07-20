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
  operationType: CartOptimisticOperation["type"];
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
  lastConfirmedCartRevision: number;
  lastConfirmedOperationId: number;
  lastConfirmedProductOperationIds: Map<string, number>;
  lastConfirmedProductRevisions: Map<string, number>;
  lastConfirmedProductSetOperationIds: Map<string, number>;
  lastConfirmedProductSetRevisions: Map<string, number>;
  operations: CartOptimisticOperation[];
}

let nextOptimisticOperationId = 1;
const optimisticStates = new Map<string, CartOptimisticState>();
const lastConfirmedCartRevisions = new Map<string, number>();
const lastConfirmedOperationIds = new Map<string, number>();
const lastConfirmedProductOperationIds = new Map<string, Map<string, number>>();
const lastConfirmedProductRevisions = new Map<string, Map<string, number>>();
const lastConfirmedProductSetOperationIds = new Map<string, Map<string, number>>();
const lastConfirmedProductSetRevisions = new Map<string, Map<string, number>>();

export function reserveCartMutationOperationId() {
  return nextOptimisticOperationId++;
}

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

function mergeCartMutationChangedItem(
  currentCart: StoreOrderCart | null,
  result: StoreOrderCartMutationResult
) {
  if (!currentCart || result.removed || !result.changedItem) {
    return currentCart;
  }

  const alreadyHasChangedItem = currentCart.items.some((item) => {
    const isSameDetail = result.changedItem?.detailGUID && item.detailGUID === result.changedItem.detailGUID;
    return isSameDetail || item.productCode === result.changedItem?.productCode;
  });

  // ponytail: 旧确认只补缺失行，不替换已有行；有服务端 revision 后替换这层客户端顺序保护。
  return alreadyHasChangedItem
    ? currentCart
    : withQuantityTotals(currentCart, [result.changedItem, ...currentCart.items]);
}

function applyCartMutationChangedItemLocally(
  currentCart: StoreOrderCart | null,
  result: StoreOrderCartMutationResult
) {
  if (!currentCart) {
    return currentCart;
  }

  const productCode = result.productCode || result.changedItem?.productCode || "";
  if (result.removed) {
    return withQuantityTotals(
      currentCart,
      currentCart.items.filter((item) => item.productCode !== productCode)
    );
  }

  if (!result.changedItem) {
    return currentCart;
  }

  let found = false;
  const items = currentCart.items.map((item) => {
    const isSameDetail = result.changedItem?.detailGUID && item.detailGUID === result.changedItem.detailGUID;
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

  return withQuantityTotals(currentCart, items);
}

function getOrCreateOptimisticState(storeCode: string, currentCart: StoreOrderCart | null | undefined) {
  const storeKey = getStoreKey(storeCode);
  const existingState = optimisticStates.get(storeKey);
  if (existingState) {
    return { state: existingState, storeKey };
  }

  const state: CartOptimisticState = {
    baseCart: currentCart ?? null,
    lastConfirmedCartRevision: lastConfirmedCartRevisions.get(storeKey) ?? 0,
    lastConfirmedOperationId: lastConfirmedOperationIds.get(storeKey) ?? 0,
    lastConfirmedProductOperationIds: new Map(lastConfirmedProductOperationIds.get(storeKey) ?? []),
    lastConfirmedProductRevisions: new Map(lastConfirmedProductRevisions.get(storeKey) ?? []),
    lastConfirmedProductSetOperationIds: new Map(lastConfirmedProductSetOperationIds.get(storeKey) ?? []),
    lastConfirmedProductSetRevisions: new Map(lastConfirmedProductSetRevisions.get(storeKey) ?? []),
    operations: [],
  };
  optimisticStates.set(storeKey, state);
  return { state, storeKey };
}

function recordConfirmedProductOperation(
  state: CartOptimisticState,
  storeKey: string,
  productCode: string,
  operationId: number
) {
  if (!productCode) {
    return;
  }

  const nextOperationId = Math.max(
    state.lastConfirmedProductOperationIds.get(productCode) ?? 0,
    operationId
  );
  state.lastConfirmedProductOperationIds.set(productCode, nextOperationId);
  lastConfirmedProductOperationIds.set(storeKey, new Map(state.lastConfirmedProductOperationIds));
}

function recordConfirmedProductSetOperation(
  state: CartOptimisticState,
  storeKey: string,
  productCode: string,
  operationId: number
) {
  if (!productCode) {
    return;
  }

  const nextOperationId = Math.max(
    state.lastConfirmedProductSetOperationIds.get(productCode) ?? 0,
    operationId
  );
  state.lastConfirmedProductSetOperationIds.set(productCode, nextOperationId);
  lastConfirmedProductSetOperationIds.set(storeKey, new Map(state.lastConfirmedProductSetOperationIds));
}

function recordConfirmedProductRevision(
  state: CartOptimisticState,
  storeKey: string,
  productCode: string,
  revision: number
) {
  if (!productCode) {
    return;
  }

  const nextRevision = Math.max(state.lastConfirmedProductRevisions.get(productCode) ?? 0, revision);
  state.lastConfirmedProductRevisions.set(productCode, nextRevision);
  lastConfirmedProductRevisions.set(storeKey, new Map(state.lastConfirmedProductRevisions));
}

function recordConfirmedProductSetRevision(
  state: CartOptimisticState,
  storeKey: string,
  productCode: string,
  revision: number
) {
  if (!productCode) {
    return;
  }

  const nextRevision = Math.max(state.lastConfirmedProductSetRevisions.get(productCode) ?? 0, revision);
  state.lastConfirmedProductSetRevisions.set(productCode, nextRevision);
  lastConfirmedProductSetRevisions.set(storeKey, new Map(state.lastConfirmedProductSetRevisions));
}

function getCartMutationRevision(
  confirmedCart: StoreOrderCart | StoreOrderCartMutationResult | null
) {
  if (isCartMutationResult(confirmedCart)) {
    const revision = Number(confirmedCart.summary.cartRevision);
    if (Number.isFinite(revision) && revision > 0) {
      return revision;
    }
  }

  return undefined;
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
  operation: Omit<CartOptimisticOperation, "id">,
  operationId = reserveCartMutationOperationId()
): CartMutationCacheSnapshot {
  const currentCart = queryClient.getQueryData<StoreOrderCart | null>(["cartSummary", storeCode]);
  const { state, storeKey } = getOrCreateOptimisticState(storeCode, currentCart);
  const previousDynamicCartQuantity = getCurrentDynamicCartQuantity(
    queryClient,
    storeCode,
    operation.product.productCode
  );
  const snapshot: CartMutationCacheSnapshot = {
    operationId,
    operationType: operation.type,
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
  const { state, storeKey } = getOrCreateOptimisticState(
    storeCode,
    queryClient.getQueryData(["cartSummary", storeCode])
  );
  state.operations = state.operations.filter((operation) => operation.id !== snapshot.operationId);
  if (confirmedCart !== undefined) {
    const cartRevision = getCartMutationRevision(confirmedCart);
    const usesServerRevision = cartRevision !== undefined;
    const confirmationOrder = cartRevision ?? snapshot.operationId;
    const ignoreRevisionlessMutation =
      !usesServerRevision && isCartMutationResult(confirmedCart) && state.lastConfirmedCartRevision > 0;
    const confirmedProductCode = isCartMutationResult(confirmedCart)
      ? confirmedCart.productCode || confirmedCart.changedItem?.productCode || snapshot.productCode
      : snapshot.productCode;
    if (snapshot.operationType === "set") {
      if (usesServerRevision) {
        recordConfirmedProductSetRevision(state, storeKey, snapshot.productCode, confirmationOrder);
      } else {
        recordConfirmedProductSetOperation(state, storeKey, snapshot.productCode, confirmationOrder);
      }
      state.operations = state.operations.filter(
        (operation) => operation.id > snapshot.operationId || operation.product.productCode !== snapshot.productCode
      );
    }

    const confirmedBaseCart = isCartMutationResult(confirmedCart)
      ? applyCartMutationResultToCart(state.baseCart, confirmedCart)
      : confirmedCart;
    const supersededBySet =
      confirmationOrder < (
        usesServerRevision
          ? state.lastConfirmedProductSetRevisions.get(snapshot.productCode) ?? 0
          : state.lastConfirmedProductSetOperationIds.get(snapshot.productCode) ?? 0
      );
    const shouldAcceptConfirmedCart = usesServerRevision
      ? confirmationOrder >= state.lastConfirmedCartRevision
      : !ignoreRevisionlessMutation && confirmationOrder >= state.lastConfirmedOperationId;

    // 服务端 revision 表示真实提交顺序；旧服务端响应再回退本地 operationId。
    if (shouldAcceptConfirmedCart) {
      state.baseCart = confirmedBaseCart;
      if (usesServerRevision) {
        state.lastConfirmedCartRevision = Math.max(state.lastConfirmedCartRevision, confirmationOrder);
        lastConfirmedCartRevisions.set(storeKey, state.lastConfirmedCartRevision);
        recordConfirmedProductRevision(state, storeKey, confirmedProductCode, confirmationOrder);
      } else {
        state.lastConfirmedOperationId = Math.max(state.lastConfirmedOperationId, confirmationOrder);
        lastConfirmedOperationIds.set(storeKey, state.lastConfirmedOperationId);
        recordConfirmedProductOperation(state, storeKey, confirmedProductCode, confirmationOrder);
      }
    } else if (
      isCartMutationResult(confirmedCart)
      && !ignoreRevisionlessMutation
      && snapshot.operationType === "set"
      && (
        usesServerRevision
          ? state.lastConfirmedProductRevisions.get(snapshot.productCode) ?? 0
          : state.lastConfirmedProductOperationIds.get(snapshot.productCode) ?? 0
      ) <= confirmationOrder
    ) {
      state.baseCart = applyCartMutationChangedItemLocally(state.baseCart, confirmedCart);
      if (usesServerRevision) {
        recordConfirmedProductRevision(state, storeKey, confirmedProductCode, confirmationOrder);
      } else {
        recordConfirmedProductOperation(state, storeKey, confirmedProductCode, confirmationOrder);
      }
    } else if (
      isCartMutationResult(confirmedCart)
      && !ignoreRevisionlessMutation
      && snapshot.operationType === "add"
      && !supersededBySet
    ) {
      state.baseCart = mergeCartMutationChangedItem(state.baseCart, confirmedCart);
      if (usesServerRevision) {
        recordConfirmedProductRevision(state, storeKey, confirmedProductCode, confirmationOrder);
      } else {
        recordConfirmedProductOperation(state, storeKey, confirmedProductCode, confirmationOrder);
      }
    }
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

export function resolveCartMutationBatchCache(
  queryClient: QueryClient,
  storeCode: string,
  snapshots: CartMutationCacheSnapshot[],
  confirmedCart?: StoreOrderCart | StoreOrderCartMutationResult | null
) {
  if (snapshots.length === 0) {
    return queryClient.getQueryData<StoreOrderCart | null>(["cartSummary", storeCode]) ?? null;
  }

  if (confirmedCart === undefined) {
    return snapshots.reduce<StoreOrderCart | null>(
      (_cart, snapshot) => resolveCartMutationCache(queryClient, storeCode, snapshot),
      null
    );
  }

  const sortedSnapshots = [...snapshots].sort((left, right) => left.operationId - right.operationId);
  const finalSnapshot = sortedSnapshots[sortedSnapshots.length - 1]!;
  const foldedOperationIds = new Set(sortedSnapshots.slice(0, -1).map((snapshot) => snapshot.operationId));
  const { state } = getOrCreateOptimisticState(
    storeCode,
    queryClient.getQueryData(["cartSummary", storeCode])
  );
  // ponytail: 后端批量提交返回一份权威结果，先折叠同批本地操作，避免确认时把剩余本地操作再叠一次。
  state.operations = state.operations.filter((operation) => !foldedOperationIds.has(operation.id));

  return resolveCartMutationCache(queryClient, storeCode, finalSnapshot, confirmedCart);
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

export function reconcileSubmittedCartCache(queryClient: QueryClient, storeCode: string) {
  // 取消动作会同步中止在途请求；随后立即清零本地缓存，后台校准不阻塞提交成功后的导航。
  void Promise.all([
    queryClient.cancelQueries({ queryKey: ["cartSummary", storeCode] }),
    queryClient.cancelQueries({ queryKey: ["shopDynamicData", storeCode] }),
  ]);
  syncCartMutationCache(queryClient, storeCode, null);
  void Promise.all([
    queryClient.invalidateQueries({ queryKey: ["cartSummary", storeCode] }),
    queryClient.invalidateQueries({ queryKey: ["shopDynamicData", storeCode] }),
  ]);
}
