export interface KeyedSingleFlight<TKey, TValue> {
  run: (key: TKey, task: () => Promise<TValue>, generation?: number) => Promise<TValue>
}

export function createKeyedSingleFlight<TKey, TValue>(): KeyedSingleFlight<TKey, TValue> {
  const inFlight = new Map<TKey, { generation: number; promise: Promise<TValue> }>()
  return {
    run(key, task, generation = 0) {
      const current = inFlight.get(key)
      if (current && current.generation >= generation) return current.promise

      let taskPromise: Promise<TValue>
      try {
        taskPromise = task()
      } catch (error) {
        taskPromise = Promise.reject(error)
      }
      // 同一 generation 共用请求；mutation 后更高 generation 必须越过旧 GET，建立 fresh lane。
      const promise = taskPromise.finally(() => {
        if (inFlight.get(key)?.promise === promise) {
          inFlight.delete(key)
        }
      })
      inFlight.set(key, { generation, promise })
      return promise
    },
  }
}
