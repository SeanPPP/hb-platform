import assert from "node:assert/strict";
import { QueryClient } from "@tanstack/react-query";
import {
  clearEmployeeProfileReviewDetailCache,
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
}

void main();
