import type { ScanFeedbackState } from "@/modules/scanner/types";

export type OrderNoticeTone = "success" | "info" | "warning" | "error" | "delisted";

/** 底部操作栏里闪现的反馈；位置固定，不会推动列表。 */
export interface OrderNotice {
  tone: OrderNoticeTone;
  title: string;
  detail?: string;
}

export const ORDER_NOTICE_DURATION_MS = 2500;

type Translate = (key: string, options?: Record<string, unknown>) => string;

/**
 * 把扫码 Hook 的反馈映射成操作栏提示。
 * ready / scanning / found / multiple 由页面其它区域承接，这里不闪现。
 */
export function mapScanFeedbackToNotice(feedback: ScanFeedbackState, t: Translate): OrderNotice | null {
  switch (feedback.status) {
    case "ready":
    case "scanning":
    case "found":
    case "multiple":
      return null;
    case "added":
      return {
        tone: "success",
        title: feedback.productName
          ? t("common:orderNotice.addedNamed", { name: feedback.productName })
          : feedback.message,
        detail: feedback.addedQuantity
          ? t("common:orderNotice.addedQuantity", { quantity: feedback.addedQuantity })
          : undefined,
      };
    case "delisted":
      return {
        tone: "delisted",
        title: t("common:orderNotice.delistedNamed", {
          name: feedback.productName || feedback.itemNumber || feedback.barcode || "",
        }),
        detail: t("common:orderNotice.delistedDetail", {
          itemNumber: feedback.itemNumber || feedback.barcode || "",
        }),
      };
    case "not_found":
      return { tone: "warning", title: feedback.message, detail: feedback.barcode };
    case "blocked":
      return { tone: "warning", title: feedback.message };
    case "price_update_required":
      return { tone: "info", title: feedback.message, detail: feedback.productName };
    case "error":
      return { tone: "error", title: feedback.message, detail: feedback.barcode };
    default:
      return null;
  }
}
