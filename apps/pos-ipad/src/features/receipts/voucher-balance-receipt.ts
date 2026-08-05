import {
  appendEscPosInitialize,
  encodeEscPosText,
} from "./esc-pos-text-encoding";
import type {
  FrozenReturnReceiptSettings,
  RenderedReturnReceipt,
  ReturnReceiptSettingsPort,
} from "./return-receipt-renderer";

import { HbposApiError } from "@/core/api";
import type { VoucherLatestBalanceConfirmation } from "@/features/payments/voucher";

export type { VoucherLatestBalanceConfirmation } from "@/features/payments/voucher";

export const VOUCHER_BALANCE_RECOVERY_BATCH_SIZE = 200;

export type VoucherBalanceMaterial = Readonly<{
  attemptId: string;
  orderGuid: string;
  storeCode: string;
  voucherCode: string;
  confirmation: VoucherLatestBalanceConfirmation | null;
}>;

export interface VoucherBalanceMaterialPort {
  listForOrder(orderGuid: string): Promise<readonly VoucherBalanceMaterial[]>;
  listSyncedPendingPrints(
    limit?: number,
  ): Promise<readonly VoucherBalanceMaterial[]>;
  saveConfirmation(
    attemptId: string,
    confirmation: VoucherLatestBalanceConfirmation,
  ): Promise<void>;
}

export interface VoucherLatestBalanceApiPort {
  query(
    storeCode: string,
    voucherCode: string,
  ): Promise<Readonly<{
    found?: boolean;
    voucher?: Readonly<{
      voucherCode?: string | null;
      storeCode?: string | null;
      status?: string | null;
      remainingAmount?: number;
    }>;
  }>>;
}

/**
 * 已完成的服务端核销可能让礼券余额归零，而现有查询端点只返回正余额礼券，
 * 因此此处只在 post-sync 查询语境把 404 解释为“无可打印余额”。
 */
export class PostSyncVoucherLatestBalanceApi
implements VoucherLatestBalanceApiPort {
  public constructor(
    private readonly api: VoucherLatestBalanceApiPort,
  ) {}

  public async query(
    storeCode: string,
    voucherCode: string,
  ): ReturnType<VoucherLatestBalanceApiPort["query"]> {
    try {
      return await this.api.query(storeCode, voucherCode);
    } catch (error) {
      if (
        error instanceof HbposApiError &&
        error.kind === "http" &&
        error.status === 404
      ) {
        return { found: false };
      }
      throw error;
    }
  }
}

export interface VoucherBalanceReceiptRendererPort {
  render(
    material: VoucherBalanceMaterial & Readonly<{
      confirmation: Extract<
        VoucherLatestBalanceConfirmation,
        { status: "confirmed" }
      >;
    }>,
  ): Promise<RenderedReturnReceipt | null>;
}

export interface VoucherBalancePrintQueuePort {
  hasPrintJob(jobId: string): Promise<boolean>;
  enqueuePrintJobOnce(input: Readonly<{
    jobId: string;
    orderGuid: string;
    printerId: string;
    receiptBytes: Uint8Array;
    isReprint: false;
  }>): Promise<"created" | "existing">;
}

/**
 * 订单只有在 Hbpos.Api 已确认同步后才进入本服务。网络查询失败直接抛回
 * order outbox；打印设置缺失只保留已确认快照，不得回滚已同步订单。
 */
export class VoucherBalancePostSyncService {
  public constructor(
    private readonly dependencies: Readonly<{
      api: VoucherLatestBalanceApiPort;
      materials: VoucherBalanceMaterialPort;
      renderer: VoucherBalanceReceiptRendererPort;
      printQueue: VoucherBalancePrintQueuePort;
      nowIso(): string;
      requestPrintDrain?: () => Promise<unknown>;
    }>,
  ) {}

  public async afterOrderAccepted(orderGuidInput: string): Promise<void> {
    const orderGuid = requiredText(orderGuidInput, 128);
    const before = await this.dependencies.materials.listForOrder(orderGuid);
    await this.confirmLatestBalances(groupMaterials(before));

    // 打印必须重新读取刚才写入的受保护快照，不能直接使用网络响应。
    const confirmed = await this.dependencies.materials.listForOrder(orderGuid);
    await this.materializePrintJobs(groupMaterials(confirmed));
  }

  public async recoverPendingPrints(): Promise<void> {
    while (true) {
      const pending =
        await this.dependencies.materials.listSyncedPendingPrints(
          VOUCHER_BALANCE_RECOVERY_BATCH_SIZE,
        );
      if (pending.length === 0) return;
      const created = await this.materializePrintJobs(
        groupMaterials(pending),
      );
      if (
        pending.length < VOUCHER_BALANCE_RECOVERY_BATCH_SIZE ||
        created === 0
      ) {
        return;
      }
    }
  }

  private async confirmLatestBalances(
    groups: readonly (readonly VoucherBalanceMaterial[])[],
  ): Promise<void> {
    for (const group of groups) {
      if (group.some((entry) => entry.confirmation !== null)) continue;
      const canonical = canonicalMaterial(group);
      const queried = await this.dependencies.api.query(
        canonical.storeCode,
        canonical.voucherCode,
      );
      const confirmation = confirmationFromQuery(
        queried,
        canonical,
        this.dependencies.nowIso(),
      );
      await this.dependencies.materials.saveConfirmation(
        canonical.attemptId,
        confirmation,
      );
    }
  }

  private async materializePrintJobs(
    groups: readonly (readonly VoucherBalanceMaterial[])[],
  ): Promise<number> {
    let created = 0;
    for (const group of groups) {
      const canonical = canonicalMaterial(group);
      const confirmation = consistentConfirmation(group);
      if (
        !confirmation ||
        confirmation.status !== "confirmed" ||
        confirmation.remainingCents <= 0
      ) {
        continue;
      }

      const jobId = voucherBalancePrintJobId(canonical.attemptId);
      if (await this.dependencies.printQueue.hasPrintJob(jobId)) continue;
      const rendered = await this.dependencies.renderer.render({
        ...canonical,
        confirmation,
      });
      if (!rendered) continue;
      const result =
        await this.dependencies.printQueue.enqueuePrintJobOnce({
          jobId,
          orderGuid: canonical.orderGuid,
          printerId: rendered.printerId,
          receiptBytes: rendered.receiptBytes,
          isReprint: false,
        });
      if (result === "created") {
        created += 1;
        this.requestPrintDrain();
      }
    }
    return created;
  }

  private requestPrintDrain(): void {
    if (!this.dependencies.requestPrintDrain) return;
    try {
      void this.dependencies.requestPrintDrain().catch(() => undefined);
    } catch {
      // 打印任务已经耐久入队；硬件唤醒失败交给启动、前台或人工重试恢复。
    }
  }
}

/**
 * 独立余额联只消费已经耐久确认的 post-sync 快照。券码既不签发也不替换，
 * CODE128 与 QR 都编码原券码，方便顾客继续使用同一张券。
 */
export class VoucherBalanceReceiptRenderer
implements VoucherBalanceReceiptRendererPort {
  public constructor(
    private readonly settings: ReturnReceiptSettingsPort,
  ) {}

  public async render(
    materialInput: VoucherBalanceMaterial & Readonly<{
      confirmation: Extract<
        VoucherLatestBalanceConfirmation,
        { status: "confirmed" }
      >;
    }>,
  ): Promise<RenderedReturnReceipt | null> {
    const material = normalizeRenderableMaterial(materialInput);
    const settings =
      await this.settings.getFrozenReturnReceiptSettings();
    if (!validSettings(settings)) return null;
    return {
      printerId: settings.printerId,
      receiptBytes: encodeVoucherBalanceReceipt(material, settings),
    };
  }
}

export function voucherBalancePrintJobId(attemptIdInput: string): string {
  return `voucher-balance:${requiredText(attemptIdInput, 96)}`;
}

function confirmationFromQuery(
  query: Awaited<ReturnType<VoucherLatestBalanceApiPort["query"]>>,
  material: VoucherBalanceMaterial,
  confirmedAtIsoInput: string,
): VoucherLatestBalanceConfirmation {
  const confirmedAtIso = canonicalIso(confirmedAtIsoInput);
  const voucher = query.voucher;
  if (
    query.found !== true ||
    !voucher ||
    !sameIdentity(voucher.voucherCode, material.voucherCode) ||
    !matchingResponseStore(voucher.storeCode, material.storeCode) ||
    voucher.status?.trim() !== "1"
  ) {
    return {
      status: "unavailable",
      remainingCents: null,
      confirmedAtIso,
    };
  }
  const remainingCents = amountToCents(voucher.remainingAmount);
  if (remainingCents === null || remainingCents < 0) {
    return {
      status: "unavailable",
      remainingCents: null,
      confirmedAtIso,
    };
  }
  return {
    status: "confirmed",
    remainingCents,
    confirmedAtIso,
  };
}

function groupMaterials(
  materials: readonly VoucherBalanceMaterial[],
): readonly (readonly VoucherBalanceMaterial[])[] {
  const groups = new Map<string, VoucherBalanceMaterial[]>();
  for (const material of materials) {
    const normalized = normalizeMaterial(material);
    const key =
      `${normalized.orderGuid}\u0000` +
      `${normalized.storeCode.toLocaleUpperCase("en-AU")}\u0000` +
      normalized.voucherCode.toLocaleUpperCase("en-AU");
    const group = groups.get(key);
    if (group) group.push(normalized);
    else groups.set(key, [normalized]);
  }
  return [...groups.values()]
    .map((group) =>
      group.sort((left, right) =>
        left.attemptId.localeCompare(right.attemptId),
      ),
    )
    .sort((left, right) =>
      canonicalMaterial(left).attemptId.localeCompare(
        canonicalMaterial(right).attemptId,
      ),
    );
}

function canonicalMaterial(
  group: readonly VoucherBalanceMaterial[],
): VoucherBalanceMaterial {
  const first = group[0];
  if (!first) throw new Error("VOUCHER_BALANCE_MATERIAL_MISSING");
  for (const current of group) {
    if (
      current.orderGuid !== first.orderGuid ||
      !sameIdentity(current.storeCode, first.storeCode) ||
      !sameIdentity(current.voucherCode, first.voucherCode)
    ) {
      throw new Error("VOUCHER_BALANCE_MATERIAL_CONFLICT");
    }
  }
  return first;
}

function consistentConfirmation(
  group: readonly VoucherBalanceMaterial[],
): VoucherLatestBalanceConfirmation | null {
  const confirmations = group
    .map((entry) => entry.confirmation)
    .filter(
      (
        value,
      ): value is VoucherLatestBalanceConfirmation => value !== null,
    );
  const first = confirmations[0];
  if (!first) return null;
  return confirmations.every(
    (value) => JSON.stringify(value) === JSON.stringify(first),
  )
    ? first
    : null;
}

function normalizeMaterial(
  material: VoucherBalanceMaterial,
): VoucherBalanceMaterial {
  if (!material || typeof material !== "object") {
    throw new Error("VOUCHER_BALANCE_MATERIAL_INVALID");
  }
  return {
    attemptId: requiredText(material.attemptId, 96),
    orderGuid: requiredText(material.orderGuid, 128),
    storeCode: requiredText(material.storeCode, 64),
    voucherCode: printableVoucherCode(material.voucherCode),
    confirmation:
      material.confirmation === null
        ? null
        : normalizeConfirmation(material.confirmation),
  };
}

function normalizeRenderableMaterial(
  material: VoucherBalanceMaterial & Readonly<{
    confirmation: Extract<
      VoucherLatestBalanceConfirmation,
      { status: "confirmed" }
    >;
  }>,
): VoucherBalanceMaterial & Readonly<{
  confirmation: Extract<
    VoucherLatestBalanceConfirmation,
    { status: "confirmed" }
  >;
}> {
  const normalized = normalizeMaterial(material);
  if (
    !normalized.confirmation ||
    normalized.confirmation.status !== "confirmed" ||
    normalized.confirmation.remainingCents <= 0
  ) {
    throw new Error("VOUCHER_BALANCE_CONFIRMATION_INVALID");
  }
  return {
    ...normalized,
    confirmation: normalized.confirmation,
  };
}

function normalizeConfirmation(
  confirmation: VoucherLatestBalanceConfirmation,
): VoucherLatestBalanceConfirmation {
  const confirmedAtIso = canonicalIso(confirmation.confirmedAtIso);
  if (confirmation.status === "unavailable") {
    if (confirmation.remainingCents !== null) {
      throw new Error("VOUCHER_BALANCE_CONFIRMATION_INVALID");
    }
    return {
      status: "unavailable",
      remainingCents: null,
      confirmedAtIso,
    };
  }
  if (
    confirmation.status !== "confirmed" ||
    !Number.isSafeInteger(confirmation.remainingCents) ||
    confirmation.remainingCents < 0
  ) {
    throw new Error("VOUCHER_BALANCE_CONFIRMATION_INVALID");
  }
  return {
    status: "confirmed",
    remainingCents: confirmation.remainingCents,
    confirmedAtIso,
  };
}

function validSettings(
  settings: FrozenReturnReceiptSettings | null,
): settings is FrozenReturnReceiptSettings {
  return Boolean(
    settings &&
      /^[A-Za-z0-9._:-]{1,128}$/u.test(settings.printerId) &&
      (settings.paper === "58mm" || settings.paper === "80mm") &&
      (settings.locale === "en" || settings.locale === "zh-CN"),
  );
}

function encodeVoucherBalanceReceipt(
  material: ReturnType<typeof normalizeRenderableMaterial>,
  settings: FrozenReturnReceiptSettings,
): Uint8Array {
  const output: number[] = [];
  appendEscPosInitialize(output);
  const width = settings.paper === "58mm" ? 32 : 48;
  const zh = settings.locale === "zh-CN";
  appendText(
    output,
    zh ? "===== 礼券余额 =====" : "===== VOUCHER BALANCE =====",
    "center",
    true,
  );
  appendWrappedText(
    output,
    `${zh ? "券码" : "Voucher"}: ${material.voucherCode}`,
    width,
    "center",
    true,
  );
  appendText(
    output,
    `${zh ? "最新余额" : "Latest Balance"}: ` +
      money(material.confirmation.remainingCents),
    "center",
    true,
  );
  appendText(output, "-".repeat(width), "left", false);
  appendWrappedText(
    output,
    `${zh ? "订单" : "Order"}: ${material.orderGuid}`,
    width,
    "left",
    false,
  );
  appendText(
    output,
    `${zh ? "余额确认时间" : "Balance Confirmed"}: ` +
      material.confirmation.confirmedAtIso,
    "left",
    false,
  );
  appendCode128(output, material.voucherCode);
  appendQrCode(output, material.voucherCode);
  output.push(0x1b, 0x64, 0x03, 0x1d, 0x56, 0x00);
  return Uint8Array.from(output);
}

function appendText(
  output: number[],
  value: string,
  alignment: "left" | "center" | "right",
  bold: boolean,
): void {
  const align = alignment === "center" ? 1 : alignment === "right" ? 2 : 0;
  output.push(0x1b, 0x61, align, 0x1b, 0x45, bold ? 1 : 0);
  output.push(...encodeEscPosText(value), 0x0a);
}

function appendWrappedText(
  output: number[],
  value: string,
  width: number,
  alignment: "left" | "center" | "right",
  bold: boolean,
): void {
  let line = "";
  for (const character of value) {
    if (line.length >= width) {
      appendText(output, line, alignment, bold);
      line = "";
    }
    line += character;
  }
  appendText(output, line, alignment, bold);
}

function appendCode128(output: number[], voucherCode: string): void {
  const data = new TextEncoder().encode(
    `{B${voucherCode.replaceAll("{", "{{")}`,
  );
  if (data.byteLength > 255) {
    throw new Error("VOUCHER_BALANCE_CODE_INVALID");
  }
  output.push(
    0x1b, 0x61, 0x01,
    0x1d, 0x48, 0x02,
    0x1d, 0x68, 0x50,
    0x1d, 0x77, 0x02,
    0x1d, 0x6b, 0x49, data.byteLength,
    ...data,
    0x0a,
  );
}

function appendQrCode(output: number[], voucherCode: string): void {
  const data = new TextEncoder().encode(voucherCode);
  const storeLength = data.byteLength + 3;
  output.push(
    0x1b, 0x61, 0x01,
    0x1d, 0x28, 0x6b, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00,
    0x1d, 0x28, 0x6b, 0x03, 0x00, 0x31, 0x43, 0x06,
    0x1d, 0x28, 0x6b, 0x03, 0x00, 0x31, 0x45, 0x31,
    0x1d, 0x28, 0x6b,
    storeLength & 0xff,
    (storeLength >> 8) & 0xff,
    0x31, 0x50, 0x30,
    ...data,
    0x1d, 0x28, 0x6b, 0x03, 0x00, 0x31, 0x51, 0x30,
    0x0a,
  );
}

function sameIdentity(value: unknown, expected: string): boolean {
  return (
    typeof value === "string" &&
    value.trim().toLocaleUpperCase("en-AU") ===
      expected.trim().toLocaleUpperCase("en-AU")
  );
}

function matchingResponseStore(
  value: unknown,
  expected: string,
): boolean {
  return (
    value === null ||
    value === undefined ||
    (typeof value === "string" &&
      (value.trim() === "" || sameIdentity(value, expected)))
  );
}

function printableVoucherCode(value: string): string {
  const normalized = requiredText(value, 80);
  if (!/^[\x20-\x7e]+$/u.test(normalized)) {
    throw new Error("VOUCHER_BALANCE_CODE_INVALID");
  }
  return normalized;
}

function amountToCents(value: unknown): number | null {
  if (typeof value !== "number" || !Number.isFinite(value)) return null;
  const rawCents = value * 100;
  const cents = Math.round(rawCents);
  return Number.isSafeInteger(cents) &&
    Math.abs(rawCents - cents) <= 1e-6
    ? cents
    : null;
}

function money(cents: number): string {
  return `AU$${(cents / 100).toFixed(2)}`;
}

function requiredText(value: string, max: number): string {
  if (typeof value !== "string") {
    throw new Error("VOUCHER_BALANCE_TEXT_INVALID");
  }
  const normalized = value.trim();
  if (
    !normalized ||
    normalized.length > max ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new Error("VOUCHER_BALANCE_TEXT_INVALID");
  }
  return normalized;
}

function canonicalIso(value: string): string {
  const parsed = Date.parse(value);
  if (!Number.isFinite(parsed) || new Date(parsed).toISOString() !== value) {
    throw new Error("VOUCHER_BALANCE_TIME_INVALID");
  }
  return value;
}
