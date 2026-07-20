import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { QueryClient, QueryObserver } from "@tanstack/react-query";
import { reconcileSubmittedCartCache } from "./cart-cache";
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

function createDeferred<T>() {
  let resolvePromise!: (value: T) => void;
  const promise = new Promise<T>((resolve) => {
    resolvePromise = resolve;
  });

  return { promise, resolve: resolvePromise };
}

function createCart(storeCode: string, productCode: string, quantity: number): StoreOrderCart {
  return {
    orderGUID: `cart-${storeCode}`,
    storeCode,
    totalAmount: 10 * quantity,
    totalQuantity: quantity,
    totalImportAmount: 8 * quantity,
    totalSku: 1,
    totalVolume: 0,
    items: [
      {
        detailGUID: `detail-${productCode}`,
        productCode,
        quantity,
        price: 10,
        amount: 10 * quantity,
        importPrice: 8,
        importAmount: 8 * quantity,
        minOrderQuantity: 1,
        isActive: true,
      },
    ],
  };
}

async function run() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  });
  const originalCancelQueries = queryClient.cancelQueries.bind(queryClient);
  const cancelledQueryKeys: unknown[] = [];
  queryClient.cancelQueries = ((...args: Parameters<QueryClient["cancelQueries"]>) => {
    cancelledQueryKeys.push(args[0]?.queryKey);
    return originalCancelQueries(...args);
  }) as QueryClient["cancelQueries"];
  const submittedStoreCode = "S001";
  const otherStoreCode = "S002";
  const submittedCart = createCart(submittedStoreCode, "P001", 3);
  const otherCart = createCart(otherStoreCode, "P900", 4);
  const firstDynamicKey = ["shopDynamicData", submittedStoreCode, ["P001", "P002"]] as const;
  const secondDynamicKey = ["shopDynamicData", submittedStoreCode, ["P003"]] as const;
  const otherDynamicKey = ["shopDynamicData", otherStoreCode, ["P900"]] as const;
  const oldSummaryResponse = createDeferred<StoreOrderCart | null>();
  const backgroundSummaryResponse = createDeferred<StoreOrderCart | null>();
  const oldResponse = createDeferred<StoreOrderDynamicData[]>();
  const backgroundResponse = createDeferred<StoreOrderDynamicData[]>();
  let summaryRequestCount = 0;
  let dynamicRequestCount = 0;

  queryClient.setQueryData(["cartSummary", submittedStoreCode], submittedCart);
  queryClient.setQueryData(["cartSummary", otherStoreCode], otherCart);
  queryClient.setQueryData<StoreOrderDynamicData[]>(firstDynamicKey, [
    { productCode: "P001", cartQuantity: 3 },
    { productCode: "P002", cartQuantity: 2 },
  ]);
  queryClient.setQueryData<StoreOrderDynamicData[]>(secondDynamicKey, [
    { productCode: "P003", cartQuantity: 5 },
  ]);
  queryClient.setQueryData<StoreOrderDynamicData[]>(otherDynamicKey, [
    { productCode: "P900", cartQuantity: 4 },
  ]);

  const observer = new QueryObserver<StoreOrderDynamicData[]>(queryClient, {
    queryKey: firstDynamicKey,
    queryFn: () => {
      dynamicRequestCount += 1;
      return dynamicRequestCount === 1 ? oldResponse.promise : backgroundResponse.promise;
    },
    staleTime: 0,
  });
  const summaryObserver = new QueryObserver<StoreOrderCart | null>(queryClient, {
    queryKey: ["cartSummary", submittedStoreCode],
    queryFn: () => {
      summaryRequestCount += 1;
      return summaryRequestCount === 1
        ? oldSummaryResponse.promise
        : backgroundSummaryResponse.promise;
    },
    staleTime: 0,
  });
  const unsubscribeDynamic = observer.subscribe(() => undefined);
  const unsubscribeSummary = summaryObserver.subscribe(() => undefined);
  assertEqual(dynamicRequestCount, 1, "测试前存在动态数据在途旧请求");
  assertEqual(summaryRequestCount, 1, "测试前存在购物车摘要在途旧请求");

  const coordinationResult = reconcileSubmittedCartCache(queryClient, submittedStoreCode);

  assertEqual(coordinationResult, undefined, "缓存协调立即返回且不等待后台校准");
  assertDeepEqual(
    cancelledQueryKeys,
    [
      ["cartSummary", submittedStoreCode],
      ["shopDynamicData", submittedStoreCode],
    ],
    "同步清零前显式取消当前门店摘要与动态数据旧请求"
  );
  assertEqual(dynamicRequestCount, 2, "缓存清空后已启动后台动态数据校准");
  assertEqual(summaryRequestCount, 2, "缓存清空后已启动后台购物车摘要校准");
  assertDeepEqual(
    queryClient.getQueryData(["cartSummary", submittedStoreCode]),
    null,
    "已提交门店购物车摘要立即清空"
  );
  assertDeepEqual(
    queryClient.getQueryData<StoreOrderDynamicData[]>(firstDynamicKey)?.map((item) => item.cartQuantity),
    [0, 0],
    "已提交门店活动动态子键数量立即归零"
  );
  assertDeepEqual(
    queryClient.getQueryData<StoreOrderDynamicData[]>(secondDynamicKey)?.map((item) => item.cartQuantity),
    [0],
    "已提交门店所有动态子键数量立即归零"
  );
  assertDeepEqual(
    queryClient.getQueryData(["cartSummary", otherStoreCode]),
    otherCart,
    "其他门店购物车摘要不受影响"
  );
  assertDeepEqual(
    queryClient.getQueryData<StoreOrderDynamicData[]>(otherDynamicKey)?.map((item) => item.cartQuantity),
    [4],
    "其他门店动态商品数量不受影响"
  );

  oldResponse.resolve([
    { productCode: "P001", cartQuantity: 30 },
    { productCode: "P002", cartQuantity: 20 },
  ]);
  oldSummaryResponse.resolve(createCart(submittedStoreCode, "P001", 30));
  await new Promise<void>((resolveDelay) => setTimeout(resolveDelay, 0));
  assertDeepEqual(
    queryClient.getQueryData(["cartSummary", submittedStoreCode]),
    null,
    "已取消的旧摘要响应不能覆盖提交后的空购物车"
  );
  assertDeepEqual(
    queryClient.getQueryData<StoreOrderDynamicData[]>(firstDynamicKey)?.map((item) => item.cartQuantity),
    [0, 0],
    "已取消的旧响应不能覆盖提交后的清零缓存"
  );

  const cartSource = readFileSync(resolve(process.cwd(), "app/(tabs)/cart.tsx"), "utf8");
  if (!cartSource.includes("reconcileSubmittedCartCache(queryClient, selectedStoreCode)")) {
    throw new Error("订单提交成功处理必须调用统一缓存协调 helper");
  }

  backgroundResponse.resolve([
    { productCode: "P001", cartQuantity: 0 },
    { productCode: "P002", cartQuantity: 0 },
  ]);
  backgroundSummaryResponse.resolve(null);
  await new Promise<void>((resolveDelay) => setTimeout(resolveDelay, 0));
  unsubscribeDynamic();
  unsubscribeSummary();
  queryClient.clear();
}

void run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
