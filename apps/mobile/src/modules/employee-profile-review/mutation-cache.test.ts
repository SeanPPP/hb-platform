import assert from "node:assert/strict";
import { QueryClient } from "@tanstack/react-query";
import { createEmployeeProfileReviewApi } from "./api";

async function main() {
  const fullSensitiveResponse = {
    requestId: 42,
    userGuid: "user-42",
    username: "employee42",
    status: "Approved",
    baseSensitiveRevision: 3,
    submittedAt: "2026-07-16T10:00:00Z",
    changedFields: ["bankAccountNumber", "identityId"],
    storeCodes: ["BNE"],
    storeNames: ["Brisbane"],
    bankAccountNumber: "123456789",
    superannuationAccountNumber: "SUPER-123456",
    identityId: "DL-123456",
    identityPhotoUrl: "https://signed.example/private-photo",
  };
  const api = createEmployeeProfileReviewApi({
    async get() {
      return { data: fullSensitiveResponse };
    },
    async post() {
      return { data: fullSensitiveResponse };
    },
  });
  const queryClient = new QueryClient();

  for (const mutationFn of [
    () => api.approve(42, "ok"),
    () => api.reject(42, "wrong"),
  ]) {
    const mutation = queryClient.getMutationCache().build(queryClient, {
      mutationFn,
      gcTime: 0,
    });
    await mutation.execute(undefined);
  }

  const serializedCaches = JSON.stringify({
    mutations: queryClient.getMutationCache().getAll().map((item) => item.state.data),
    queries: queryClient.getQueryCache().getAll().map((item) => item.state.data),
  });
  assert.doesNotMatch(serializedCaches, /123456789|SUPER-123456|DL-123456|private-photo/);
  assert.match(serializedCaches, /"requestId":42/);
  assert.match(serializedCaches, /"status":"Approved"/);
}

void main();
