import assert from "node:assert/strict";
import { QueryClient } from "@tanstack/react-query";
import { employeeProfileReviewDetailQueryKey } from "./review-cache";
import { createEmployeeProfileReviewAppStateHandler } from "./privacy-state";
import {
  createEmployeeProfileSensitiveDetailActivityGuard,
  isSensitiveDetailFetchBlockedError,
} from "./sensitive-detail-activity-guard";

async function testInactiveLateCallbacks() {
  const queryClient = new QueryClient();
  const requestId = 42;
  const queryKey = employeeProfileReviewDetailQueryKey(requestId);
  let appIsActive = true;
  let activityGeneration = 0;
  let fetchCount = 0;
  let inactiveCleanup = Promise.resolve();
  const clearCache = async () => {
    await queryClient.cancelQueries({ queryKey, exact: true });
    queryClient.removeQueries({ queryKey, exact: true });
  };
  const guard = createEmployeeProfileSensitiveDetailActivityGuard({
    isActive: () => appIsActive,
    getActivityGeneration: () => activityGeneration,
    clearCache,
  });
  const fetchDetail = async () => {
    fetchCount += 1;
    return { requestId };
  };

  queryClient.setQueryData(queryKey, {
    requestId,
    bankAccountNumber: "123456789",
  });
  const handleAppState = createEmployeeProfileReviewAppStateHandler({
    onInactive: () => {
      // AppState 回调必须先同步关闭闸门，再开始异步清缓存。
      appIsActive = false;
      activityGeneration += 1;
      inactiveCleanup = clearCache();
    },
    onActive: () => {
      appIsActive = true;
    },
  });
  handleAppState("inactive");
  await inactiveCleanup;

  // 模拟迟到响应曾尝试重建缓存，mutation 的 409 callback 必须再次清掉并忽略。
  queryClient.setQueryData(queryKey, {
    requestId,
    identityId: "DL-123456",
  });
  assert.equal(guard.shouldIgnoreLateCallback(), true);
  // 照片到期 timer、Image onError 随后到达时也不能触发 fetch。
  await guard.runIfActive(fetchDetail);
  await guard.runIfActive(fetchDetail);
  await assert.rejects(
    () => guard.fetch(fetchDetail),
    (error: unknown) => isSensitiveDetailFetchBlockedError(error)
  );

  assert.equal(fetchCount, 0);
  assert.equal(queryClient.getQueryCache().findAll({ queryKey }).length, 0);
}

function createInterleavingHarness(requestId: number) {
  const queryClient = new QueryClient();
  const queryKey = employeeProfileReviewDetailQueryKey(requestId);
  let appIsActive = true;
  let activityGeneration = 0;
  const clearCache = async () => {
    await queryClient.cancelQueries({ queryKey, exact: true });
    queryClient.removeQueries({ queryKey, exact: true });
  };
  const guard = createEmployeeProfileSensitiveDetailActivityGuard({
    isActive: () => appIsActive,
    getActivityGeneration: () => activityGeneration,
    clearCache,
  });
  return {
    guard,
    queryClient,
    queryKey,
    async enterInactive() {
      appIsActive = false;
      activityGeneration += 1;
      await clearCache();
    },
    enterActiveNewGeneration() {
      appIsActive = true;
      activityGeneration += 1;
    },
    writeNewGeneration() {
      queryClient.setQueryData(queryKey, { requestId, marker: "NEW-GENERATION" });
    },
  };
}

async function testOldFetchDoesNotClearNewGeneration() {
  const harness = createInterleavingHarness(84);
  let resolveRequest: ((value: { requestId: number }) => void) | undefined;
  const pending = harness.guard.fetch(() => new Promise<{ requestId: number }>((resolve) => {
    resolveRequest = resolve;
  }));
  const rejected = assert.rejects(
    pending,
    (error: unknown) => isSensitiveDetailFetchBlockedError(error)
  );

  await harness.enterInactive();
  harness.enterActiveNewGeneration();
  harness.writeNewGeneration();
  resolveRequest?.({ requestId: 84 });
  await rejected;
  await new Promise<void>((resolve) => setTimeout(resolve, 0));
  assert.deepEqual(harness.queryClient.getQueryData(harness.queryKey), {
    requestId: 84,
    marker: "NEW-GENERATION",
  });
}

async function testOldRunIfActiveDoesNotClearNewGeneration() {
  const harness = createInterleavingHarness(85);
  let resolveAction: (() => void) | undefined;
  const pending = harness.guard.runIfActive(() => new Promise<void>((resolve) => {
    resolveAction = resolve;
  }));

  await harness.enterInactive();
  harness.enterActiveNewGeneration();
  harness.writeNewGeneration();
  resolveAction?.();
  assert.equal(await pending, false);
  assert.deepEqual(harness.queryClient.getQueryData(harness.queryKey), {
    requestId: 85,
    marker: "NEW-GENERATION",
  });
}

async function testOldMutationCallbackDoesNotClearNewGeneration() {
  const harness = createInterleavingHarness(86);
  const oldMutationGeneration = 0;

  await harness.enterInactive();
  harness.enterActiveNewGeneration();
  harness.writeNewGeneration();
  assert.equal(
    harness.guard.shouldIgnoreLateCallback(oldMutationGeneration),
    true
  );
  // 等待一轮，确保任何误触的异步清理都会暴露为新世代缓存丢失。
  await new Promise<void>((resolve) => setTimeout(resolve, 0));
  assert.deepEqual(harness.queryClient.getQueryData(harness.queryKey), {
    requestId: 86,
    marker: "NEW-GENERATION",
  });
}

async function testRequestSpanningBackgroundIsDiscarded() {
  const queryClient = new QueryClient();
  const queryKey = employeeProfileReviewDetailQueryKey(87);
  let appIsActive = true;
  let activityGeneration = 0;
  let resolveRequest: ((value: { requestId: number }) => void) | undefined;
  const clearCache = async () => {
    await queryClient.cancelQueries({ queryKey, exact: true });
    queryClient.removeQueries({ queryKey, exact: true });
  };
  const guard = createEmployeeProfileSensitiveDetailActivityGuard({
    isActive: () => appIsActive,
    getActivityGeneration: () => activityGeneration,
    clearCache,
  });
  const pending = guard.fetch(() => new Promise<{ requestId: number }>((resolve) => {
    resolveRequest = resolve;
  }));
  const rejected = assert.rejects(
    pending,
    (error: unknown) => isSensitiveDetailFetchBlockedError(error)
  );

  // 请求跨过 inactive → active，也不能把旧世代响应当成本次重新鉴权结果。
  appIsActive = false;
  activityGeneration += 1;
  await clearCache();
  appIsActive = true;
  activityGeneration += 1;
  resolveRequest?.({ requestId: 87 });
  await rejected;
  assert.equal(queryClient.getQueryCache().findAll({ queryKey }).length, 0);
}

async function main() {
  await testInactiveLateCallbacks();
  await testOldFetchDoesNotClearNewGeneration();
  await testOldRunIfActiveDoesNotClearNewGeneration();
  await testOldMutationCallbackDoesNotClearNewGeneration();
  await testRequestSpanningBackgroundIsDiscarded();
}

void main();
