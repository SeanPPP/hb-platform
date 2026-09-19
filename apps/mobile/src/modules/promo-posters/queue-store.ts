import { useEffect } from "react";
import { create } from "zustand";
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import {
  DEFAULT_PROMO_POSTER_QUEUE_SNAPSHOT,
  PROMO_POSTER_QUEUE_LIMIT,
  normalizeStoredQueueSnapshot,
  resolveQueueAddition,
  type PromoPosterQueueSnapshot,
  type QueueAdditionResult,
} from "./logic";
import type { PromoPosterQueueItem, PromoPosterSize, PromoPosterSpec, PromoPosterStyle } from "./types";

const STORAGE_KEY = "hbweb_promo_poster_queue";

interface PromoPosterQueueState extends PromoPosterQueueSnapshot {
  hydrated: boolean;
  /** 入队：超上限返回 full；队列是其它分店的海报时返回 storeConflict，由页面确认后调用 replaceAll。 */
  add: (input: { storeCode: string; productName: string; poster: PromoPosterSpec }) => QueueAdditionResult;
  /** 清空原队列后只保留这一张（切换分店时使用）。 */
  replaceAll: (input: { storeCode: string; productName: string; poster: PromoPosterSpec }) => void;
  remove: (id: string) => void;
  /** 生成 PDF 后只清掉本次已打印的条目，生成期间新加入的保留。 */
  removeMany: (ids: readonly string[]) => void;
  /** 撤销移除：放回原位置（同一张已在队列里时忽略）。 */
  restore: (item: PromoPosterQueueItem, index: number) => void;
  clear: () => void;
  setStyle: (style: PromoPosterStyle) => void;
  setSize: (size: PromoPosterSize) => void;
  setImpose: (impose: boolean) => void;
}

function createItemId() {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

function toQueueItem(input: { storeCode: string; productName: string; poster: PromoPosterSpec }): PromoPosterQueueItem {
  return {
    id: createItemId(),
    storeCode: input.storeCode,
    productName: input.productName,
    addedAt: new Date().toISOString(),
    poster: input.poster,
  };
}

function snapshotOf(state: PromoPosterQueueSnapshot): PromoPosterQueueSnapshot {
  return { items: state.items, style: state.style, size: state.size, impose: state.impose };
}

let persistChain: Promise<void> = Promise.resolve();

/** 串行写入，避免连续操作时旧快照晚于新快照落盘。 */
function persist(state: PromoPosterQueueSnapshot) {
  const snapshot = snapshotOf(state);
  persistChain = persistChain
    .then(() => AppAsyncStorage.setObject(STORAGE_KEY, snapshot))
    .catch((error) => {
      console.warn("[promo-posters] 保存待打印队列失败", error);
    });
}

export const usePromoPosterQueueStore = create<PromoPosterQueueState>((set, get) => {
  // 每次修改后落盘；hydrate 完成前的修改也会在 hydrate 合并后一起写入。
  const update = (partial: Partial<PromoPosterQueueSnapshot>) => {
    set(partial);
    if (get().hydrated) persist(get());
  };

  return {
    ...DEFAULT_PROMO_POSTER_QUEUE_SNAPSHOT,
    items: [],
    hydrated: false,
    add: (input) => {
      const result = resolveQueueAddition(get().items, input.storeCode);
      if (result !== "ok") return result;
      update({ items: [...get().items, toQueueItem(input)] });
      return "ok";
    },
    replaceAll: (input) => update({ items: [toQueueItem(input)] }),
    remove: (id) => update({ items: get().items.filter((item) => item.id !== id) }),
    removeMany: (ids) => {
      const removed = new Set(ids);
      update({ items: get().items.filter((item) => !removed.has(item.id)) });
    },
    restore: (item, index) => {
      const items = get().items;
      if (items.some((existing) => existing.id === item.id) || items.length >= PROMO_POSTER_QUEUE_LIMIT) return;
      const next = [...items];
      next.splice(Math.max(0, Math.min(index, next.length)), 0, item);
      update({ items: next });
    },
    clear: () => update({ items: [] }),
    setStyle: (style) => update({ style }),
    setSize: (size) => update({ size }),
    setImpose: (impose) => update({ impose }),
  };
});

let hydratePromise: Promise<void> | null = null;

/** 幂等恢复本地队列；恢复前已加入的条目追加在已保存条目之后。 */
export function hydratePromoPosterQueue() {
  if (!hydratePromise) {
    hydratePromise = (async () => {
      let stored: PromoPosterQueueSnapshot | null = null;
      try {
        stored = normalizeStoredQueueSnapshot(await AppAsyncStorage.getObject(STORAGE_KEY));
      } catch (error) {
        console.warn("[promo-posters] 读取待打印队列失败", error);
      }
      const current = usePromoPosterQueueStore.getState();
      if (stored) {
        usePromoPosterQueueStore.setState({
          items: [...stored.items, ...current.items].slice(0, PROMO_POSTER_QUEUE_LIMIT),
          style: stored.style,
          size: stored.size,
          impose: stored.impose,
          hydrated: true,
        });
      } else {
        usePromoPosterQueueStore.setState({ hydrated: true });
      }
      persist(usePromoPosterQueueStore.getState());
    })();
  }
  return hydratePromise;
}

/** 页面挂载时确保队列已从本地恢复。 */
export function usePromoPosterQueueHydration() {
  const hydrated = usePromoPosterQueueStore((state) => state.hydrated);
  useEffect(() => {
    if (!hydrated) void hydratePromoPosterQueue();
  }, [hydrated]);
  return hydrated;
}
