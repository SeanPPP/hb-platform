import {
  compactBoardClientCacheLimit,
  readCompactBoardCache,
  writeCompactBoardCache,
} from './cache'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

const cache = new Map<string, { expiresAt: number; data: string }>()
cache.set('expired', { expiresAt: 100, data: 'old' })
assertEqual(readCompactBoardCache(cache, 'expired', 100), undefined, '过期缓存不得返回')
assertEqual(cache.has('expired'), false, '读取过期缓存必须立即清理')

writeCompactBoardCache(cache, 'first', { expiresAt: 200, data: 'first' }, 100)
writeCompactBoardCache(cache, 'second', { expiresAt: 200, data: 'second' }, 100)
assertEqual(readCompactBoardCache(cache, 'first', 101), 'first', '未过期缓存应返回数据')
writeCompactBoardCache(cache, 'third', { expiresAt: 200, data: 'third' }, 101, 2)
assertEqual(cache.has('second'), false, 'LRU 写入满容量时应淘汰最久未访问项')
assertEqual(cache.has('first'), true, '读取应提升 LRU 项，不能被后续淘汰')

const staleCache = new Map<string, { expiresAt: number; data: string }>([
  ['stale', { expiresAt: 100, data: 'stale' }],
])
writeCompactBoardCache(staleCache, 'fresh', { expiresAt: 200, data: 'fresh' }, 100)
assertEqual(staleCache.has('stale'), false, '写入缓存前必须清理其他过期项')
assertEqual(compactBoardClientCacheLimit > 0 && compactBoardClientCacheLimit <= 64, true, '缓存容量必须是有限的小上限')

console.log('compactSalesBoard cache: ok')
