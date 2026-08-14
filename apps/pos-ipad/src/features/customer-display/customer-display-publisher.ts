import {
  CUSTOMER_DISPLAY_VISIBLE_ITEM_LIMIT,
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
const CUSTOMER_DISPLAY_SNAPSHOT_ITEM_LIMIT = 100;

/**
 * 从共享购物车只投影客显白名单。银行卡、顾客、收银员和授权信息不在输入面中，
 * line sync provenance 也不会进入最终 snapshot。
 */
export function buildCustomerDisplaySnapshot(
  revision: number,
  frame: CustomerDisplayFrame,
  advertisementCacheRootUri?: string | null,
  visibleItemStart?: number,
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
  const cartLines = cart?.lines ?? [];
  const projectedLines = cartLines.slice(0, CUSTOMER_DISPLAY_SNAPSHOT_ITEM_LIMIT);
  const candidate = {
    revision,
    mode: frame.mode,
    items: projectedLines.map((line) => ({
      name: line.displayName,
      quantity: line.quantity,
      unitPrice: createAud(line.unitPrice.cents),
      amount: createAud(line.actualAmount.cents),
    })),
    summary: {
      itemQuantity: sumFixedQuantities(cartLines.map((line) => line.quantity)),
      skuCount: cartLines.length,
      subtotal: createAud(cart?.subtotal.cents ?? 0),
    },
    visibleItemStart:
      visibleItemStart ?? defaultVisibleItemStart(projectedLines.length),
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
  private lastObservedCart: CartSnapshot | null = null;
  private queue: Promise<unknown> = Promise.resolve();
  private readonly advertisementCacheRootUri: string | null;
  private visibleItemStart = 0;

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
    const visibleItemStart = this.resolveVisibleItemStart(frame.cart);
    const draft = buildCustomerDisplaySnapshot(
      0,
      frame,
      this.advertisementCacheRootUri,
      visibleItemStart,
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
      visibleItemStart,
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

  private resolveVisibleItemStart(cart: CartSnapshot | null): number {
    // 使用完整购物车判断变化，避免删除前 100 行时把边界补入项误判为新增。
    const currentLines = cart?.lines ?? [];
    const previousLines = this.lastObservedCart?.lines ?? [];
    const visibleItemCount = Math.min(
      currentLines.length,
      CUSTOMER_DISPLAY_SNAPSHOT_ITEM_LIMIT,
    );
    const maximumStart = defaultVisibleItemStart(visibleItemCount);
    let nextStart = Math.min(this.visibleItemStart, maximumStart);

    if (currentLines.length === 0) {
      nextStart = 0;
    } else if (previousLines.length === 0) {
      nextStart = maximumStart;
    } else {
      const targetIndex = recentlyChangedItemIndex(
        previousLines,
        currentLines,
      );
      if (targetIndex !== null && targetIndex < visibleItemCount) {
        if (targetIndex < nextStart) {
          nextStart = targetIndex;
        } else if (
          targetIndex >=
          nextStart + CUSTOMER_DISPLAY_VISIBLE_ITEM_LIMIT
        ) {
          nextStart =
            targetIndex - CUSTOMER_DISPLAY_VISIBLE_ITEM_LIMIT + 1;
        }
      }
      nextStart = Math.max(0, Math.min(nextStart, maximumStart));
    }

    this.lastObservedCart = cart;
    this.visibleItemStart = nextStart;
    return nextStart;
  }
}

type ProjectedCartLine = CartSnapshot["lines"][number];

function defaultVisibleItemStart(itemCount: number): number {
  return Math.max(0, itemCount - CUSTOMER_DISPLAY_VISIBLE_ITEM_LIMIT);
}

function recentlyChangedItemIndex(
  previous: readonly ProjectedCartLine[],
  current: readonly ProjectedCartLine[],
): number | null {
  const previousById = new Map(
    previous.map((line) => [line.lineId, line] as const),
  );
  const currentIndexById = new Map(
    current.map((line, index) => [line.lineId, index] as const),
  );
  const changedCurrentIndexes: number[] = [];

  for (let index = 0; index < current.length; index += 1) {
    const line = current[index]!;
    const before = previousById.get(line.lineId);
    if (before === undefined || customerVisibleLineChanged(before, line)) {
      changedCurrentIndexes.push(index);
    }
  }

  for (let index = 0; index < previous.length; index += 1) {
    if (currentIndexById.has(previous[index]!.lineId)) continue;

    // 删除后优先显示原位置的下一项；删除末项时回到新的最后一项。
    let adjacentIndex: number | null = null;
    for (
      let successorIndex = index + 1;
      successorIndex < previous.length;
      successorIndex += 1
    ) {
      const currentIndex = currentIndexById.get(
        previous[successorIndex]!.lineId,
      );
      if (currentIndex !== undefined) {
        adjacentIndex = currentIndex;
        break;
      }
    }
    if (adjacentIndex !== null) {
      changedCurrentIndexes.push(adjacentIndex);
    } else if (current.length > 0) {
      changedCurrentIndexes.push(current.length - 1);
    }
  }

  // 同一快照存在多种商品变化时，以当前购物车顺序最靠后的目标为准。
  return changedCurrentIndexes.length === 0
    ? null
    : Math.max(...changedCurrentIndexes);
}

function customerVisibleLineChanged(
  before: ProjectedCartLine,
  after: ProjectedCartLine,
): boolean {
  return (
    before.displayName !== after.displayName ||
    before.quantity !== after.quantity ||
    before.unitPrice.currency !== after.unitPrice.currency ||
    before.unitPrice.cents !== after.unitPrice.cents ||
    // 行折扣本身就是最近商品操作；即使金额重算稍晚，也应把该行带入视野。
    before.discount.currency !== after.discount.currency ||
    before.discount.cents !== after.discount.cents ||
    before.actualAmount.currency !== after.actualAmount.currency ||
    before.actualAmount.cents !== after.actualAmount.cents
  );
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

/**
 * 数量最多三位小数，先统一换算成千分位整数再求和，避免称重商品产生
 * 0.1 + 0.2 之类的二进制浮点尾差。
 */
function sumFixedQuantities(quantities: readonly string[]): string {
  let totalThousandths = 0n;

  for (const quantity of quantities) {
    const match = /^(-?)(\d+)(?:\.(\d{1,3}))?$/.exec(quantity);
    if (match === null) {
      throw new TypeError("Customer display quantity is invalid.");
    }

    const whole = BigInt(match[2]!);
    const fraction = BigInt((match[3] ?? "").padEnd(3, "0") || "0");
    const thousandths = whole * 1_000n + fraction;
    totalThousandths += match[1] === "-" ? -thousandths : thousandths;
  }

  const sign = totalThousandths < 0n ? "-" : "";
  const absolute = totalThousandths < 0n ? -totalThousandths : totalThousandths;
  const whole = absolute / 1_000n;
  const fraction = String(absolute % 1_000n)
    .padStart(3, "0")
    .replace(/0+$/, "");
  return `${sign}${whole}${fraction.length > 0 ? `.${fraction}` : ""}`;
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
          unitPrice:
            item.unitPrice === undefined
              ? undefined
              : Object.freeze({ ...item.unitPrice }),
          amount: Object.freeze({ ...item.amount }),
        }),
      ),
    ),
    summary:
      snapshot.summary === undefined
        ? undefined
        : Object.freeze({
            ...snapshot.summary,
            subtotal: Object.freeze({ ...snapshot.summary.subtotal }),
          }),
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
