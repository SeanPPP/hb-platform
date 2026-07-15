import assert from "node:assert/strict";
import { QueryClient } from "@tanstack/react-query";
import { clearSensitiveQueryCache } from "./sensitive-query-cache";

const queryClient = new QueryClient();
const profileKey = ["employee-profile", "me", "user-a"] as const;
const barcodeKey = ["employee-profile", "cashier-barcode", "me", "user-a"] as const;
queryClient.setQueryData(profileKey, { bankAccountNumber: "secret-account" });
queryClient.setQueryData(barcodeKey, { barcode: "2912345678906" });

clearSensitiveQueryCache(queryClient);

assert.equal(queryClient.getQueryData(profileKey), undefined, "会话清理必须移除员工银行资料缓存");
assert.equal(queryClient.getQueryData(barcodeKey), undefined, "会话清理必须移除员工条码缓存");
assert.equal(queryClient.getQueryCache().getAll().length, 0, "会话清理后不得残留其他账号查询");

console.log("sensitive-query-cache.test.ts: ok");
