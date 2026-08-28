import type { EscPosDocument } from "@hb/pos-receipt-core/features/receipts/receipt-document";
import {
  renderLocalOrderReceiptDocument,
  requiresCashSettlementAudit,
  type FrozenReceiptDocumentSettings,
  type ReceiptCompletionSettlementSource,
  type ReceiptReprintOrderSource,
} from "@hb/pos-receipt-core/features/receipts/receipt-reprint-service";

import type { LocalHistoryReceiptPreviewPort } from "@/features/local-history/local-history-domain";

export interface ReceiptPreviewSettingsSource {
  getFrozenReceiptPreviewSettings(): Promise<FrozenReceiptDocumentSettings | null>;
}

export type LocalHistoryReceiptPreviewServiceOptions = Readonly<{
  orders: Pick<ReceiptReprintOrderSource, "getByOrderGuid">;
  settings: ReceiptPreviewSettingsSource;
  settlements: ReceiptCompletionSettlementSource;
}>;

/**
 * 本机历史页的安全边界：原始订单和支付引用止于此服务，UI 只能取得已脱敏文档。
 */
export class LocalHistoryReceiptPreviewService
implements LocalHistoryReceiptPreviewPort {
  public constructor(
    private readonly options: LocalHistoryReceiptPreviewServiceOptions,
  ) {}

  public async getPreview(orderGuid: string): Promise<EscPosDocument | null> {
    try {
      const order = await this.options.orders.getByOrderGuid(orderGuid);
      if (!order || order.orderGuid !== orderGuid) return null;

      const settings = await this.options.settings.getFrozenReceiptPreviewSettings();
      if (!settings) return null;

      let cashChangeCents: number | null = null;
      if (requiresCashSettlementAudit(order)) {
        const settlement = await this.options.settlements
          .getCompletionSettlement(order.orderGuid);
        if (
          !settlement
          || !Number.isSafeInteger(settlement.cashChangeCents)
          || settlement.cashChangeCents < 0
        ) {
          // 中文注释：缺少完成审计时不能从订单金额反推或默认零找零。
          return null;
        }
        cashChangeCents = settlement.cashChangeCents;
      }

      return renderLocalOrderReceiptDocument(
        order,
        settings,
        cashChangeCents,
        {
          isReprint: true,
          // 预览是稳定的订单快照，不能每次打开都显示新的设备时间。
          printedAtIso: order.soldAtIso,
        },
      );
    } catch {
      return null;
    }
  }
}
