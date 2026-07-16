import assert from "node:assert/strict";
import { QueryClient } from "@tanstack/react-query";
import {
  clearEmployeeProfileReviewDetailCache,
  clearEmployeeProfileReviewListCache,
  employeeProfileReviewDetailQueryKey,
} from "./review-cache";

async function main() {
  const queryClient = new QueryClient();
  const queryKey = employeeProfileReviewDetailQueryKey(42);
  queryClient.setQueryData(queryKey, {
    requestId: 42,
    bankAccountNumber: "123456789",
    identityId: "DL-123456",
  });
  assert.ok(queryClient.getQueryData(queryKey));

  await clearEmployeeProfileReviewDetailCache(queryClient, 42);
  assert.equal(queryClient.getQueryData(queryKey), undefined);

  queryClient.setQueryData(["employeeProfileReview", "requests", "Pending"], {
    items: [{ requestId: 42, username: "employee42" }],
  });
  queryClient.setQueryData(["employeeProfileReview", "requests", "Pending", "count"], {
    total: 1,
  });
  await clearEmployeeProfileReviewListCache(queryClient);
  assert.equal(
    queryClient.getQueryCache().findAll({ queryKey: ["employeeProfileReview", "requests"] }).length,
    0
  );
}

void main();
