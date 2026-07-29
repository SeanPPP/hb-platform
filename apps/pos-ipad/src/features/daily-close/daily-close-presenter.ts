import {
  resolveDailyCloseAccess,
  type DailyCloseAccess,
} from "./daily-close-authorization";
import {
  buildDailyCloseArchiveCommit,
  businessDateInTimeZone,
  dailyCloseBusinessDayScope,
  type DailyCloseIdentity,
} from "./daily-close-domain";

import {
  AUD_CASH_DENOMINATIONS_CENTS,
  normalizeDailyCloseCounts,
  type AudCashDenominationCents,
  type DailyCloseArchive,
  type DailyCloseDenominationCount,
  type DailyCloseRepositoryPort,
  type DailyCloseSummary,
} from "@/core/contracts";
import {
  buildDailyCloseReceipt,
  type DailyCloseReceiptDocument,
} from "@/features/receipts/daily-close-receipt";
import type {
  ReceiptLocale,
  ReceiptPaper,
} from "@/features/receipts/receipt-document";

export type DailyCloseStatusCode =
  | "invalid-business-date"
  | "load-failed"
  | "permission-required"
  | "reprint-failed"
  | "reprint-printed"
  | "save-failed"
  | "saved-print-failed"
  | "saved-printed"
  | "select-archive-required";

export type DailyCloseState = Readonly<{
  access: DailyCloseAccess;
  activePane: "count" | "history";
  archives: readonly DailyCloseArchive[];
  businessDate: string;
  busy: boolean;
  coinsSubtotalCents: number;
  countedCashCents: number;
  counts: readonly DailyCloseDenominationCount[];
  kind: "idle" | "loading" | "ready" | "unauthorized" | "failed";
  notesSubtotalCents: number;
  selectedArchive: DailyCloseArchive | null;
  statusCode: DailyCloseStatusCode | null;
  summary: DailyCloseSummary | null;
  varianceCents: number;
}>;

export type DailyClosePrintJob = Readonly<{
  archive: DailyCloseArchive;
  document: DailyCloseReceiptDocument;
  reprint: boolean;
}>;

export interface DailyClosePrinterPort {
  print(job: DailyClosePrintJob): Promise<void>;
}

export type DailyClosePresenterOptions = Readonly<{
  businessTimeZone: string;
  createId(): string;
  identity: DailyCloseIdentity;
  initialBusinessDate?: string;
  now(): Date;
  printer: DailyClosePrinterPort;
  receiptLocale: ReceiptLocale;
  receiptPaper: ReceiptPaper;
  repository: DailyCloseRepositoryPort;
  storeName: string;
  historyLimit?: number;
}>;

export class DailyClosePresenter {
  private readonly access: DailyCloseAccess;
  private readonly listeners = new Set<() => void>();
  private readonly historyLimit: number;
  private state: DailyCloseState;
  private destroyed = false;
  private loadGeneration = 0;
  private actionInFlight: Promise<void> | null = null;

  public constructor(private readonly options: DailyClosePresenterOptions) {
    this.access = resolveDailyCloseAccess(options.identity.permissions);
    this.historyLimit = normalizeHistoryLimit(options.historyLimit ?? 100);
    const businessDate =
      options.initialBusinessDate ??
      businessDateInTimeZone(options.now(), options.businessTimeZone);
    // 构造期先验证日期与可信门店/设备，避免 UI 首刷后才暴露组合错误。
    dailyCloseBusinessDayScope({
      businessDate,
      businessTimeZone: options.businessTimeZone,
      deviceCode: options.identity.deviceCode,
      storeCode: options.identity.storeCode,
    });
    const totals = countTotals(zeroCounts(), 0);
    this.state = Object.freeze({
      access: this.access,
      activePane: "count",
      archives: Object.freeze([]),
      businessDate,
      busy: false,
      ...totals,
      kind: this.access.canView ? "idle" : "unauthorized",
      selectedArchive: null,
      statusCode: this.access.canView ? null : "permission-required",
      summary: null,
    });
  }

  public readonly getState = (): DailyCloseState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.loadGeneration += 1;
    this.listeners.clear();
  }

  public async load(): Promise<void> {
    if (this.destroyed) return;
    if (!this.access.canView) {
      this.patch({
        kind: "unauthorized",
        statusCode: "permission-required",
      });
      return;
    }
    const generation = ++this.loadGeneration;
    let scope;
    try {
      scope = dailyCloseBusinessDayScope({
        businessDate: this.state.businessDate,
        businessTimeZone: this.options.businessTimeZone,
        deviceCode: this.options.identity.deviceCode,
        storeCode: this.options.identity.storeCode,
      });
    } catch {
      this.patch({
        kind: "failed",
        statusCode: "invalid-business-date",
      });
      return;
    }
    this.patch({ kind: "loading", statusCode: null });
    try {
      const [summary, archives] = await Promise.all([
        this.options.repository.summarize(scope),
        this.options.repository.listArchives({
          businessDate: scope.businessDate,
          deviceCode: scope.deviceCode,
          limit: this.historyLimit,
          storeCode: scope.storeCode,
        }),
      ]);
      if (!this.isCurrentLoad(generation)) return;
      const selectedArchive =
        archives.find(
          (archive) =>
            archive.closeId === this.state.selectedArchive?.closeId,
        ) ??
        archives[0] ??
        null;
      this.patch({
        archives: Object.freeze([...archives]),
        kind: "ready",
        selectedArchive,
        statusCode: null,
        summary,
        ...countTotals(this.state.counts, summary.expectedCashCents),
      });
    } catch {
      if (!this.isCurrentLoad(generation)) return;
      this.patch({ kind: "failed", statusCode: "load-failed" });
    }
  }

  public setBusinessDate(businessDate: string): boolean {
    if (this.destroyed || this.state.busy) return false;
    let normalizedBusinessDate: string;
    try {
      normalizedBusinessDate = dailyCloseBusinessDayScope({
        businessDate,
        businessTimeZone: this.options.businessTimeZone,
        deviceCode: this.options.identity.deviceCode,
        storeCode: this.options.identity.storeCode,
      }).businessDate;
    } catch {
      this.patch({ statusCode: "invalid-business-date" });
      return false;
    }
    if (normalizedBusinessDate === this.state.businessDate) {
      this.patch({ statusCode: null });
      return true;
    }
    this.loadGeneration += 1;
    const totals = countTotals(zeroCounts(), 0);
    this.patch({
      activePane: "count",
      archives: Object.freeze([]),
      businessDate: normalizedBusinessDate,
      ...totals,
      kind: "idle",
      selectedArchive: null,
      statusCode: null,
      summary: null,
    });
    return true;
  }

  public setCount(
    denominationCents: number,
    quantity: number,
  ): boolean {
    if (
      this.destroyed ||
      this.state.busy ||
      !this.access.canSave ||
      !AUD_CASH_DENOMINATIONS_CENTS.includes(
        denominationCents as AudCashDenominationCents,
      ) ||
      !Number.isSafeInteger(quantity) ||
      quantity < 0
    ) {
      if (!this.access.canSave) {
        this.patch({ statusCode: "permission-required" });
      }
      return false;
    }
    const counts = normalizeDailyCloseCounts(
      this.state.counts.map((entry) => ({
        denominationCents: entry.denominationCents,
        quantity:
          entry.denominationCents === denominationCents
            ? quantity
            : entry.quantity,
      })),
    );
    this.patch({
      statusCode: null,
      ...countTotals(
        counts,
        this.state.summary?.expectedCashCents ?? 0,
      ),
    });
    return true;
  }

  public saveAndPrint(): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (this.actionInFlight) return this.actionInFlight;
    const operation = this.performSaveAndPrint().finally(() => {
      if (this.actionInFlight === operation) this.actionInFlight = null;
    });
    this.actionInFlight = operation;
    return operation;
  }

  public showCount(): void {
    if (!this.destroyed) this.patch({ activePane: "count" });
  }

  public showHistory(): void {
    if (!this.destroyed) this.patch({ activePane: "history" });
  }

  public selectArchive(closeId: string): void {
    if (this.destroyed) return;
    const selectedArchive =
      this.state.archives.find(
        (archive) => archive.closeId === closeId,
      ) ?? null;
    if (!selectedArchive) return;
    this.patch({ activePane: "history", selectedArchive, statusCode: null });
  }

  public reprintSelected(): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (this.actionInFlight) return this.actionInFlight;
    const operation = this.performReprint().finally(() => {
      if (this.actionInFlight === operation) this.actionInFlight = null;
    });
    this.actionInFlight = operation;
    return operation;
  }

  private async performSaveAndPrint(): Promise<void> {
    if (!this.access.canSave) {
      this.patch({ statusCode: "permission-required" });
      return;
    }
    if (!this.state.summary || this.state.kind !== "ready") {
      this.patch({ statusCode: "load-failed" });
      return;
    }
    this.patch({ busy: true, statusCode: null });
    const summary = this.state.summary;
    const savedAtIso = this.options.now().toISOString();
    const closeId = this.options.createId();
    const auditEventId = this.options.createId();
    const commit = buildDailyCloseArchiveCommit({
      auditEventId,
      closeId,
      counts: this.state.counts,
      savedAtIso,
      savedCashierId: this.options.identity.cashierId,
      savedCashierName: this.options.identity.cashierName,
      summary,
    });
    let archive: DailyCloseArchive;
    try {
      const saved = await this.options.repository.saveArchive(commit);
      archive = saved.archive;
    } catch {
      if (!this.destroyed) {
        this.patch({ busy: false, statusCode: "save-failed" });
      }
      return;
    }

    if (!this.destroyed) {
      const archives = Object.freeze([
        archive,
        ...this.state.archives.filter(
          (candidate) => candidate.closeId !== archive.closeId,
        ),
      ].slice(0, this.historyLimit));
      this.patch({
        activePane: "history",
        archives,
        selectedArchive: archive,
        ...countTotals(zeroCounts(), summary.expectedCashCents),
      });
    }

    const job = this.createPrintJob(archive, false);
    try {
      await this.options.printer.print(job);
      if (!this.destroyed) {
        this.patch({ busy: false, statusCode: "saved-printed" });
      }
    } catch {
      // 中文注释：归档和审计已经提交；打印失败只改变安全状态码，绝不补偿删除。
      if (!this.destroyed) {
        this.patch({ busy: false, statusCode: "saved-print-failed" });
      }
    }
  }

  private async performReprint(): Promise<void> {
    if (!this.access.canReprint) {
      this.patch({ statusCode: "permission-required" });
      return;
    }
    const archive = this.state.selectedArchive;
    if (!archive) {
      this.patch({ statusCode: "select-archive-required" });
      return;
    }
    this.patch({ busy: true, statusCode: null });
    try {
      await this.options.printer.print(this.createPrintJob(archive, true));
      if (!this.destroyed) {
        this.patch({ busy: false, statusCode: "reprint-printed" });
      }
    } catch {
      if (!this.destroyed) {
        this.patch({ busy: false, statusCode: "reprint-failed" });
      }
    }
  }

  private createPrintJob(
    archive: DailyCloseArchive,
    reprint: boolean,
  ): DailyClosePrintJob {
    return Object.freeze({
      archive,
      document: buildDailyCloseReceipt({
        archive,
        locale: this.options.receiptLocale,
        paper: this.options.receiptPaper,
        reprint,
        storeName: this.options.storeName,
      }),
      reprint,
    });
  }

  private isCurrentLoad(generation: number): boolean {
    return !this.destroyed && generation === this.loadGeneration;
  }

  private patch(patch: Partial<DailyCloseState>): void {
    if (this.destroyed) return;
    this.state = Object.freeze({ ...this.state, ...patch });
    for (const listener of this.listeners) listener();
  }
}

function zeroCounts(): readonly DailyCloseDenominationCount[] {
  return normalizeDailyCloseCounts([]);
}

function countTotals(
  counts: readonly DailyCloseDenominationCount[],
  expectedCashCents: number,
): Pick<
  DailyCloseState,
  | "coinsSubtotalCents"
  | "countedCashCents"
  | "counts"
  | "notesSubtotalCents"
  | "varianceCents"
> {
  const notesSubtotalCents = counts
    .filter((entry) => entry.denominationCents >= 500)
    .reduce((sum, entry) => safeAdd(sum, entry.subtotalCents), 0);
  const coinsSubtotalCents = counts
    .filter((entry) => entry.denominationCents < 500)
    .reduce((sum, entry) => safeAdd(sum, entry.subtotalCents), 0);
  const countedCashCents = safeAdd(
    notesSubtotalCents,
    coinsSubtotalCents,
  );
  return {
    coinsSubtotalCents,
    countedCashCents,
    counts: Object.freeze([...counts]),
    notesSubtotalCents,
    varianceCents: safeAdd(countedCashCents, -expectedCashCents),
  };
}

function safeAdd(left: number, right: number): number {
  const result = left + right;
  if (!Number.isSafeInteger(result)) {
    throw new TypeError("Daily close cash total is invalid.");
  }
  return result;
}

function normalizeHistoryLimit(value: number): number {
  if (!Number.isSafeInteger(value) || value < 1 || value > 500) {
    throw new TypeError("Daily close history limit is invalid.");
  }
  return value;
}
