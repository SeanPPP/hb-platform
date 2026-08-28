import type {
  AttendanceQrController,
  AttendanceQrState,
} from "./attendance-qr-controller";
import type {
  OperationAuditPresenter,
  OperationAuditPresenterState,
  OperationAuditSource,
  OperationAuditUploadState,
} from "@hb/pos-domain/features/attendance-audit/operation-audit-presenter";

export type AttendanceAuditPresenterState = Readonly<{
  audit: OperationAuditPresenterState;
  qr: AttendanceQrState;
}>;

type AttendanceQrPresenterPort = Pick<
  AttendanceQrController,
  "destroy" | "getState" | "refresh" | "start" | "subscribe"
>;

type OperationAuditPresenterPort = Pick<
  OperationAuditPresenter,
  | "destroy"
  | "getState"
  | "load"
  | "select"
  | "setOnline"
  | "setQuery"
  | "setSource"
  | "setUploadState"
  | "subscribe"
>;

/**
 * 将考勤 QR 与审计查看器合并为单一只读 UI 快照。两套安全状态机仍保持隔离：
 * 缺少 Audit.View 不影响考勤二维码，审计筛选也不会触碰 QR 密钥或 token。
 */
export class AttendanceAuditPresenter {
  private readonly listeners = new Set<() => void>();
  private readonly unsubscribeQr: () => void;
  private readonly unsubscribeAudit: () => void;
  private state: AttendanceAuditPresenterState;
  private started = false;
  private destroyed = false;

  public constructor(
    private readonly options: Readonly<{
      audit: OperationAuditPresenterPort;
      qr: AttendanceQrPresenterPort;
    }>,
  ) {
    this.state = this.captureState();
    this.unsubscribeQr = options.qr.subscribe(this.publishSnapshot);
    this.unsubscribeAudit =
      options.audit.subscribe(this.publishSnapshot);
  }

  public readonly getState = (): AttendanceAuditPresenterState =>
    this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public start(): void {
    if (this.destroyed || this.started) return;
    this.started = true;
    this.options.qr.start();
    if (this.options.audit.getState().access.canView) {
      void this.options.audit.load();
    }
  }

  public refreshAttendanceQr(): Promise<void> {
    return this.options.qr.refresh();
  }

  public loadAudit(): Promise<void> {
    return this.options.audit.load();
  }

  public selectAudit(eventId: string): Promise<void> {
    return this.options.audit.select(eventId);
  }

  public setAuditQuery(value: string): void {
    this.options.audit.setQuery(value);
  }

  public setAuditSource(source: OperationAuditSource): void {
    this.options.audit.setSource(source);
  }

  public setAuditUploadState(
    uploadState: OperationAuditUploadState | null,
  ): void {
    this.options.audit.setUploadState(uploadState);
  }

  public setOnline(online: boolean): void {
    this.options.audit.setOnline(online);
  }

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.unsubscribeQr();
    this.unsubscribeAudit();
    this.options.qr.destroy();
    this.options.audit.destroy();
    this.listeners.clear();
  }

  private readonly publishSnapshot = (): void => {
    if (this.destroyed) return;
    this.state = this.captureState();
    for (const listener of this.listeners) listener();
  };

  private captureState(): AttendanceAuditPresenterState {
    return Object.freeze({
      audit: this.options.audit.getState(),
      qr: this.options.qr.getState(),
    });
  }
}
