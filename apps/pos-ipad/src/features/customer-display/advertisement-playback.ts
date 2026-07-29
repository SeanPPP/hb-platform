import type {
  CustomerDisplayAdvertisementItem,
  CustomerDisplayAdvertisementRemotePort,
} from "./advertisement-api";

import type { CustomerDisplaySnapshot } from "@/core/contracts";

const DEFAULT_REFRESH_INTERVAL_MS = 5 * 60 * 1_000;
const DEFAULT_ROTATION_INTERVAL_MS = 10_000;

export type CachedCustomerDisplayAdvertisement =
  CustomerDisplayAdvertisementItem &
    Readonly<{ localUri: string }>;

export interface CustomerDisplayAdvertisementCachePort {
  cache(
    items: readonly CustomerDisplayAdvertisementItem[],
  ): Promise<readonly CachedCustomerDisplayAdvertisement[]>;
}

export interface CustomerDisplayAdvertisementSinkPort {
  setAdvert(
    advert: CustomerDisplaySnapshot["advert"],
  ): Promise<unknown>;
}

export type AdvertisementRefreshResult =
  | "updated"
  | "unchanged"
  | "retained"
  | "cleared";

export type CustomerDisplayAdvertisementPlaybackOptions = Readonly<{
  remote: CustomerDisplayAdvertisementRemotePort;
  cache: CustomerDisplayAdvertisementCachePort;
  sink: CustomerDisplayAdvertisementSinkPort;
  now(): Date;
  refreshIntervalMs?: number;
  scheduler?: Readonly<{
    every(intervalMs: number, listener: () => void): () => void;
  }>;
}>;

/**
 * 远端响应只用于选择素材；客显永远只收到缓存 Port 返回的本地 file URI。
 * 同门店刷新失败保留最后快照，切换门店失败则立即清空，避免跨店广告泄漏。
 */
export class CustomerDisplayAdvertisementPlayback {
  private readonly refreshIntervalMs: number;
  private currentIndex = -1;
  private items: readonly CachedCustomerDisplayAdvertisement[] = [];
  private lastRefreshMs = Number.NEGATIVE_INFINITY;
  private storeCode: string | null = null;
  private inFlight: Promise<AdvertisementRefreshResult> | null = null;
  private stopRefreshTimer: (() => void) | null = null;
  private stopRotationTimer: (() => void) | null = null;

  public constructor(
    private readonly options: CustomerDisplayAdvertisementPlaybackOptions,
  ) {
    const interval =
      options.refreshIntervalMs ?? DEFAULT_REFRESH_INTERVAL_MS;
    if (!Number.isSafeInteger(interval) || interval <= 0) {
      throw new TypeError("Advertisement refresh interval is invalid.");
    }
    this.refreshIntervalMs = interval;
  }

  public refresh(
    requestedStoreCode: string,
    force = false,
  ): Promise<AdvertisementRefreshResult> {
    const storeCode = normalizeStoreCode(requestedStoreCode);
    if (
      !force &&
      this.storeCode === storeCode &&
      this.nowMs() - this.lastRefreshMs < this.refreshIntervalMs
    ) {
      return Promise.resolve("unchanged");
    }
    if (this.inFlight) return this.inFlight;
    const operation = this.refreshOnce(storeCode).finally(() => {
      if (this.inFlight === operation) this.inFlight = null;
    });
    this.inFlight = operation;
    return operation;
  }

  public start(requestedStoreCode: string): void {
    const storeCode = normalizeStoreCode(requestedStoreCode);
    this.stop();
    void this.refresh(storeCode, true).catch(() => undefined);
    const scheduler = this.options.scheduler ?? defaultScheduler;
    this.stopRefreshTimer = scheduler.every(
      this.refreshIntervalMs,
      () => {
        void this.refresh(storeCode, true).catch(() => undefined);
      },
    );
    this.stopRotationTimer = scheduler.every(
      DEFAULT_ROTATION_INTERVAL_MS,
      () => {
        void this.advance().catch(() => undefined);
      },
    );
  }

  public stop(): void {
    this.stopRefreshTimer?.();
    this.stopRotationTimer?.();
    this.stopRefreshTimer = null;
    this.stopRotationTimer = null;
  }

  public async advance(): Promise<boolean> {
    const now = this.nowMs();
    this.items = Object.freeze(
      this.items.filter((item) => isEffective(item, now)),
    );
    if (this.items.length === 0) {
      this.currentIndex = -1;
      await this.options.sink.setAdvert(null);
      return false;
    }
    this.currentIndex = (this.currentIndex + 1) % this.items.length;
    await this.publishCurrent();
    return true;
  }

  private async refreshOnce(
    storeCode: string,
  ): Promise<AdvertisementRefreshResult> {
    const storeChanged =
      this.storeCode !== null && this.storeCode !== storeCode;
    try {
      const response = await this.options.remote.getActive(storeCode);
      if (response.storeCode !== storeCode) {
        throw new TypeError("Advertisement response store is invalid.");
      }
      const now = this.nowMs();
      const current = response.items
        .filter((item) => isEffective(item, now))
        .sort(
          (left, right) =>
            left.sortOrder - right.sortOrder ||
            left.id.localeCompare(right.id),
        );
      const cached = await this.options.cache.cache(current);
      assertCachedItems(current, cached);
      this.items = Object.freeze([...cached]);
      this.storeCode = storeCode;
      this.lastRefreshMs = now;
      this.currentIndex = this.items.length > 0 ? 0 : -1;
      await this.publishCurrent();
      return "updated";
    } catch {
      if (storeChanged || this.storeCode === null) {
        this.items = Object.freeze([]);
        this.storeCode = storeCode;
        this.currentIndex = -1;
        await this.options.sink.setAdvert(null);
        return "cleared";
      }
      return "retained";
    }
  }

  private publishCurrent(): Promise<unknown> {
    const item =
      this.currentIndex >= 0 ? this.items[this.currentIndex] : undefined;
    return this.options.sink.setAdvert(
      item
        ? Object.freeze({
            kind: item.kind,
            localUri: item.localUri,
          })
        : null,
    );
  }

  private nowMs(): number {
    const value = this.options.now();
    const timestamp = value.getTime();
    if (!Number.isFinite(timestamp)) {
      throw new TypeError("Advertisement clock is invalid.");
    }
    return timestamp;
  }
}

const defaultScheduler = Object.freeze({
  every(intervalMs: number, listener: () => void): () => void {
    const timer = setInterval(listener, intervalMs);
    return () => clearInterval(timer);
  },
});

function isEffective(
  item: CustomerDisplayAdvertisementItem,
  nowMs: number,
): boolean {
  return (
    Date.parse(item.effectiveStartIso) <= nowMs &&
    Date.parse(item.effectiveEndIso) >= nowMs
  );
}

function assertCachedItems(
  requested: readonly CustomerDisplayAdvertisementItem[],
  cached: readonly CachedCustomerDisplayAdvertisement[],
): void {
  let requestedIndex = 0;
  const seen = new Set<string>();
  for (const item of cached) {
    while (
      requestedIndex < requested.length &&
      requested[requestedIndex]?.id !== item.id
    ) {
      requestedIndex += 1;
    }
    const source = requested[requestedIndex];
    if (
      !source ||
      seen.has(item.id) ||
      item.kind !== source.kind ||
      !isLocalFileUri(item.localUri)
    ) {
      throw new TypeError("Advertisement cache result is invalid.");
    }
    seen.add(item.id);
    requestedIndex += 1;
  }
}

function isLocalFileUri(value: unknown): value is string {
  if (typeof value !== "string") return false;
  try {
    const parsed = new URL(value);
    return (
      parsed.protocol === "file:" &&
      (parsed.hostname === "" || parsed.hostname === "localhost") &&
      !parsed.username &&
      !parsed.password &&
      !parsed.search &&
      !parsed.hash
    );
  } catch {
    return false;
  }
}

function normalizeStoreCode(value: unknown): string {
  if (typeof value !== "string") {
    throw new TypeError("Advertisement store code is invalid.");
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > 128 ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError("Advertisement store code is invalid.");
  }
  return normalized;
}
