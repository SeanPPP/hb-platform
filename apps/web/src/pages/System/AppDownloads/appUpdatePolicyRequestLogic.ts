export type LatestRequestLaneResult =
  | { status: 'applied' }
  | { status: 'stale' }
  | { status: 'failed'; error: unknown }

interface LatestRequestToken {
  generation: number
  controller: AbortController
  signal: AbortSignal
}

export class LatestRequestLane {
  private generation = 0
  private controller: AbortController | null = null

  start(): LatestRequestToken {
    this.controller?.abort()
    this.generation += 1
    this.controller = new AbortController()
    return {
      generation: this.generation,
      controller: this.controller,
      signal: this.controller.signal,
    }
  }

  invalidate() {
    this.controller?.abort()
    this.generation += 1
    this.controller = null
  }

  isCurrent(token: LatestRequestToken) {
    return token.generation === this.generation
      && token.controller === this.controller
  }

  finish(token: LatestRequestToken) {
    if (!this.isCurrent(token)) {
      return false
    }

    this.controller = null
    return true
  }
}

export async function executeLatestRequestLane<T>(
  lane: LatestRequestLane,
  load: (signal: AbortSignal) => Promise<T>,
  commit: (value: T) => void,
): Promise<LatestRequestLaneResult> {
  const token = lane.start()
  try {
    const value = await load(token.signal)
    if (!lane.isCurrent(token)) {
      return { status: 'stale' }
    }

    commit(value)
    return { status: 'applied' }
  } catch (error) {
    if (token.signal.aborted || !lane.isCurrent(token)) {
      return { status: 'stale' }
    }
    return { status: 'failed', error }
  } finally {
    lane.finish(token)
  }
}

export type PolicyMutationResult =
  | 'saved'
  | 'conflict-reloaded'
  | 'conflict-reload-superseded'
  | 'conflict-reload-failed'

export async function savePolicyWithConflictReload(
  mutate: () => Promise<unknown>,
  reload: () => Promise<LatestRequestLaneResult['status']>,
  isConflict: (error: unknown) => boolean,
): Promise<PolicyMutationResult> {
  try {
    await mutate()
    return 'saved'
  } catch (error) {
    if (!isConflict(error)) {
      throw error
    }

    // 关键逻辑：冲突后只读取权威状态，绝不自动重放本次写入。
    const reloadStatus = await reload()
    if (reloadStatus === 'applied') {
      return 'conflict-reloaded'
    }
    return reloadStatus === 'stale'
      ? 'conflict-reload-superseded'
      : 'conflict-reload-failed'
  }
}
