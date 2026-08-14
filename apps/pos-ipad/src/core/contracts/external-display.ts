import { z } from "zod";

import { MoneySchema } from "./money";

export type DisplayStatus = "disconnected" | "connecting" | "ready" | "failed";
export const CUSTOMER_DISPLAY_VISIBLE_ITEM_LIMIT = 12;

const CustomerDisplayQuantitySchema = z
  .string()
  .regex(/^-?\d+(?:\.\d{1,3})?$/);

const CustomerDisplayItemSchema = z
  .object({
    name: z.string().min(1).max(160),
    quantity: CustomerDisplayQuantitySchema,
    unitPrice: MoneySchema.optional(),
    amount: MoneySchema,
  })
  .strict();

const CustomerDisplaySummarySchema = z
  .object({
    itemQuantity: CustomerDisplayQuantitySchema,
    skuCount: z.number().int().nonnegative().max(Number.MAX_SAFE_INTEGER),
    subtotal: MoneySchema,
  })
  .strict();

const CustomerDisplayAdvertSchema = z
  .object({
    kind: z.enum(["image", "video"]),
    localUri: z.string().min(1),
  })
  .strict();

const CustomerDisplaySnapshotBaseSchema = z
  .object({
    revision: z.number().int().nonnegative(),
    mode: z.enum(["idle", "cart", "payment", "change", "success"]),
    items: z.array(CustomerDisplayItemSchema).max(100),
    summary: CustomerDisplaySummarySchema.optional(),
    visibleItemStart: z.number().int().nonnegative().optional(),
    gst: MoneySchema,
    discount: MoneySchema,
    total: MoneySchema,
    change: MoneySchema,
    advert: CustomerDisplayAdvertSchema.nullable(),
  })
  .strict();

export const CustomerDisplaySnapshotSchema =
  CustomerDisplaySnapshotBaseSchema.superRefine((snapshot, context) => {
    if (snapshot.visibleItemStart === undefined) return;
    const maximumStart = Math.max(
      0,
      snapshot.items.length - CUSTOMER_DISPLAY_VISIBLE_ITEM_LIMIT,
    );
    if (snapshot.visibleItemStart > maximumStart) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        path: ["visibleItemStart"],
        message: "Visible customer display item window is out of range.",
      });
    }
  });

export type CustomerDisplaySnapshot = Readonly<
  z.infer<typeof CustomerDisplaySnapshotSchema>
>;

export interface ExternalCustomerDisplayPort {
  getStatus(): Promise<DisplayStatus>;
  setEnabled(enabled: boolean): Promise<void>;
  publish(snapshot: CustomerDisplaySnapshot): Promise<void>;
  subscribe(listener: (status: DisplayStatus) => void): () => void;
}
