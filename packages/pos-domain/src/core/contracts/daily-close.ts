import type { AuditEventDraft } from "./order";

export const AUD_CASH_DENOMINATIONS_CENTS = Object.freeze([
  10_000,
  5_000,
  2_000,
  1_000,
  500,
  200,
  100,
  50,
  20,
  10,
  5,
] as const);

export type AudCashDenominationCents =
  (typeof AUD_CASH_DENOMINATIONS_CENTS)[number];

export type DailyCloseTenderMethod = "cash" | "card" | "voucher";

export type DailyCloseTenderBreakdown = Readonly<{
  method: DailyCloseTenderMethod;
  salesCents: number;
  refundCents: number;
  netCents: number;
}>;

export type DailyCloseScope = Readonly<{
  businessDate: string;
  periodFromIso: string;
  periodToIso: string;
  storeCode: string;
  deviceCode: string;
}>;

export type DailyCloseSummary = DailyCloseScope &
  Readonly<{
    orderCount: number;
    returnQuantity: string;
    tenders: readonly DailyCloseTenderBreakdown[];
    expectedCashCents: number;
  }>;

export type DailyCloseDenominationCount = Readonly<{
  denominationCents: AudCashDenominationCents;
  quantity: number;
  subtotalCents: number;
}>;

export type DailyCloseArchive = DailyCloseSummary &
  Readonly<{
    closeId: string;
    savedCashierId: string;
    savedCashierName: string;
    savedAtIso: string;
    denominations: readonly DailyCloseDenominationCount[];
    notesSubtotalCents: number;
    coinsSubtotalCents: number;
    countedCashCents: number;
    varianceCents: number;
  }>;

export type DailyCloseArchiveCommit = Readonly<{
  archive: DailyCloseArchive;
  audit: AuditEventDraft;
}>;

export interface DailyCloseRepositoryPort {
  summarize(scope: DailyCloseScope): Promise<DailyCloseSummary>;
  saveArchive(
    input: DailyCloseArchiveCommit,
  ): Promise<Readonly<{ replayed: boolean; archive: DailyCloseArchive }>>;
  getArchive(closeId: string): Promise<DailyCloseArchive | null>;
  listArchives(
    scope: Readonly<{
      storeCode: string;
      deviceCode: string;
      businessDate?: string | null;
      limit: number;
    }>,
  ): Promise<readonly DailyCloseArchive[]>;
}

export function normalizeDailyCloseCounts(
  input: readonly Readonly<{
    denominationCents: number;
    quantity: number;
  }>[],
): readonly DailyCloseDenominationCount[] {
  const byDenomination = new Map<number, number>();
  for (const entry of input) {
    if (
      !AUD_CASH_DENOMINATIONS_CENTS.includes(
        entry.denominationCents as AudCashDenominationCents,
      )
    ) {
      throw new TypeError("Daily close denomination is unsupported.");
    }
    if (!Number.isSafeInteger(entry.quantity) || entry.quantity < 0) {
      throw new TypeError(
        "Daily close quantity must be a non-negative integer.",
      );
    }
    if (byDenomination.has(entry.denominationCents)) {
      throw new TypeError("Daily close denomination is duplicated.");
    }
    byDenomination.set(entry.denominationCents, entry.quantity);
  }
  return Object.freeze(
    AUD_CASH_DENOMINATIONS_CENTS.map((denominationCents) => {
      const quantity = byDenomination.get(denominationCents) ?? 0;
      const subtotalCents = denominationCents * quantity;
      if (!Number.isSafeInteger(subtotalCents)) {
        throw new TypeError("Daily close denomination subtotal is invalid.");
      }
      return Object.freeze({
        denominationCents,
        quantity,
        subtotalCents,
      });
    }),
  );
}
