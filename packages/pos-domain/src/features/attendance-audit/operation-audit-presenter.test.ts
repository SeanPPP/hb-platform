import assert from "node:assert/strict";
import test from "node:test";

import {
  AUDIT_VIEW_PERMISSION,
  OperationAuditPresenter,
  type OperationAuditRawRecord,
  type OperationAuditReadPort,
} from "./operation-audit-presenter";

class FakeAuditRead implements OperationAuditReadPort {
  public readonly listInputs: unknown[] = [];
  public readonly detailInputs: unknown[] = [];
  public rows: readonly OperationAuditRawRecord[] = [];
  public details = new Map<string, OperationAuditRawRecord | null>();
  public nextList: Promise<readonly OperationAuditRawRecord[]> | null =
    null;
  public nextDetail: Promise<OperationAuditRawRecord | null> | null =
    null;

  public async list(input: unknown): Promise<readonly OperationAuditRawRecord[]> {
    this.listInputs.push(input);
    if (this.nextList) return this.nextList;
    return this.rows;
  }

  public async get(
    input: Readonly<{ eventId: string }>,
  ): Promise<OperationAuditRawRecord | null> {
    this.detailInputs.push(input);
    if (this.nextDetail) return this.nextDetail;
    return this.details.get(input.eventId) ?? null;
  }
}

test("缺少 WPF Audit.View 精确权限时拒绝读取", async () => {
  const read = new FakeAuditRead();
  const presenter = createPresenter(read, [
    `${AUDIT_VIEW_PERMISSION}.Extra`,
  ]);

  await presenter.load();

  assert.equal(presenter.getState().kind, "unauthorized");
  assert.equal(read.listInputs.length, 0);
});

test("本地审计按可信门店/设备读取；离线时远程来源失败关闭", async () => {
  const read = new FakeAuditRead();
  read.rows = [record()];
  const presenter = createPresenter(read);

  await presenter.load();

  assert.deepEqual(read.listInputs, [
    {
      deviceCode: "IPAD-1",
      keyword: null,
      limit: 100,
      source: "local",
      storeCode: "S1",
      uploadState: null,
    },
  ]);
  assert.equal(presenter.getState().rows.length, 1);

  presenter.setSource("remote");
  presenter.setOnline(false);
  await presenter.load();
  assert.equal(presenter.getState().statusCode, "online-required");
  assert.equal(read.listInputs.length, 1);
});

test("列表与详情对 token、PAN、授权字段和联系方式做二次脱敏", async () => {
  const read = new FakeAuditRead();
  const secret =
    "Bearer eyJsecret HBPOSE2-SECRET HBATE1.kid.secret 4111 1111 1111 1111 " +
    "authorizationToken=topsecret reservationToken: locksecret " +
    "bob@example.com 0412 345 678";
  read.rows = [
    record({
      cashierName: `Alice ${secret}`,
      primaryProduct: `Tea ${secret}`,
      safeMessage: secret,
    }),
  ];
  read.details.set(
    EVENT_ID,
    record({
      items: [
        {
          actualAmountDeltaCents: 100,
          displayName: `Item ${secret}`,
          lineIndex: 0,
          productCode: `P1 ${secret}`,
          quantityDelta: "1",
        },
      ],
      safeMessage: secret,
    }),
  );
  const presenter = createPresenter(read);

  await presenter.load();
  await presenter.select(EVENT_ID);

  const serialized = JSON.stringify(presenter.getState());
  for (const value of [
    "eyJsecret",
    "HBPOSE2-SECRET",
    "HBATE1.kid.secret",
    "4111 1111 1111 1111",
    "topsecret",
    "locksecret",
    "bob@example.com",
    "0412 345 678",
  ]) {
    assert.equal(serialized.includes(value), false, value);
  }
  assert.match(serialized, /\[REDACTED_/u);
});

test("跨门店/设备、重复 EventId 或无效金额使整页失败，不显示误导性部分结果", async () => {
  const read = new FakeAuditRead();
  const presenter = createPresenter(read);
  read.rows = [
    record(),
    record({
      eventId: "20000000-0000-4000-8000-000000000002",
      storeCode: "S2",
    }),
  ];

  await presenter.load();
  assert.equal(presenter.getState().kind, "failed");
  assert.equal(presenter.getState().rows.length, 0);

  read.rows = [record(), record()];
  await presenter.load();
  assert.equal(presenter.getState().kind, "failed");

  read.rows = [record({ paymentAmountCents: 1.2 })];
  await presenter.load();
  assert.equal(presenter.getState().kind, "failed");
});

test("较旧详情异步结果不能覆盖新选择", async () => {
  const read = new FakeAuditRead();
  const presenter = createPresenter(read);
  read.rows = [
    record(),
    record({
      eventId: EVENT_ID_2,
      operationType: "CASH_DRAWER_OPEN",
    }),
  ];
  await presenter.load();
  const first = deferred<OperationAuditRawRecord | null>();
  read.nextDetail = first.promise;
  const selectingFirst = presenter.select(EVENT_ID);
  read.nextDetail = null;
  read.details.set(EVENT_ID_2, record({ eventId: EVENT_ID_2 }));

  await presenter.select(EVENT_ID_2);
  first.resolve(record({ safeMessage: "stale" }));
  await selectingFirst;

  assert.equal(presenter.getState().selectedEventId, EVENT_ID_2);
  assert.equal(presenter.getState().detail?.eventId, EVENT_ID_2);
});

function createPresenter(
  read: OperationAuditReadPort,
  permissions: readonly string[] = [AUDIT_VIEW_PERMISSION],
) {
  return new OperationAuditPresenter({
    initialOnline: true,
    permissions,
    read,
    trustedDeviceCode: "IPAD-1",
    trustedStoreCode: "S1",
  });
}

const EVENT_ID = "10000000-0000-4000-8000-000000000001";
const EVENT_ID_2 = "20000000-0000-4000-8000-000000000001";

function record(
  overrides: Partial<OperationAuditRawRecord> = {},
): OperationAuditRawRecord {
  return {
    cashierName: "Alice",
    correlationId: "corr-1",
    deviceCode: "IPAD-1",
    eventId: EVENT_ID,
    items: [],
    occurredAtIso: "2026-07-28T01:02:03.000Z",
    operationType: "SALE_COMPLETE",
    orderGuid: "30000000-0000-4000-8000-000000000001",
    outcome: "Succeeded",
    paymentAmountCents: 1_000,
    primaryProduct: "Tea",
    productCount: 1,
    receiptNumber: "R-1",
    safeMessage: "Completed",
    storeCode: "S1",
    uploadState: "uploaded",
    ...overrides,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolver) => {
    resolve = resolver;
  });
  return { promise, resolve };
}
