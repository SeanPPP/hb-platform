import type { QueryClient } from "@tanstack/react-query";

export function employeeProfileReviewDetailQueryKey(requestId: number) {
  return ["employeeProfileReview", "detail", requestId] as const;
}

export async function clearEmployeeProfileReviewDetailCache(
  queryClient: QueryClient,
  requestId: number
) {
  const queryKey = employeeProfileReviewDetailQueryKey(requestId);
  // 关键逻辑：先取消在途详情，再移除完整敏感值缓存，避免响应晚到后重新写回。
  await queryClient.cancelQueries({ queryKey, exact: true });
  queryClient.removeQueries({ queryKey, exact: true });
}

export async function clearEmployeeProfileReviewListCache(queryClient: QueryClient) {
  const queryKey = ["employeeProfileReview", "requests"] as const;
  await queryClient.cancelQueries({ queryKey });
  queryClient.removeQueries({ queryKey });
}
