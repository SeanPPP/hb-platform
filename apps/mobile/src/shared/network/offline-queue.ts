/**
 * 离线请求队列：持久化"用户操作产生的待补传请求"。
 *
 * 业务侧在请求因网络失败而无法提交时调用 enqueue 入队；
 * 网络恢复后由 use-network-recovery 按 FIFO 顺序补传，成功后 dequeue。
 * 队列整体持久化到 AsyncStorage，App 重启后仍可继续补传。
 */
import { AppAsyncStorage } from "@/shared/storage/async-storage";

/** 一个待补传的请求描述（仅存 URL/方法/头/体，不含本地机密）。 */
export type QueuedRequest = Readonly<{
  /** 唯一 ID（默认由队列生成）。 */
  id: string;
  /** 完整请求 URL。 */
  url: string;
  /** HTTP 方法：GET/POST/PUT/DELETE 等。 */
  method: string;
  /** 需要原样重放的请求头（敏感头由请求时注入，不落盘）。 */
  headers?: Record<string, string>;
  /** JSON 序列化的请求体。 */
  body?: string;
  /** 入队时间（ISO）。 */
  createdAt: string;
  /** 已重试次数。 */
  retryCount: number;
  /** 最大重试次数，超过后移出活跃队列。 */
  maxRetries: number;
}>;

/** enqueue 的入参（无需调用方关心 id/retryCount 等内部字段）。 */
export type EnqueueRequestInput = Readonly<{
  url: string;
  method: string;
  headers?: Record<string, string>;
  body?: string;
  maxRetries?: number;
}>;

/** 队列持久化端口：测试可注入内存实现，生产使用 AsyncStorage。 */
export type OfflineQueueStorage = {
  load(): Promise<QueuedRequest[]>;
  save(items: QueuedRequest[]): Promise<void>;
};

const QUEUE_STORAGE_KEY = "@offline-queue/v1";
export const DEFAULT_MAX_RETRIES = 5;
export const DEFAULT_MAX_QUEUE_LENGTH = 100;

/** 基于 AppAsyncStorage 的默认持久化实现。 */
export const asyncStorageOfflineQueueStorage: OfflineQueueStorage = {
  async load() {
    return (
      (await AppAsyncStorage.getObject<QueuedRequest[]>(QUEUE_STORAGE_KEY)) ?? []
    );
  },
  async save(items: QueuedRequest[]) {
    await AppAsyncStorage.setObject(QUEUE_STORAGE_KEY, items);
  },
};

export type OfflineQueueOptions = {
  storage?: OfflineQueueStorage;
  /** ID 生成器，便于测试固定值。 */
  createId?: () => string;
  /** 队列长度上限，超出丢弃最旧条目（默认 100）。 */
  maxQueueLength?: number;
  /** 条目被丢弃（队列满）时的回调，便于记录日志。 */
  onDiscard?: (item: QueuedRequest, reason: "queue-full") => void;
};

export class OfflineRequestQueue {
  private readonly storage: OfflineQueueStorage;
  private readonly createId: () => string;
  private readonly maxQueueLength: number;
  private readonly onDiscard: OfflineQueueOptions["onDiscard"];

  public constructor(options: OfflineQueueOptions = {}) {
    this.storage = options.storage ?? asyncStorageOfflineQueueStorage;
    this.createId =
      options.createId ??
      (() =>
        `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`);
    this.maxQueueLength = options.maxQueueLength ?? DEFAULT_MAX_QUEUE_LENGTH;
    this.onDiscard = options.onDiscard;
  }

  /**
   * 将请求加入队尾（FIFO）。重复调用会生成新条目（不做内容去重，
   * 业务幂等由调用方通过 idempotency key 等机制保证）。
   * 队列达到上限时丢弃最旧条目并触发 onDiscard 回调。
   */
  public async enqueue(input: EnqueueRequestInput): Promise<QueuedRequest> {
    const item: QueuedRequest = {
      id: this.createId(),
      url: input.url,
      method: input.method,
      headers: input.headers,
      body: input.body,
      createdAt: new Date().toISOString(),
      retryCount: 0,
      maxRetries: input.maxRetries ?? DEFAULT_MAX_RETRIES,
    };
    const items = await this.loadWithFallback();
    items.push(item);
    if (items.length > this.maxQueueLength) {
      const dropped = items.shift();
      if (dropped && this.onDiscard) {
        this.onDiscard(dropped, "queue-full");
      }
    }
    await this.storage.save(items);
    return item;
  }

  /** 按 id 从队列移除（补传成功后调用），返回是否确有移除。 */
  public async dequeue(id: string): Promise<boolean> {
    const items = await this.loadWithFallback();
    const next = items.filter((item) => item.id !== id);
    if (next.length === items.length) {
      return false;
    }
    await this.storage.save(next);
    return true;
  }

  /** 重试次数 +1（补传失败时调用）；返回更新后的条目，条目不存在返回 null。 */
  public async markRetry(id: string): Promise<QueuedRequest | null> {
    const items = await this.loadWithFallback();
    const index = items.findIndex((item) => item.id === id);
    if (index < 0) {
      return null;
    }
    const updated: QueuedRequest = {
      ...items[index],
      retryCount: items[index].retryCount + 1,
    };
    items[index] = updated;
    await this.storage.save(items);
    return updated;
  }

  /** 读取全部待补传请求（FIFO 顺序）。 */
  public async getAll(): Promise<QueuedRequest[]> {
    return this.loadWithFallback();
  }

  /** 待补传条目数。 */
  public async size(): Promise<number> {
    return (await this.loadWithFallback()).length;
  }

  /** 清空队列。 */
  public async clear(): Promise<void> {
    await this.storage.save([]);
  }

  /** 读取失败时按空队列处理，避免单个存储故障阻塞补传流程。 */
  private async loadWithFallback(): Promise<QueuedRequest[]> {
    try {
      return await this.storage.load();
    } catch {
      // 存储读取异常不阻断队列操作，按空队列继续。
      return [];
    }
  }
}
