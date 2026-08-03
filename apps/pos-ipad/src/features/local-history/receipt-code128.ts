import { receiptCode128 } from "@/features/receipts/receipt-code128";

export type { ReceiptCode128Run } from "@/features/receipts/receipt-code128";

export function receiptCode128Runs(value: string) {
  return receiptCode128(value).runs;
}
