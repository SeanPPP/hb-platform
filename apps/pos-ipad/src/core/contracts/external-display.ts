import { z } from "zod";

import { MoneySchema } from "./money";

export type DisplayStatus = "disconnected" | "connecting" | "ready" | "failed";

const CustomerDisplayItemSchema = z
  .object({
    name: z.string().min(1).max(160),
    quantity: z.string().regex(/^-?\d+(?:\.\d{1,3})?$/),
    amount: MoneySchema,
  })
  .strict();

const CustomerDisplayAdvertSchema = z
  .object({
    kind: z.enum(["image", "video"]),
    localUri: z.string().min(1),
  })
  .strict();

export const CustomerDisplaySnapshotSchema = z
  .object({
    revision: z.number().int().nonnegative(),
    mode: z.enum(["idle", "cart", "payment", "change", "success"]),
    items: z.array(CustomerDisplayItemSchema).max(100),
    gst: MoneySchema,
    discount: MoneySchema,
    total: MoneySchema,
    change: MoneySchema,
    advert: CustomerDisplayAdvertSchema.nullable(),
  })
  .strict();

export type CustomerDisplaySnapshot = Readonly<
  z.infer<typeof CustomerDisplaySnapshotSchema>
>;

export interface ExternalCustomerDisplayPort {
  getStatus(): Promise<DisplayStatus>;
  setEnabled(enabled: boolean): Promise<void>;
  publish(snapshot: CustomerDisplaySnapshot): Promise<void>;
  subscribe(listener: (status: DisplayStatus) => void): () => void;
}
