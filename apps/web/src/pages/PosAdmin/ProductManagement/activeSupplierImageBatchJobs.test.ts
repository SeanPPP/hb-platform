import {
  SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY,
  clearActiveSupplierImageBatchJob,
  readActiveSupplierImageBatchJobs,
  saveActiveSupplierImageBatchJobs,
} from './activeSupplierImageBatchJobs'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function createMemoryStorage(): Storage {
  const values = new Map<string, string>()
  return {
    get length() {
      return values.size
    },
    clear() {
      values.clear()
    },
    getItem(key: string) {
      return values.get(key) ?? null
    },
    key(index: number) {
      return Array.from(values.keys())[index] ?? null
    },
    removeItem(key: string) {
      values.delete(key)
    },
    setItem(key: string, value: string) {
      values.set(key, value)
    },
  }
}

const storage = createMemoryStorage()

storage.setItem(SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY, JSON.stringify({
  jobId: 'job-dats',
  operationId: 'op-dats',
  localSupplierCode: 'dats',
  createdAt: '2026-06-04T00:00:00Z',
}))
const migratedJobs = readActiveSupplierImageBatchJobs(storage)
assertEqual(Object.keys(migratedJobs).length, 1, '旧单任务结构应迁移为 map')
assert(migratedJobs.DATS, '旧单任务结构应按供应商代码归档')

saveActiveSupplierImageBatchJobs({
  dats: {
    jobId: 'job-dats',
    operationId: 'op-dats',
    localSupplierCode: 'dats',
    createdAt: '2026-06-04T00:00:00Z',
  },
  MALMAR: {
    jobId: 'job-malmar',
    operationId: 'op-malmar',
    localSupplierCode: 'MALMAR',
    createdAt: '2026-06-04T00:01:00Z',
  },
}, storage)
const savedJobs = readActiveSupplierImageBatchJobs(storage)
assert(savedJobs.DATS, '应保存 DATS active job')
assert(savedJobs.MALMAR, '应保存 MALMAR active job')
assertEqual(Object.keys(savedJobs).length, 2, '应允许多个供应商 active job 同时存在')

const afterClearDats = clearActiveSupplierImageBatchJob(savedJobs, 'dats')
saveActiveSupplierImageBatchJobs(afterClearDats, storage)
const remainingJobs = readActiveSupplierImageBatchJobs(storage)
assert(!remainingJobs.DATS, '清理 DATS 不应留下同供应商任务')
assert(remainingJobs.MALMAR, '清理 DATS 不应影响 MALMAR')

saveActiveSupplierImageBatchJobs({}, storage)
assertEqual(storage.getItem(SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY), null, '清空 map 时应移除 localStorage key')
