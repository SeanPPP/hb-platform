import type {
  CatalogRemoteLookupItem,
  CatalogRemoteLookupPort,
  LocalCatalogReadPort,
} from "./remote-catalog-fallback";

import type { LocalCatalogMatch } from "@/core/db/catalog-repository";

export type CatalogLookupOverlayApplyResult =
  | "applied"
  | "stale-generation";

/**
 * 在线单品校准只需要目录代次与窄写入面；具体 SQLCipher 表、事务和清理由组合根适配。
 */
export interface CatalogLookupOverlayWritePort {
  getActiveSnapshotId(): Promise<string | null>;
  upsert(input: Readonly<{
    baseSnapshotId: string | null;
    item: LocalCatalogMatch;
  }>): Promise<CatalogLookupOverlayApplyResult>;
  tombstone(input: Readonly<{
    baseSnapshotId: string | null;
    storeCode: string;
    lookupCodeNormalized: string;
  }>): Promise<CatalogLookupOverlayApplyResult>;
}

export type CatalogLookupRevalidationResult =
  | Readonly<{
      kind: "found";
      baseSnapshotId: string | null;
      item: LocalCatalogMatch;
    }>
  | Readonly<{
      kind: "not-found";
      baseSnapshotId: string | null;
    }>
  | Readonly<{
      kind: "unavailable";
    }>;

export interface CatalogLookupRevalidationPort {
  revalidate(lookupCode: string): Promise<CatalogLookupRevalidationResult>;
  isCurrentBaseSnapshot(baseSnapshotId: string | null): Promise<boolean>;
}

export type RemoteCatalogLookupRevalidationOptions = Readonly<{
  storeCode: string;
  remote: CatalogRemoteLookupPort;
  overlay: CatalogLookupOverlayWritePort;
  isOnline: () => boolean | Promise<boolean>;
  local?: LocalCatalogReadPort;
}>;

type RevalidationAttemptResult =
  | CatalogLookupRevalidationResult
  | Readonly<{ kind: "stale-generation" }>;

/**
 * WPF 式单品校准：同代次同码合并网络请求，远程结果先可靠写入覆盖层，
 * 目录在请求期间换代则只按新代次重试一次。
 */
export class RemoteCatalogLookupRevalidationService
  implements CatalogLookupRevalidationPort
{
  private readonly storeCode: string;
  private readonly pending = new Map<
    string,
    Promise<RevalidationAttemptResult>
  >();

  public constructor(
    private readonly options: RemoteCatalogLookupRevalidationOptions,
  ) {
    this.storeCode = requiredText(options.storeCode, "storeCode");
  }

  public async revalidate(
    lookupCode: string,
  ): Promise<CatalogLookupRevalidationResult> {
    const lookupCodeNormalized = normalizeLookupCode(
      requiredText(lookupCode, "lookupCode"),
    );
    try {
      if (!(await this.options.isOnline())) {
        return { kind: "unavailable" };
      }

      // 初次代次和换代后的新代次各允许一次，避免目录持续激活时无限追赶。
      for (let attempt = 0; attempt < 2; attempt += 1) {
        const baseSnapshotId =
          await this.options.overlay.getActiveSnapshotId();
        const result = await this.runDeduplicatedAttempt(
          lookupCodeNormalized,
          baseSnapshotId,
        );
        if (result.kind !== "stale-generation") {
          return result;
        }
      }
    } catch {
      // 网络、状态探针或 SQLite 暂不可用时，调用方必须保留本地购物车结果。
    }
    return { kind: "unavailable" };
  }

  public async isCurrentBaseSnapshot(
    baseSnapshotId: string | null,
  ): Promise<boolean> {
    try {
      return (
        (await this.options.overlay.getActiveSnapshotId()) ===
        baseSnapshotId
      );
    } catch {
      return false;
    }
  }

  private runDeduplicatedAttempt(
    lookupCodeNormalized: string,
    baseSnapshotId: string | null,
  ): Promise<RevalidationAttemptResult> {
    const key = [
      this.storeCode,
      baseSnapshotId ?? "<no-active-snapshot>",
      lookupCodeNormalized,
    ].join("\u0000");
    const existing = this.pending.get(key);
    if (existing) return existing;

    const pending = this.fetchAndPersist(
      lookupCodeNormalized,
      baseSnapshotId,
    );
    this.pending.set(key, pending);
    void pending.then(
      () => {
        if (this.pending.get(key) === pending) this.pending.delete(key);
      },
      () => {
        if (this.pending.get(key) === pending) this.pending.delete(key);
      },
    );
    return pending;
  }

  private async fetchAndPersist(
    lookupCodeNormalized: string,
    baseSnapshotId: string | null,
  ): Promise<RevalidationAttemptResult> {
    try {
      const result = await this.options.remote.lookup({
        storeCode: this.storeCode,
        lookupCode: lookupCodeNormalized,
      });
      if (
        result.storeCode !== this.storeCode ||
        result.lookupCodeNormalized !== lookupCodeNormalized ||
        normalizeLookupCode(result.lookupCode) !== lookupCodeNormalized
      ) {
        return { kind: "unavailable" };
      }

      if (!result.found) {
        if (result.item !== null) return { kind: "unavailable" };
        const applied = await this.options.overlay.tombstone({
          baseSnapshotId,
          storeCode: this.storeCode,
          lookupCodeNormalized,
        });
        return applied === "stale-generation"
          ? { kind: "stale-generation" }
          : { kind: "not-found", baseSnapshotId };
      }

      if (
        result.item === null ||
        result.item.storeCode !== this.storeCode ||
        result.item.lookupCodeNormalized !== lookupCodeNormalized ||
        normalizeLookupCode(result.item.lookupCode) !== lookupCodeNormalized
      ) {
        return { kind: "unavailable" };
      }
      const item = await this.toLocalMatch(result.item);
      const applied = await this.options.overlay.upsert({
        baseSnapshotId,
        item,
      });
      return applied === "stale-generation"
        ? { kind: "stale-generation" }
        : { kind: "found", baseSnapshotId, item };
    } catch {
      return { kind: "unavailable" };
    }
  }

  private async toLocalMatch(
    item: CatalogRemoteLookupItem,
  ): Promise<LocalCatalogMatch> {
    let taxRateBasisPoints: number | null = null;
    const local = await this.options.local?.findExact(
      item.lookupCodeNormalized,
    );
    if (
      local?.storeCode === item.storeCode &&
      local.productCode === item.productCode &&
      local.referenceCode === item.referenceCode &&
      local.lookupCodeNormalized === item.lookupCodeNormalized
    ) {
      taxRateBasisPoints = local.taxRateBasisPoints;
    }

    return Object.freeze({
      storeCode: item.storeCode,
      productCode: item.productCode,
      referenceCode: item.referenceCode,
      itemNumber: item.itemNumber,
      displayName: item.displayName,
      barcode: item.barcode,
      lookupCode: item.lookupCode,
      lookupCodeNormalized: item.lookupCodeNormalized,
      retailPriceCents: moneyCents(item.retailPrice),
      priceSource: item.priceSource,
      priceSourceLabel: item.priceSourceLabel,
      quantityFactor: item.quantityFactor,
      taxRateBasisPoints,
      updatedAtIso: item.updatedAt,
      rowVersion: item.rowVersion,
      productImage: item.productImage,
      discountRate: item.discountRate,
      isSpecialProduct: item.isSpecialProduct,
    });
  }
}

function requiredText(value: string, label: string): string {
  const normalized = value.trim();
  if (
    !normalized ||
    normalized.length > 128 ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`${label} is required`);
  }
  return normalized;
}

function normalizeLookupCode(value: string): string {
  return value.trim().toUpperCase();
}

function moneyCents(value: number): number {
  if (!Number.isFinite(value) || value < 0) {
    throw new TypeError("item.retailPrice is invalid");
  }
  const scaled = value * 100;
  const cents = Math.round(scaled);
  if (
    !Number.isSafeInteger(cents) ||
    Math.abs(scaled - cents) >
      Number.EPSILON * Math.max(100, Math.abs(scaled))
  ) {
    throw new RangeError("item.retailPrice must use exact cents");
  }
  return cents;
}
