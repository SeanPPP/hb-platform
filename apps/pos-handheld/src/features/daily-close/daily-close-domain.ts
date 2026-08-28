import {
  auditActorPayload,
  normalizeDailyCloseCounts,
  type DailyCloseArchiveCommit,
  type DailyCloseScope,
  type DailyCloseSummary,
} from "@/core/contracts";
import { businessDayUtcRange } from "@hb/pos-sync/features/sync-history/business-day-range";

export type DailyCloseIdentity = Readonly<{
  cashierId: string;
  cashierName: string;
  userGuid: string | null;
  deviceCode: string;
  permissions: readonly string[];
  storeCode: string;
}>;

export function dailyCloseBusinessDayScope(input: {
  businessDate: string;
  businessTimeZone: string;
  deviceCode: string;
  storeCode: string;
}): DailyCloseScope {
  const businessDate = validateBusinessDate(input.businessDate);
  const range = businessDayUtcRange(
    businessDate,
    businessDate,
    requiredText(input.businessTimeZone, "business time zone"),
  );
  if (!range?.dateFromIso || !range.dateToIso) {
    throw new TypeError("Daily close business date or time zone is invalid.");
  }
  const toExclusiveEpoch = Date.parse(range.dateToIso) + 1;
  if (!Number.isSafeInteger(toExclusiveEpoch)) {
    throw new TypeError("Daily close period end is invalid.");
  }
  return Object.freeze({
    businessDate,
    periodFromIso: range.dateFromIso,
    periodToIso: new Date(toExclusiveEpoch).toISOString(),
    storeCode: requiredText(input.storeCode, "store code"),
    deviceCode: requiredText(input.deviceCode, "device code"),
  });
}

export function businessDateInTimeZone(
  now: Date,
  businessTimeZone: string,
): string {
  if (!Number.isFinite(now.getTime())) {
    throw new TypeError("Daily close clock is invalid.");
  }
  const formatter = new Intl.DateTimeFormat("en-CA", {
    calendar: "gregory",
    day: "2-digit",
    month: "2-digit",
    numberingSystem: "latn",
    timeZone: requiredText(businessTimeZone, "business time zone"),
    year: "numeric",
  });
  const parts = new Map(
    formatter
      .formatToParts(now)
      .filter((part) =>
        ["year", "month", "day"].includes(part.type),
      )
      .map((part) => [part.type, part.value]),
  );
  return validateBusinessDate(
    `${parts.get("year") ?? ""}-${parts.get("month") ?? ""}-${parts.get("day") ?? ""}`,
  );
}

export function buildDailyCloseArchiveCommit(input: {
  auditEventId: string;
  closeId: string;
  counts: readonly Readonly<{
    denominationCents: number;
    quantity: number;
  }>[];
  savedAtIso: string;
  savedCashierId: string;
  savedCashierName: string;
  savedUserGuid: string | null;
  summary: DailyCloseSummary;
}): DailyCloseArchiveCommit {
  const closeId = requiredText(input.closeId, "daily close id");
  const savedAtIso = validateIso(input.savedAtIso, "saved at");
  const denominations = normalizeDailyCloseCounts(input.counts);
  const notesSubtotalCents = denominations
    .filter((entry) => entry.denominationCents >= 500)
    .reduce(
      (sum, entry) => safeAdd(sum, entry.subtotalCents, "notes subtotal"),
      0,
    );
  const coinsSubtotalCents = denominations
    .filter((entry) => entry.denominationCents < 500)
    .reduce(
      (sum, entry) => safeAdd(sum, entry.subtotalCents, "coins subtotal"),
      0,
    );
  const countedCashCents = safeAdd(
    notesSubtotalCents,
    coinsSubtotalCents,
    "counted cash",
  );
  const varianceCents = safeAdd(
    countedCashCents,
    -assertSafeInteger(input.summary.expectedCashCents, "expected cash"),
    "cash variance",
  );
  const archive = Object.freeze({
    ...input.summary,
    closeId,
    savedCashierId: requiredText(input.savedCashierId, "cashier id"),
    savedCashierName: requiredText(input.savedCashierName, "cashier name"),
    savedAtIso,
    denominations,
    notesSubtotalCents,
    coinsSubtotalCents,
    countedCashCents,
    varianceCents,
  });
  return Object.freeze({
    archive,
    audit: Object.freeze({
      eventId: requiredText(input.auditEventId, "audit event id"),
      eventType: "DAILY_CLOSE_SAVE",
      occurredAtIso: savedAtIso,
      orderGuid: null,
      correlationId: closeId,
      payload: Object.freeze({
        action: "daily-close-save",
        businessDate: archive.businessDate,
        closeId,
        countedCashCents,
        deviceCode: archive.deviceCode,
        storeCode: archive.storeCode,
        varianceCents,
        ...auditActorPayload({
          cashierId: archive.savedCashierId,
          cashierName: archive.savedCashierName,
          userGuid: input.savedUserGuid,
        }),
      }),
    }),
  });
}

function validateBusinessDate(value: string): string {
  const normalized = value.trim();
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(normalized);
  if (!match) throw new TypeError("Daily close business date is invalid.");
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const date = new Date(0);
  date.setUTCFullYear(year, month - 1, day);
  date.setUTCHours(0, 0, 0, 0);
  if (
    date.getUTCFullYear() !== year ||
    date.getUTCMonth() !== month - 1 ||
    date.getUTCDate() !== day
  ) {
    throw new TypeError("Daily close business date is invalid.");
  }
  return normalized;
}

function validateIso(value: string, label: string): string {
  const normalized = requiredText(value, label);
  if (!Number.isFinite(Date.parse(normalized))) {
    throw new TypeError(`Daily close ${label} is invalid.`);
  }
  return normalized;
}

function requiredText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized || normalized.length > 256 || /[\u0000-\u001f\u007f]/.test(normalized)) {
    throw new TypeError(`Daily close ${label} is invalid.`);
  }
  return normalized;
}

function assertSafeInteger(value: number, label: string): number {
  if (!Number.isSafeInteger(value)) {
    throw new TypeError(`Daily close ${label} is invalid.`);
  }
  return value;
}

function safeAdd(left: number, right: number, label: string): number {
  return assertSafeInteger(left + right, label);
}
