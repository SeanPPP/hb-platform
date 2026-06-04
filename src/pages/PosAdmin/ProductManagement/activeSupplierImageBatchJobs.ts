export type ActiveSupplierImageBatchJob = {
  jobId: string
  operationId: string
  localSupplierCode: string
  createdAt: string
  status?: string
  message?: string
}

export type ActiveSupplierImageBatchJobMap = Record<string, ActiveSupplierImageBatchJob>

export const SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY = 'posAdmin.products.activeSupplierImageBatchJob'

export function normalizeSupplierImageBatchJobKey(localSupplierCode: string | undefined) {
  return (localSupplierCode ?? '').trim().toUpperCase()
}

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

export function isActiveSupplierImageBatchJob(value: unknown): value is ActiveSupplierImageBatchJob {
  if (!isPlainRecord(value)) return false
  return typeof value.jobId === 'string' &&
    typeof value.operationId === 'string' &&
    typeof value.localSupplierCode === 'string' &&
    typeof value.createdAt === 'string'
}

export function readActiveSupplierImageBatchJobs(storage: Storage | undefined = globalThis.window?.localStorage): ActiveSupplierImageBatchJobMap {
  if (!storage) return {}
  try {
    const raw = storage.getItem(SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY)
    if (!raw) return {}
    const parsed = JSON.parse(raw) as unknown
    if (isActiveSupplierImageBatchJob(parsed)) {
      const jobKey = normalizeSupplierImageBatchJobKey(parsed.localSupplierCode)
      return jobKey ? { [jobKey]: parsed } : {}
    }
    if (!isPlainRecord(parsed)) return {}
    return Object.values(parsed).reduce<ActiveSupplierImageBatchJobMap>((jobs, value) => {
      if (!isActiveSupplierImageBatchJob(value)) return jobs
      const jobKey = normalizeSupplierImageBatchJobKey(value.localSupplierCode)
      if (jobKey) jobs[jobKey] = value
      return jobs
    }, {})
  } catch {
    return {}
  }
}

export function saveActiveSupplierImageBatchJobs(
  jobs: ActiveSupplierImageBatchJobMap,
  storage: Storage | undefined = globalThis.window?.localStorage,
) {
  if (!storage) return
  const normalizedJobs = Object.values(jobs).reduce<ActiveSupplierImageBatchJobMap>((result, job) => {
    const jobKey = normalizeSupplierImageBatchJobKey(job.localSupplierCode)
    if (jobKey) result[jobKey] = job
    return result
  }, {})
  if (!Object.keys(normalizedJobs).length) {
    storage.removeItem(SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY)
    return
  }
  storage.setItem(SUPPLIER_IMAGE_BATCH_ACTIVE_JOB_STORAGE_KEY, JSON.stringify(normalizedJobs))
}

export function clearActiveSupplierImageBatchJob(
  jobs: ActiveSupplierImageBatchJobMap,
  localSupplierCode: string,
) {
  const jobKey = normalizeSupplierImageBatchJobKey(localSupplierCode)
  if (!jobKey || !jobs[jobKey]) return jobs
  const next = { ...jobs }
  delete next[jobKey]
  return next
}
