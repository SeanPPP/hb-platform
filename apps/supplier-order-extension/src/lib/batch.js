// 微批队列：150ms 合并、每批最多 maxSize、按键去重、完成后缓存。
// 用于列表页多个商品摘要的批量请求，滚动追加不会重复请求/重复按钮。
export function createBatchQueue({
  flush,
  maxSize = 100,
  delayMs = 150,
  cacheTtlMs = 60000,
  schedule = (fn) => setTimeout(fn, delayMs),
  cancel = clearTimeout,
} = {}) {
  const pending = new Map(); // key -> {key,item,resolve,reject,promise}
  const cache = new Map(); // key -> {value,expiresAt}
  let timer = null;
  let flushing = false;

  const now = () => Date.now();

  function readCache(key) {
    const c = cache.get(key);
    if (!c) return undefined;
    if (c.expiresAt <= now()) {
      cache.delete(key);
      return undefined;
    }
    return c.value;
  }

  function scheduleFlush() {
    if (timer !== null) return;
    timer = schedule(drain);
  }

  async function drain() {
    if (flushing) return;
    flushing = true;
    try {
      while (pending.size > 0) {
        const batch = [];
        for (const [key, entry] of pending) {
          if (batch.length >= maxSize) break;
          batch.push(entry);
          pending.delete(key);
        }
        let results;
        try {
          results = await flush(batch.map((e) => ({ key: e.key, item: e.item })));
        } catch (err) {
          for (const entry of batch) entry.reject(err);
          for (const entry of pending.values()) entry.reject(err);
          pending.clear();
          return;
        }
        for (const entry of batch) {
          const val =
            results instanceof Map ? results.get(entry.key) : results && results[entry.key];
          cache.set(entry.key, { value: val, expiresAt: now() + cacheTtlMs });
          entry.resolve(val);
        }
      }
    } finally {
      flushing = false;
      timer = null;
    }
  }

  function enqueue(key, item) {
    const cached = readCache(key);
    if (cached !== undefined) return Promise.resolve(cached);
    const existing = pending.get(key);
    if (existing) return existing.promise;
    let resolve;
    let reject;
    const promise = new Promise((res, rej) => {
      resolve = res;
      reject = rej;
    });
    pending.set(key, { key, item, resolve, reject, promise });
    scheduleFlush();
    return promise;
  }

  return {
    enqueue,
    flushNow: () => {
      if (timer !== null) {
        cancel(timer);
        timer = null;
      }
      return drain();
    },
    pendingSize: () => pending.size,
    cacheSize: () => cache.size,
    clearCache: () => cache.clear(),
  };
}
