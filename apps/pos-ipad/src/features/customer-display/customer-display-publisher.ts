import {
  createAud,
  CustomerDisplaySnapshotSchema,
  type CartSnapshot,
  type CustomerDisplaySnapshot,
  type DisplayStatus,
  type ExternalCustomerDisplayPort,
} from "@/core/contracts";
import {
  normalizeAdvertisementCacheRootUri,
  normalizeLocalAdvertisementUri,
} from "@/core/peripherals/customer-display/local-advertisement-uri";

export type CustomerDisplayFrame = Readonly<{
  mode: CustomerDisplaySnapshot["mode"];
  cart: CartSnapshot | null;
  changeCents: number;
  advert: CustomerDisplaySnapshot["advert"];
}>;

export type CustomerDisplayPublishResult =
  | Readonly<{ status: "published"; revision: number }>
  | Readonly<{ status: "unchanged"; revision: number }>
  | Readonly<{
      status: "failed";
      revision: number;
      errorCode: "DISPLAY_PUBLISH_FAILED";
    }>;

export type CustomerDisplayEnableResult =
  | Readonly<{ status: "updated" }>
  | Readonly<{
      status: "failed";
      errorCode: "DISPLAY_ENABLE_FAILED";
    }>;

export type CustomerDisplayPublisherOptions = Readonly<{
  advertisementCacheRootUri?: string | null;
}>;

let producerSessionRevision = 0;

/**
 * 从共享购物车只投影客显白名单。银行卡、顾客、收银员和授权信息不在输入面中，
 * line sync provenance 也不会进入最终 snapshot。
 */
export function buildCustomerDisplaySnapshot(
  revision: number,
  frame: CustomerDisplayFrame,
  advertisementCacheRootUri?: string | null,
): CustomerDisplaySnapshot {
  if (!Number.isSafeInteger(revision) || revision < 0) {
    throw new TypeError("Customer display revision must be non-negative.");
  }
  if (!Number.isSafeInteger(frame.changeCents)) {
    throw new TypeError("Customer display change must use safe integer cents.");
  }
  const advert = normalizeLocalAdvert(
    frame.advert,
    advertisementCacheRootUri ?? null,
  );
  const cart = frame.cart;
  const totalCents = cart?.actualAmount.cents ?? 0;
  const candidate = {
    revision,
    mode: frame.mode,
    items: (cart?.lines ?? []).slice(0, 100).map((line) => ({
      name: line.displayName,
      quantity: line.quantity,
      amount: createAud(line.actualAmount.cents),
    })),
    // 与 WPF CustomerDisplayViewModel 一致：GST 是含税应付额中的 1/11。
    gst: createAud(roundRatioAwayFromZero(totalCents, 11)),
    discount: createAud(cart?.discount.cents ?? 0),
    total: createAud(totalCents),
    change: createAud(frame.changeCents),
    advert,
  };
  return freezeSnapshot(CustomerDisplaySnapshotSchema.parse(candidate));
}

/**
 * 发布失败只影响客显状态，不向主交易流程抛出。所有发布串行化，保证原生桥看到的
 * revision 严格递增；一次结果不确定后也永不复用旧 revision。
 */
export class CustomerDisplayPublisher {
  private lastPublishedRevision = 0;
  private lastPublishedFingerprint: string | null = null;
  private queue: Promise<unknown> = Promise.resolve();
  private readonly advertisementCacheRootUri: string | null;

  public constructor(
    private readonly display: ExternalCustomerDisplayPort,
    options: CustomerDisplayPublisherOptions = {},
  ) {
    this.advertisementCacheRootUri =
      options.advertisementCacheRootUri === null ||
      options.advertisementCacheRootUri === undefined
        ? null
        : normalizeAdvertisementCacheRootUri(
            options.advertisementCacheRootUri,
          );
  }

  public publish(
    frame: CustomerDisplayFrame,
  ): Promise<CustomerDisplayPublishResult> {
    const operation = this.queue.then(
      () => this.publishNow(frame),
      () => this.publishNow(frame),
    );
    this.queue = operation;
    return operation;
  }

  public async setEnabled(
    enabled: boolean,
  ): Promise<CustomerDisplayEnableResult> {
    try {
      await this.display.setEnabled(enabled);
      return Object.freeze({ status: "updated" as const });
    } catch {
      return Object.freeze({
        status: "failed" as const,
        errorCode: "DISPLAY_ENABLE_FAILED" as const,
      });
    }
  }

  public async getStatus(): Promise<DisplayStatus> {
    try {
      return await this.display.getStatus();
    } catch {
      return "failed";
    }
  }

  public subscribe(listener: (status: DisplayStatus) => void): () => void {
    return this.display.subscribe(listener);
  }

  private async publishNow(
    frame: CustomerDisplayFrame,
  ): Promise<CustomerDisplayPublishResult> {
    const draft = buildCustomerDisplaySnapshot(
      0,
      frame,
      this.advertisementCacheRootUri,
    );
    const fingerprint = snapshotFingerprint(draft);
    if (fingerprint === this.lastPublishedFingerprint) {
      return Object.freeze({
        status: "unchanged" as const,
        revision: this.lastPublishedRevision,
      });
    }
    const revision = allocateProducerSessionRevision();
    const snapshot = buildCustomerDisplaySnapshot(
      revision,
      frame,
      this.advertisementCacheRootUri,
    );
    try {
      await this.display.publish(snapshot);
      this.lastPublishedFingerprint = fingerprint;
      this.lastPublishedRevision = revision;
      return Object.freeze({
        status: "published" as const,
        revision,
      });
    } catch {
      return Object.freeze({
        status: "failed" as const,
        revision,
        errorCode: "DISPLAY_PUBLISH_FAILED" as const,
      });
    }
  }
}

function normalizeLocalAdvert(
  advert: CustomerDisplaySnapshot["advert"],
  advertisementCacheRootUri: string | null,
): CustomerDisplaySnapshot["advert"] {
  if (advert === null) return null;
  if (advertisementCacheRootUri === null) {
    throw new TypeError("Customer display local advertisement URI is invalid.");
  }
  const localUri = normalizeLocalAdvertisementUri(
    advert.localUri,
    advertisementCacheRootUri,
  );
  return Object.freeze({ kind: advert.kind, localUri });
}

function allocateProducerSessionRevision(): number {
  if (producerSessionRevision >= Number.MAX_SAFE_INTEGER) {
    throw new RangeError("Customer display revision is exhausted.");
  }
  producerSessionRevision += 1;
  return producerSessionRevision;
}

function roundRatioAwayFromZero(value: number, denominator: number): number {
  if (
    !Number.isSafeInteger(value) ||
    !Number.isSafeInteger(denominator) ||
    denominator <= 0
  ) {
    throw new TypeError("Customer display GST inputs are invalid.");
  }
  const sign = value < 0 ? -1 : 1;
  const absolute = Math.abs(value);
  let quotient = Math.floor(absolute / denominator);
  const remainder = absolute % denominator;
  if (remainder * 2 >= denominator) quotient += 1;
  const result = quotient * sign;
  if (!Number.isSafeInteger(result)) {
    throw new TypeError("Customer display GST exceeds safe integer cents.");
  }
  return result;
}

function snapshotFingerprint(snapshot: CustomerDisplaySnapshot): string {
  const { revision: _revision, ...content } = snapshot;
  void _revision;
  return JSON.stringify(content);
}

function freezeSnapshot(
  snapshot: CustomerDisplaySnapshot,
): CustomerDisplaySnapshot {
  const frozen = Object.freeze({
    ...snapshot,
    items: Object.freeze(
      snapshot.items.map((item) =>
        Object.freeze({
          ...item,
          amount: Object.freeze({ ...item.amount }),
        }),
      ),
    ),
    gst: Object.freeze({ ...snapshot.gst }),
    discount: Object.freeze({ ...snapshot.discount }),
    total: Object.freeze({ ...snapshot.total }),
    change: Object.freeze({ ...snapshot.change }),
    advert:
      snapshot.advert === null
        ? null
        : Object.freeze({ ...snapshot.advert }),
  });
  return frozen as unknown as CustomerDisplaySnapshot;
}
