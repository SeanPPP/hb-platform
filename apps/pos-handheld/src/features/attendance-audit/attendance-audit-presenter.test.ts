import assert from "node:assert/strict";
import test from "node:test";

import { AttendanceAuditPresenter } from "./attendance-audit-presenter";
import type { AttendanceQrState } from "./attendance-qr-controller";
import type { OperationAuditPresenterState } from "@hb/pos-domain/features/attendance-audit/operation-audit-presenter";

test("启动二维码状态机并按权限读取审计，统一发布只读快照", async () => {
  const qr = new FakeQrController();
  const audit = new FakeAuditPresenter(true);
  const presenter = new AttendanceAuditPresenter({ audit, qr });
  let changed = 0;
  presenter.subscribe(() => {
    changed += 1;
  });

  presenter.start();
  assert.equal(qr.startCount, 1);
  assert.equal(audit.loadCount, 1);

  qr.publish({ secondsRemaining: 9 });
  audit.publish({ kind: "ready" });
  assert.equal(presenter.getState().qr.secondsRemaining, 9);
  assert.equal(presenter.getState().audit.kind, "ready");
  assert.equal(changed, 2);

  presenter.setOnline(false);
  assert.deepEqual(audit.onlineValues, [false]);
  await presenter.refreshAttendanceQr();
  assert.equal(qr.refreshCount, 1);
});

test("缺少 Audit.View 时仍启动考勤 QR，但不主动读取审计", () => {
  const qr = new FakeQrController();
  const audit = new FakeAuditPresenter(false);
  const presenter = new AttendanceAuditPresenter({ audit, qr });

  presenter.start();

  assert.equal(qr.startCount, 1);
  assert.equal(audit.loadCount, 0);
  assert.equal(presenter.getState().audit.access.canView, false);
});

test("销毁时取消底层订阅并销毁两个状态机，之后不再发布", () => {
  const qr = new FakeQrController();
  const audit = new FakeAuditPresenter(true);
  const presenter = new AttendanceAuditPresenter({ audit, qr });
  let changed = 0;
  presenter.subscribe(() => {
    changed += 1;
  });

  presenter.destroy();
  qr.publish({ secondsRemaining: 2 });
  audit.publish({ kind: "failed" });

  assert.equal(qr.destroyCount, 1);
  assert.equal(audit.destroyCount, 1);
  assert.equal(qr.unsubscribeCount, 1);
  assert.equal(audit.unsubscribeCount, 1);
  assert.equal(changed, 0);
});

class FakeQrController {
  public startCount = 0;
  public refreshCount = 0;
  public destroyCount = 0;
  public unsubscribeCount = 0;
  private readonly listeners = new Set<() => void>();
  private state: AttendanceQrState = qrState();

  public getState = () => this.state;

  public subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.unsubscribeCount += 1;
      this.listeners.delete(listener);
    };
  };

  public start(): void {
    this.startCount += 1;
  }

  public async refresh(): Promise<void> {
    this.refreshCount += 1;
  }

  public destroy(): void {
    this.destroyCount += 1;
  }

  public publish(patch: Partial<AttendanceQrState>): void {
    this.state = Object.freeze({ ...this.state, ...patch });
    for (const listener of this.listeners) listener();
  }
}

class FakeAuditPresenter {
  public loadCount = 0;
  public destroyCount = 0;
  public unsubscribeCount = 0;
  public readonly onlineValues: boolean[] = [];
  private readonly listeners = new Set<() => void>();
  private state: OperationAuditPresenterState;

  public constructor(canView: boolean) {
    this.state = auditState(canView);
  }

  public getState = () => this.state;

  public subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.unsubscribeCount += 1;
      this.listeners.delete(listener);
    };
  };

  public async load(): Promise<void> {
    this.loadCount += 1;
  }

  public setOnline(online: boolean): void {
    this.onlineValues.push(online);
  }

  public setQuery(): void {}

  public setSource(): void {}

  public setUploadState(): void {}

  public async select(): Promise<void> {}

  public destroy(): void {
    this.destroyCount += 1;
  }

  public publish(patch: Partial<OperationAuditPresenterState>): void {
    this.state = Object.freeze({ ...this.state, ...patch });
    for (const listener of this.listeners) listener();
  }
}

function qrState(): AttendanceQrState {
  return Object.freeze({
    deviceText: "IPAD-1",
    kind: "ready",
    online: true,
    qrImageUri: "data:image/png;base64,QQ==",
    requiresOnlineResync: false,
    secondsRemaining: 15,
    statusCode: "online-verified",
    storeText: "Store One (S1)",
  });
}

function auditState(canView: boolean): OperationAuditPresenterState {
  return Object.freeze({
    access: Object.freeze({ canView }),
    detail: null,
    detailLoading: false,
    kind: "idle",
    online: true,
    query: "",
    rows: Object.freeze([]),
    selectedEventId: null,
    source: "local",
    statusCode: null,
    uploadState: null,
  });
}
