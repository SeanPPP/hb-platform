export interface ScheduledTaskInstanceState {
  instanceId: string
  hostName?: string
  processId: number
  schedulerEnabledByConfig: boolean
  lastSeenAtUtc: string
  isCurrent: boolean
  isActive: boolean
}

export interface ScheduledTaskRuntimeControlStatus {
  schedulerEnabled: boolean
  schedulerEnabledByConfig: boolean
  effectiveSchedulerEnabled: boolean
  currentInstanceId: string
  activeInstanceId?: string | null
  updatedAtUtc?: string
  updatedBy?: string
  runningLeaseCount: number
  recentDuplicateSkipCount: number
  knownInstances: ScheduledTaskInstanceState[]
}

export interface ScheduledTaskRuntimeControlUpdate {
  schedulerEnabled: boolean
  activeInstanceId?: string | null
}
