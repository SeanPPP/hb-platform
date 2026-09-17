import { z } from "zod";
import { unwrapApiEnvelope } from "../../shared/api/api-envelope";
import { validateWarehouseInsightRange } from "./logic";
import type { WarehouseProductInsight } from "./types";

const number = z.number().finite();
const text = z.string();
// 后端统一使用 WhenWritingNull，合法的空元数据会省略字段。
const nullableText = text.nullish().transform((value) => value ?? null);
const nullableNumber = number.nullish().transform((value) => value ?? null);
const movement = z.object({
  documentNo: text,
  storeCode: text,
  storeName: text,
  date: text,
  quantity: number,
});

const insightSchema = z.object({
  range: z
    .object({ startDate: text, endDate: text, dayCount: z.number().int() })
    .refine((value) => validateWarehouseInsightRange(value).ok),
  inboundRange: z.object({
    startDate: text,
    endDate: text,
    dayCount: z.number().int(),
  }),
  generatedAt: text,
  salesStatisticLastUpdatedAt: nullableText,
  scope: z.enum(["all-stores", "authorized-stores"]),
  product: z.object({
    productCode: text.min(1),
    productName: text,
    itemNumber: nullableText,
    barcode: nullableText,
    productImage: nullableText,
    supplierCode: nullableText,
    supplierName: nullableText,
    locationCode: nullableText,
    stockQuantity: nullableNumber,
  }),
  totals: z.object({
    inboundQuantity: number,
    containerCount: z.number().int().nonnegative(),
    inTransitQuantity: number,
    inTransitContainerCount: z.number().int().nonnegative(),
    orderedQuantity: number,
    orderedStoreCount: z.number().int().nonnegative(),
    orderDocumentCount: z.number().int().nonnegative(),
    shippedQuantity: number,
    shippedStoreCount: z.number().int().nonnegative(),
    shipmentDocumentCount: z.number().int().nonnegative(),
    pendingQuantity: number,
    pendingStoreCount: z.number().int().nonnegative(),
    salesQuantity: number,
    salesAmount: number,
    salesStoreCount: z.number().int().nonnegative(),
  }),
  branches: z.array(
    z.object({
      storeCode: text.min(1),
      storeName: text,
      orderedQuantity: number,
      shippedQuantity: number,
      pendingQuantity: number,
      salesQuantity: number,
      salesAmount: number,
      sellThroughRate: nullableNumber,
    }),
  ),
  containers: z.array(
    z.object({
      containerNumber: text,
      arrivalDate: text,
      isEstimatedArrival: z.boolean(),
      quantity: number,
      pieces: number,
      status: text,
    }),
  ),
  orders: z.array(movement),
  shipments: z.array(movement),
  dailySales: z.array(
    z.object({ date: text, quantity: number, amount: number }),
  ),
});

export function normalizeWarehouseProductInsight(
  payload: unknown,
): WarehouseProductInsight {
  const result = insightSchema.safeParse(unwrapApiEnvelope(payload));
  if (!result.success) {
    // 缺字段或无效数值属于接口失败，不能静默补零伪装成没有进销记录。
    throw Object.assign(new Error("Invalid warehouse product insight response"), {
      code: "WAREHOUSE_INSIGHT_INVALID_RESPONSE",
    });
  }
  const data = result.data;
  const branchStoreCodes = new Set(
    data.branches.map((row) => row.storeCode.toLowerCase()),
  );
  if (branchStoreCodes.size !== data.branches.length) {
    throw Object.assign(new Error("Duplicated warehouse insight branch rows"), {
      code: "WAREHOUSE_INSIGHT_INVALID_RESPONSE",
    });
  }
  return data;
}
