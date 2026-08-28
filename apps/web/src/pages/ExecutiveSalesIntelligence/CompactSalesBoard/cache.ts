export interface CompactBoardCacheEntry<T> {
  expiresAt: number
  data: T
}

export const compactBoardClientCacheLimit = 48

export function readCompactBoardCache<T>(
  cache: Map<string, CompactBoardCacheEntry<T>>,
  key: string,
  now = Date.now(),
): T | undefined {
  const entry = cache.get(key)
  if (!entry) return undefined

  if (entry.expiresAt <= now) {
    cache.delete(key)
    return undefined
  }

  // 中文注释：Map 的插入顺序即 LRU 顺序，命中时移到末尾避免误淘汰热数据。
  cache.delete(key)
  cache.set(key, entry)
  return entry.data
}

export function writeCompactBoardCache<T>(
  cache: Map<string, CompactBoardCacheEntry<T>>,
  key: string,
  entry: CompactBoardCacheEntry<T>,
  now = Date.now(),
  limit = compactBoardClientCacheLimit,
) {
  for (const [staleKey, staleEntry] of cache) {
    if (staleEntry.expiresAt <= now) cache.delete(staleKey)
  }

  cache.delete(key)
  cache.set(key, entry)
  while (cache.size > limit) {
    const oldestKey = cache.keys().next().value as string | undefined
    if (!oldestKey) break
    cache.delete(oldestKey)
  }
}
