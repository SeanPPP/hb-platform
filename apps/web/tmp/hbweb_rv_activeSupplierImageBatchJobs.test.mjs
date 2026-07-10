// src/pages/PosAdmin/ProductManagement/activeSupplierImageBatchJobs.ts
var SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY = "posAdmin.products.activeSupplierImageBatchJob";
function normalizeSupplierImageBatchJobKey(localSupplierCode) {
  return (localSupplierCode ?? "").trim().toUpperCase();
}
function isPlainRecord(value) {
  return typeof value === "object" && value !== null;
}
function isActiveSupplierImageBatchJob(value) {
  if (!isPlainRecord(value)) return false;
  return typeof value.jobId === "string" && typeof value.operationId === "string" && typeof value.localSupplierCode === "string" && typeof value.createdAt === "string";
}
function readActiveSupplierImageBatchJobs(storage2 = globalThis.window?.localStorage) {
  if (!storage2) return {};
  try {
    const raw = storage2.getItem(SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw);
    if (isActiveSupplierImageBatchJob(parsed)) {
      const jobKey = normalizeSupplierImageBatchJobKey(parsed.localSupplierCode);
      return jobKey ? { [jobKey]: parsed } : {};
    }
    if (!isPlainRecord(parsed)) return {};
    return Object.values(parsed).reduce((jobs, value) => {
      if (!isActiveSupplierImageBatchJob(value)) return jobs;
      const jobKey = normalizeSupplierImageBatchJobKey(value.localSupplierCode);
      if (jobKey) jobs[jobKey] = value;
      return jobs;
    }, {});
  } catch {
    return {};
  }
}
function saveActiveSupplierImageBatchJobs(jobs, storage2 = globalThis.window?.localStorage) {
  if (!storage2) return;
  const normalizedJobs = Object.values(jobs).reduce((result, job) => {
    const jobKey = normalizeSupplierImageBatchJobKey(job.localSupplierCode);
    if (jobKey) result[jobKey] = job;
    return result;
  }, {});
  if (!Object.keys(normalizedJobs).length) {
    storage2.removeItem(SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY);
    return;
  }
  storage2.setItem(SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY, JSON.stringify(normalizedJobs));
}
function clearActiveSupplierImageBatchJob(jobs, localSupplierCode) {
  const jobKey = normalizeSupplierImageBatchJobKey(localSupplierCode);
  if (!jobKey || !jobs[jobKey]) return jobs;
  const next = { ...jobs };
  delete next[jobKey];
  return next;
}

// src/pages/PosAdmin/ProductManagement/activeSupplierImageBatchJobs.test.ts
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function createMemoryStorage() {
  const values = /* @__PURE__ */ new Map();
  return {
    get length() {
      return values.size;
    },
    clear() {
      values.clear();
    },
    getItem(key) {
      return values.get(key) ?? null;
    },
    key(index) {
      return Array.from(values.keys())[index] ?? null;
    },
    removeItem(key) {
      values.delete(key);
    },
    setItem(key, value) {
      values.set(key, value);
    }
  };
}
var storage = createMemoryStorage();
storage.setItem(SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY, JSON.stringify({
  jobId: "job-dats",
  operationId: "op-dats",
  localSupplierCode: "dats",
  createdAt: "2026-06-04T00:00:00Z"
}));
var migratedJobs = readActiveSupplierImageBatchJobs(storage);
assertEqual(Object.keys(migratedJobs).length, 1, "\u65E7\u5355\u4EFB\u52A1\u7ED3\u6784\u5E94\u8FC1\u79FB\u4E3A map");
assert(migratedJobs.DATS, "\u65E7\u5355\u4EFB\u52A1\u7ED3\u6784\u5E94\u6309\u4F9B\u5E94\u5546\u4EE3\u7801\u5F52\u6863");
saveActiveSupplierImageBatchJobs({
  dats: {
    jobId: "job-dats",
    operationId: "op-dats",
    localSupplierCode: "dats",
    createdAt: "2026-06-04T00:00:00Z"
  },
  MALMAR: {
    jobId: "job-malmar",
    operationId: "op-malmar",
    localSupplierCode: "MALMAR",
    createdAt: "2026-06-04T00:01:00Z"
  }
}, storage);
var savedJobs = readActiveSupplierImageBatchJobs(storage);
assert(savedJobs.DATS, "\u5E94\u4FDD\u5B58 DATS active job");
assert(savedJobs.MALMAR, "\u5E94\u4FDD\u5B58 MALMAR active job");
assertEqual(Object.keys(savedJobs).length, 2, "\u5E94\u5141\u8BB8\u591A\u4E2A\u4F9B\u5E94\u5546 active job \u540C\u65F6\u5B58\u5728");
var afterClearDats = clearActiveSupplierImageBatchJob(savedJobs, "dats");
saveActiveSupplierImageBatchJobs(afterClearDats, storage);
var remainingJobs = readActiveSupplierImageBatchJobs(storage);
assert(!remainingJobs.DATS, "\u6E05\u7406 DATS \u4E0D\u5E94\u7559\u4E0B\u540C\u4F9B\u5E94\u5546\u4EFB\u52A1");
assert(remainingJobs.MALMAR, "\u6E05\u7406 DATS \u4E0D\u5E94\u5F71\u54CD MALMAR");
saveActiveSupplierImageBatchJobs({}, storage);
assertEqual(storage.getItem(SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY), null, "\u6E05\u7A7A map \u65F6\u5E94\u79FB\u9664 localStorage key");
