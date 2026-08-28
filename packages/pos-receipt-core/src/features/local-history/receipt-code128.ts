import { receiptCode128 } from "../receipts/receipt-code128";

export type { ReceiptCode128Run } from "../receipts/receipt-code128";

export function receiptCode128Runs(value: string) {
  return receiptCode128(value).runs;
}
