import type { SqliteConnectionPort } from "./types";

export type StoredReceiptCompletionSettlement = Readonly<{
  cashChangeCents: number;
}>;

type CompletionAuditRow = Readonly<{
  event_type: unknown;
  correlation_id: unknown;
  payload_json: unknown;
}>;

type TenderProofRow = Readonly<{
  tender_guid: unknown;
  method: unknown;
  amount_cents: unknown;
  payment_attempt_id: unknown;
  reversal_count: unknown;
}>;

/**
 * 重打现金小票所需的找零只允许来自完成事务已持久化的审计。
 *
 * 同一订单缺失或出现多份完成审计都视为账本证据不唯一，调用方必须拒绝重打，
 * 不能从 tender、当前购物车或设备时间推算。
 */
export class ReceiptCompletionSettlementRepository {
  public constructor(private readonly connection: SqliteConnectionPort) {}

  public async getByOrderGuid(
    orderGuid: string,
  ): Promise<StoredReceiptCompletionSettlement | null> {
    if (typeof orderGuid !== "string" || !orderGuid.trim()) return null;

    const completionRows = await this.connection.getAll<CompletionAuditRow>(
      `SELECT event_type, correlation_id, payload_json
       FROM audit_events
       WHERE order_guid = ?
         AND event_type IN ('SALE_COMPLETE', 'RETURN_REFUND_COMPLETE')
       ORDER BY occurred_at_iso DESC, event_id DESC
       LIMIT 2`,
      [orderGuid],
    );
    if (completionRows.length > 1) return null;
    if (completionRows.length === 1) {
      return completionSettlement(completionRows[0]);
    }

    // 旧版混合支付完成链路没有 SALE_COMPLETE；必须同时证明最终完成、全部现金入账和未撤销 tender。
    const mixedCompletionRows = await this.connection.getAll<CompletionAuditRow>(
      `SELECT event_type, correlation_id, payload_json
       FROM audit_events
       WHERE order_guid = ?
         AND event_type IN ('PAYMENT_MIXED_CASH_COMPLETE', 'PAYMENT_APPROVED_COMPLETE')
       ORDER BY occurred_at_iso DESC, event_id DESC
       LIMIT 2`,
      [orderGuid],
    );
    if (mixedCompletionRows.length !== 1) return null;
    const completion = mixedCompletionRows[0];
    if (!completion) return null;
    const correlationId = strictText(completion?.correlation_id);
    if (!correlationId) return null;

    const mixedCashRows = await this.connection.getAll<CompletionAuditRow>(
      `SELECT event_type, correlation_id, payload_json
       FROM audit_events
       WHERE order_guid = ?
         AND event_type = 'MIXED_CASH_TENDER_APPENDED'
       ORDER BY occurred_at_iso ASC, event_id ASC`,
      [orderGuid],
    );

    const cashTenderRows = await this.connection.getAll<TenderProofRow>(
      `SELECT
         tender_guid,
         method,
         amount_cents,
         payment_attempt_id,
         (SELECT COUNT(*)
          FROM payment_tender_reversal_links reversal
          WHERE reversal.order_guid = order_tenders.order_guid
            AND reversal.source_tender_guid = order_tenders.tender_guid) AS reversal_count
       FROM order_tenders
       WHERE order_guid = ?
         AND method = 'cash'
         AND amount_cents > 0
         AND NOT EXISTS (
           SELECT 1
           FROM payment_tender_reversal_links reversal
           WHERE reversal.order_guid = order_tenders.order_guid
             AND reversal.source_tender_guid = order_tenders.tender_guid
         )
       ORDER BY created_at_iso ASC, tender_guid ASC`,
      [orderGuid],
    );
    if (cashTenderRows.length === 0) return null;

    const cashProofs: MixedCashAppendProof[] = [];
    for (const tender of cashTenderRows) {
      const tenderGuid = strictText(tender.tender_guid);
      if (
        !tenderGuid ||
        tender.method !== "cash" ||
        !isPositiveCents(tender.amount_cents) ||
        tender.payment_attempt_id !== null ||
        tender.reversal_count !== 0
      ) {
        return null;
      }
      const matchingRows = mixedCashRows.filter((row) =>
        auditPayload(row.payload_json)?.tenderGuid === tenderGuid
      );
      if (matchingRows.length !== 1) return null;
      const proof = mixedCashAppendProof(matchingRows[0]);
      if (!proof || proof.appliedCents !== tender.amount_cents) return null;
      cashProofs.push(proof);
    }

    if (completion.event_type === "PAYMENT_MIXED_CASH_COMPLETE") {
      const payload = auditPayload(completion.payload_json);
      const amountCents = payload?.amountCents;
      const finalProofs = cashProofs.filter(
        (proof) =>
          proof.correlationId === correlationId &&
          proof.appliedCents === amountCents,
      );
      const finalProof = finalProofs.length === 1 ? finalProofs[0] : undefined;
      if (
        payload?.method !== "cash" ||
        !isPositiveCents(amountCents) ||
        !finalProof ||
        !isFinalCashAppendProof(finalProof) ||
        cashProofs.some(
          (proof) => proof !== finalProof && !isExactCashAppendProof(proof),
        )
      ) {
        return null;
      }
    } else if (completion.event_type === "PAYMENT_APPROVED_COMPLETE") {
      const payload = auditPayload(completion.payload_json);
      const method = payload?.method;
      const amountCents = payload?.amountCents;
      if (
        cashProofs.some((proof) => !isExactCashAppendProof(proof)) ||
        (method !== "card" && method !== "voucher") ||
        payload?.attemptId !== correlationId ||
        !isPositiveCents(amountCents)
      ) {
        return null;
      }
      const approvedTenderRows = await this.connection.getAll<TenderProofRow>(
        `SELECT
           tender_guid,
           method,
           amount_cents,
           payment_attempt_id,
           (SELECT COUNT(*)
            FROM payment_tender_reversal_links reversal
            WHERE reversal.order_guid = order_tenders.order_guid
              AND reversal.source_tender_guid = order_tenders.tender_guid) AS reversal_count
         FROM order_tenders
         WHERE order_guid = ?
           AND payment_attempt_id = ?
         LIMIT 2`,
        [orderGuid, correlationId],
      );
      const approvedTender = approvedTenderRows.length === 1
        ? approvedTenderRows[0]
        : undefined;
      if (
        !approvedTender ||
        !strictText(approvedTender.tender_guid) ||
        approvedTender.method !== method ||
        approvedTender.amount_cents !== amountCents ||
        approvedTender.payment_attempt_id !== correlationId ||
        approvedTender.reversal_count !== 0
      ) {
        return null;
      }
    } else {
      return null;
    }

    const cashChangeCents = cashProofs.reduce(
      (total, proof) => total + proof.cashChangeCents,
      0,
    );
    return Number.isSafeInteger(cashChangeCents)
      ? { cashChangeCents }
      : null;
  }
}

function completionSettlement(
  row: CompletionAuditRow | undefined,
): StoredReceiptCompletionSettlement | null {
  if (
    !row ||
    (row.event_type !== "SALE_COMPLETE" &&
      row.event_type !== "RETURN_REFUND_COMPLETE")
  ) {
    return null;
  }
  const payload = auditPayload(row.payload_json);
  const changeCents = payload?.changeCents;
  return isNonNegativeCents(changeCents) ? { cashChangeCents: changeCents } : null;
}

type MixedCashAppendProof = Readonly<{
  cashChangeCents: number;
  appliedCents: number;
  tenderedCents: number;
  tenderGuid: string;
  correlationId: string;
}>;

function mixedCashAppendProof(
  row: CompletionAuditRow | undefined,
): MixedCashAppendProof | null {
  if (
    !row ||
    row.event_type !== "MIXED_CASH_TENDER_APPENDED"
  ) {
    return null;
  }
  const payload = auditPayload(row.payload_json);
  const appliedCents = payload?.appliedCents;
  const changeCents = payload?.changeCents;
  const tenderedCents = payload?.tenderedCents;
  const tenderGuid = strictText(payload?.tenderGuid);
  const correlationId = strictText(row.correlation_id);
  if (
    !isPositiveCents(appliedCents) ||
    !isNonNegativeCents(changeCents) ||
    !isNonNegativeCents(tenderedCents) ||
    !tenderGuid ||
    !correlationId
  ) {
    return null;
  }
  return {
    cashChangeCents: changeCents,
    appliedCents,
    tenderedCents,
    tenderGuid,
    correlationId,
  };
}

function isExactCashAppendProof(proof: MixedCashAppendProof): boolean {
  return proof.cashChangeCents === proof.tenderedCents - proof.appliedCents;
}

function isFinalCashAppendProof(proof: MixedCashAppendProof): boolean {
  if (isExactCashAppendProof(proof)) return true;

  // 最终现金镜像写入端按 AUD 五分舍入；旧版逐分实收仍由精确规则兼容。
  const cashDueCents = roundCashDueCents(proof.appliedCents);
  return (
    proof.tenderedCents >= cashDueCents &&
    proof.tenderedCents % 5 === 0 &&
    proof.cashChangeCents === proof.tenderedCents - cashDueCents
  );
}

function roundCashDueCents(amountCents: number): number {
  const remainder = amountCents % 5;
  return amountCents - remainder + (remainder >= 3 ? 5 : 0);
}

function auditPayload(
  value: unknown,
): Readonly<Record<string, unknown>> | null {
  if (typeof value !== "string") return null;
  try {
    const payload: unknown = JSON.parse(value);
    return payload && typeof payload === "object" && !Array.isArray(payload)
      ? (payload as Readonly<Record<string, unknown>>)
      : null;
  } catch {
    return null;
  }
}

function isNonNegativeCents(value: unknown): value is number {
  return (
    typeof value === "number" &&
    Number.isSafeInteger(value) &&
    value >= 0
  );
}

function isPositiveCents(value: unknown): value is number {
  return isNonNegativeCents(value) && value > 0;
}

function strictText(value: unknown): string | null {
  return typeof value === "string" && value.trim() === value && value.length > 0
    ? value
    : null;
}
