import { z } from "zod";
import { unwrapApiEnvelope } from "@/shared/api/api-envelope";
import { POS_OPERATION_LOG_PAGE_SIZE } from "./logic";
import type {
  PosOperationLogDetail,
  PosOperationLogPage,
  PosOperationLogSummary,
} from "./types";

const text = z.string();
// 后端 WhenWritingNull 会省略空字段，统一归一为 null 便于界面判空。
const nullableText = text.nullish().transform((value) => (value?.trim() ? value : null));
const nullableNumber = z.number().finite().nullish().transform((value) => value ?? null);
const flag = z.boolean().nullish().transform((value) => value === true);

const outcomeSchema = z
  .string()
  .transform((value) => {
    const normalized = value.trim().toLowerCase();
    if (normalized === "denied") return "Denied" as const;
    if (normalized === "failed") return "Failed" as const;
    return "Succeeded" as const;
  });

const listItemSchema = z.object({
  eventId: text.min(1),
  occurredAtUtc: text,
  receivedAtUtc: text,
  operationType: text,
  outcome: outcomeSchema,
  cashierId: nullableText,
  userGuid: nullableText,
  cashierName: nullableText,
  isOfflineCached: flag,
  isEmergencyOverride: flag,
  storeCode: text,
  deviceCode: text,
  deviceSystem: nullableText,
  appVersion: nullableText,
  orderGuid: nullableText,
  receiptNumber: nullableText,
  correlationId: nullableText,
  traceId: nullableText,
  paymentMethod: nullableText,
  reasonCode: nullableText,
  safeMessage: nullableText,
  currencyCode: text.nullish().transform((value) => value || "AUD"),
  paymentAmount: nullableNumber,
  beforeGross: nullableNumber,
  afterGross: nullableNumber,
  beforeDiscount: nullableNumber,
  afterDiscount: nullableNumber,
  beforeActual: nullableNumber,
  afterActual: nullableNumber,
  amountDelta: nullableNumber,
  productCount: z.number().int().nullish().transform((value) => value ?? 0),
  primaryProduct: nullableText,
});

const detailItemSchema = z.object({
  lineIndex: z.number().int(),
  productCode: nullableText,
  itemNumber: nullableText,
  referenceCode: nullableText,
  lookupCode: nullableText,
  displayName: nullableText,
  lineKind: nullableText,
  beforeQuantity: nullableNumber,
  afterQuantity: nullableNumber,
  quantityDelta: nullableNumber,
  beforeUnitPrice: nullableNumber,
  afterUnitPrice: nullableNumber,
  unitPriceDelta: nullableNumber,
  beforeDiscountAmount: nullableNumber,
  afterDiscountAmount: nullableNumber,
  discountAmountDelta: nullableNumber,
  beforeGrossAmount: nullableNumber,
  afterGrossAmount: nullableNumber,
  grossAmountDelta: nullableNumber,
  beforeActualAmount: nullableNumber,
  afterActualAmount: nullableNumber,
  actualAmountDelta: nullableNumber,
});

const detailSchema = listItemSchema.extend({
  propertiesJson: nullableText,
  items: z.array(detailItemSchema).nullish().transform((value) => value ?? []),
});

const pageSchema = z.object({
  items: z.array(listItemSchema).nullish().transform((value) => value ?? []),
  total: z.number().int().nullish().transform((value) => value ?? 0),
  pageNumber: z.number().int().nullish().transform((value) => value ?? 1),
  pageSize: z.number().int().nullish().transform((value) => value ?? POS_OPERATION_LOG_PAGE_SIZE),
});

const summarySchema = z.object({
  total: z.number().int().nullish().transform((value) => value ?? 0),
  succeeded: z.number().int().nullish().transform((value) => value ?? 0),
  denied: z.number().int().nullish().transform((value) => value ?? 0),
  failed: z.number().int().nullish().transform((value) => value ?? 0),
  emergencyOverride: z.number().int().nullish().transform((value) => value ?? 0),
  offlineCached: z.number().int().nullish().transform((value) => value ?? 0),
});

export function normalizePosOperationLogPage(payload: unknown): PosOperationLogPage {
  return pageSchema.parse(unwrapApiEnvelope(payload));
}

export function normalizePosOperationLogSummary(payload: unknown): PosOperationLogSummary {
  return summarySchema.parse(unwrapApiEnvelope(payload));
}

export function normalizePosOperationLogDetail(payload: unknown): PosOperationLogDetail {
  const detail = detailSchema.parse(unwrapApiEnvelope(payload));
  return { ...detail, items: [...detail.items].sort((a, b) => a.lineIndex - b.lineIndex) };
}
