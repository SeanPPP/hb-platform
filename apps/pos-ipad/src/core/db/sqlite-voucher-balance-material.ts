import type { SqliteConnectionPort } from "./types";

import type {
  VoucherLatestBalanceConfirmation,
  VoucherProtectedAttemptState,
  VoucherProtectedTokenPort,
} from "@/features/payments/voucher";
import type {
  VoucherBalanceMaterial,
  VoucherBalanceMaterialPort,
} from "@/features/receipts/voucher-balance-receipt";

type VoucherBalanceBindingRow = Readonly<{
  order_guid: unknown;
  order_state: unknown;
  order_store_code: unknown;
  tender_guid: unknown;
  tender_amount_cents: unknown;
  attempt_id: unknown;
  idempotency_key: unknown;
  attempt_order_guid: unknown;
  provider: unknown;
  operation: unknown;
  attempt_amount_cents: unknown;
  attempt_state: unknown;
  protected_reference: unknown;
  protected_attempt_id: unknown;
  protected_idempotency_key: unknown;
  protected_order_guid: unknown;
}>;

/**
 * 公开账本只提供 attempt/order 关系；完整券码与最新余额始终从二次密文读取。
 * 查询明确排除退款、负 tender、已撤回 tender 和非 Approved attempt。
 */
export class SqliteVoucherBalanceMaterialStore
implements VoucherBalanceMaterialPort {
  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly protectedTokens: Pick<
      VoucherProtectedTokenPort,
      "getByAttempt" | "save"
    >,
  ) {}

  public listForOrder(
    orderGuidInput: string,
  ): Promise<readonly VoucherBalanceMaterial[]> {
    const orderGuid = exactText(orderGuidInput, 128);
    if (!orderGuid) return Promise.resolve([]);
    return this.readMaterials(
      `o.order_guid = ?
       AND o.state IN ('PendingSync', 'Syncing', 'Synced')`,
      [orderGuid],
      32,
    );
  }

  public async listSyncedPendingPrints(
    limitInput = 200,
  ): Promise<readonly VoucherBalanceMaterial[]> {
    const limit = positiveLimit(limitInput);
    const result: VoucherBalanceMaterial[] = [];
    const scanSize = 200;
    let offset = 0;
    while (result.length < limit) {
      const page = await this.readMaterials(
        `o.state = 'Synced'
         AND NOT EXISTS (
           SELECT 1
           FROM print_jobs job
           WHERE job.job_id = 'voucher-balance:' || attempt.attempt_id
         )`,
        [],
        scanSize,
        offset,
      );
      result.push(
        ...page.filter(
          (material) =>
            material.confirmation?.status === "confirmed" &&
            material.confirmation.remainingCents > 0,
        ),
      );
      if (page.length < scanSize) break;
      offset += page.length;
    }
    return Object.freeze(result.slice(0, limit));
  }

  public async saveConfirmation(
    attemptIdInput: string,
    confirmation: VoucherLatestBalanceConfirmation,
  ): Promise<void> {
    const attemptId = exactText(attemptIdInput, 128);
    if (!attemptId) {
      throw new Error("Voucher balance attempt id is invalid.");
    }
    const state = await this.protectedTokens.getByAttempt(attemptId);
    if (
      !state ||
      state.attemptId !== attemptId ||
      state.operation !== "purchase" ||
      state.phase !== "approved" ||
      !state.voucherCode ||
      !state.reservationToken
    ) {
      throw new Error("Voucher balance protected state is invalid.");
    }
    if (state.latestBalanceConfirmation) {
      if (
        JSON.stringify(state.latestBalanceConfirmation) ===
        JSON.stringify(confirmation)
      ) {
        return;
      }
      throw new Error(
        "Voucher latest balance confirmation cannot be changed.",
      );
    }
    const { protectedReference: _protectedReference, ...draft } = state;
    await this.protectedTokens.save({
      ...draft,
      latestBalanceConfirmation: confirmation,
    });
  }

  private async readMaterials(
    condition: string,
    parameters: readonly string[],
    limit: number,
    offset = 0,
  ): Promise<readonly VoucherBalanceMaterial[]> {
    const rows = await this.connection.getAll<VoucherBalanceBindingRow>(
      `SELECT
        o.order_guid,
        o.state AS order_state,
        o.store_code AS order_store_code,
        tender.tender_guid,
        tender.amount_cents AS tender_amount_cents,
        attempt.attempt_id,
        attempt.idempotency_key,
        attempt.order_guid AS attempt_order_guid,
        attempt.provider,
        attempt.operation,
        attempt.amount_cents AS attempt_amount_cents,
        attempt.state AS attempt_state,
        protected.protected_reference,
        protected.attempt_id AS protected_attempt_id,
        protected.idempotency_key AS protected_idempotency_key,
        protected.order_guid AS protected_order_guid
       FROM local_orders o
       INNER JOIN order_tenders tender
         ON tender.order_guid = o.order_guid
        AND tender.method = 'voucher'
        AND tender.amount_cents > 0
       INNER JOIN payment_attempts attempt
         ON attempt.attempt_id = tender.payment_attempt_id
        AND attempt.order_guid = o.order_guid
        AND attempt.provider = 'voucher'
        AND attempt.operation = 'purchase'
        AND attempt.amount_cents = tender.amount_cents
        AND attempt.state = 'Approved'
       INNER JOIN voucher_protected_attempt_states protected
         ON protected.attempt_id = attempt.attempt_id
        AND protected.idempotency_key = attempt.idempotency_key
        AND protected.order_guid = attempt.order_guid
       WHERE ${condition}
         AND o.original_order_guid IS NULL
         AND o.total_cents > 0
         AND o.actual_amount_cents > 0
         AND NOT EXISTS (
           SELECT 1
           FROM local_order_lines return_line
           WHERE return_line.order_guid = o.order_guid
             AND return_line.line_kind <> 'sale'
         )
         AND NOT EXISTS (
           SELECT 1
           FROM payment_tender_reversal_links link
           WHERE link.order_guid = o.order_guid
             AND link.source_tender_guid = tender.tender_guid
         )
         AND NOT EXISTS (
           SELECT 1
           FROM voucher_tender_reversal_actions reversal
           WHERE reversal.order_guid = o.order_guid
             AND reversal.source_tender_guid = tender.tender_guid
         )
       ORDER BY o.local_sequence, attempt.attempt_id
       LIMIT ? OFFSET ?`,
      [...parameters, limit, offset],
    );
    const materials: VoucherBalanceMaterial[] = [];
    for (const row of rows) {
      const attemptId = exactText(row.attempt_id, 128);
      const orderGuid = exactText(row.order_guid, 128);
      const storeCode = exactText(row.order_store_code, 64);
      const idempotencyKey = exactText(row.idempotency_key, 256);
      const protectedReference =
        exactText(row.protected_reference, 128);
      const amountCents = safeInteger(row.tender_amount_cents);
      if (
        !attemptId ||
        !orderGuid ||
        !storeCode ||
        !idempotencyKey ||
        !protectedReference ||
        exactText(row.tender_guid, 128) === null ||
        exactText(row.attempt_order_guid, 128) !== orderGuid ||
        row.provider !== "voucher" ||
        row.operation !== "purchase" ||
        row.attempt_state !== "Approved" ||
        amountCents === null ||
        amountCents <= 0 ||
        safeInteger(row.attempt_amount_cents) !== amountCents ||
        exactText(row.protected_attempt_id, 128) !== attemptId ||
        exactText(row.protected_idempotency_key, 256) !==
          idempotencyKey ||
        exactText(row.protected_order_guid, 128) !== orderGuid
      ) {
        throw new Error("Voucher balance public binding is invalid.");
      }
      const state =
        await this.protectedTokens.getByAttempt(attemptId);
      materials.push(
        materialFromProtectedState(
          state,
          protectedReference,
          orderGuid,
          storeCode,
          amountCents,
        ),
      );
    }
    return Object.freeze(materials);
  }
}

function materialFromProtectedState(
  state: VoucherProtectedAttemptState | null,
  protectedReference: string,
  orderGuid: string,
  storeCode: string,
  amountCents: number,
): VoucherBalanceMaterial {
  if (
    !state ||
    state.protectedReference !== protectedReference ||
    state.orderGuid !== orderGuid ||
    state.storeCode !== storeCode ||
    state.operation !== "purchase" ||
    state.phase !== "approved" ||
    state.amountCents !== amountCents ||
    !state.voucherCode ||
    !state.reservationToken
  ) {
    throw new Error("Voucher balance protected binding is invalid.");
  }
  return Object.freeze({
    attemptId: state.attemptId,
    orderGuid,
    storeCode,
    voucherCode: state.voucherCode,
    confirmation: state.latestBalanceConfirmation ?? null,
  });
}

function exactText(value: unknown, max: number): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return normalized &&
    normalized === value &&
    normalized.length <= max &&
    !/[\u0000-\u001f\u007f]/u.test(normalized)
    ? normalized
    : null;
}

function safeInteger(value: unknown): number | null {
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) ? parsed : null;
}

function positiveLimit(value: number): number {
  return Number.isSafeInteger(value) && value > 0 && value <= 200
    ? value
    : 200;
}
