import {
  buildStoreVoucherDetailTargets,
  buildStoreVoucherListPayload,
  normalizeStoreVoucherDetail,
  normalizeStoreVouchersResponse,
} from "./api";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

const defaultPayload = buildStoreVoucherListPayload({});
assertDeepEqual(
  defaultPayload,
  {
    storeCode: undefined,
    status: undefined,
    startDate: undefined,
    endDate: undefined,
    pageNumber: 1,
    pageSize: 20,
  },
  "default voucher payload keeps empty filters and default pagination"
);

const filteredPayload = buildStoreVoucherListPayload({
  page: 2,
  pageSize: 50,
  filters: {
    branchCode: " STO01 ",
    status: "1",
    startDate: " 2026-05-01 ",
    endDate: "2026-05-31",
  },
});
assertDeepEqual(
  filteredPayload,
  {
    storeCode: "STO01",
    status: "1",
    startDate: "2026-05-01",
    endDate: "2026-05-31",
    pageNumber: 2,
    pageSize: 50,
  },
  "voucher payload trims filters and maps branchCode to storeCode"
);

const invalidPayload = buildStoreVoucherListPayload({
  page: 0,
  pageSize: 999,
  filters: { storeCode: "  " },
});
assertEqual(invalidPayload.pageNumber, 1, "invalid voucher page falls back to first page");
assertEqual(invalidPayload.pageSize, 20, "invalid voucher page size falls back to 20");
assertEqual(invalidPayload.storeCode, undefined, "blank store code is omitted");

assertDeepEqual(
  buildStoreVoucherDetailTargets({
    voucherCode: " VC-001 ",
    id: " internal-1 ",
  }),
  ["VC-001", "internal-1"],
  "detail targets prefer voucher code before internal id",
);

assertDeepEqual(
  buildStoreVoucherDetailTargets({
    voucherCode: " SAME ",
    id: "SAME",
  }),
  ["SAME"],
  "detail targets should deduplicate equivalent voucher code and id",
);

const listResult = normalizeStoreVouchersResponse({
  Items: [
    {
      ID: 42,
      VoucherCode: "V-001",
      VoucherType: "3",
      StoreCode: "BNE01",
      Amount: "30.5",
      RemainingAmount: "12.25",
      Status: "1",
      CreateTime: "2026-05-01T00:00:00Z",
      UpdateTime: "2026-05-02T00:00:00Z",
      ExpiredDate: "2026-12-31T00:00:00Z",
      Remark: "legacy payload",
    },
  ],
  TotalCount: "8",
  Page: "3",
  Limit: "100",
});
assertEqual(listResult.items.length, 1, "voucher list keeps items");
assertEqual(listResult.items[0]?.id, "42", "voucher list normalizes ID to string");
assertEqual(listResult.items[0]?.voucherType, 3, "voucher list normalizes voucher type");
assertEqual(listResult.items[0]?.remainingAmount, 12.25, "voucher list normalizes remaining amount");
assertEqual(listResult.items[0]?.status, 1, "voucher list normalizes numeric string status");
assertEqual(listResult.total, 8, "voucher list normalizes total count");
assertEqual(listResult.pageNumber, 3, "voucher list normalizes page number");
assertEqual(listResult.pageSize, 100, "voucher list normalizes page size");

const detail = normalizeStoreVoucherDetail({
  storeVoucher: {
    id: "voucher-1",
    voucherCode: "V-002",
    storeCode: "SYD01",
    supplierCode: "SUP01",
    supplierName: "Supplier One",
    amount: "80",
    remainingAmount: "20",
    status: "active",
  },
  LedgerItems: [
    {
      LedgerId: "ledger-1",
      VoucherCode: "V-002",
      Type: "Issue",
      Amount: "80",
      RemainingAmount: "80",
      CreateTime: "2026-05-05T10:00:00Z",
      UserName: "Alice",
    },
    {
      ledgerId: "ledger-2",
      voucherCode: "V-002",
      action: "used",
      usedAmount: "60",
      balance: "20",
      actionTime: "2026-05-06T11:00:00Z",
      orderGuid: "order-9",
      orderNo: "SO-009",
      cashierName: "Bob",
    },
  ],
  Orders: [
    {
      OrderGUID: "order-9",
      OrderNo: "SO-009",
      BranchCode: "SYD01",
      SupplierCode: "SUP01",
      ActualAmount: "60",
      OrderTime: "2026-05-06T11:00:00Z",
    },
  ],
});
assertEqual(detail.voucher?.voucherCode, "V-002", "voucher detail normalizes nested voucher");
assertEqual(detail.ledger[0]?.action, "issued", "ledger infers issued action from legacy type");
assertEqual(detail.ledger[1]?.action, "used", "ledger keeps explicit used action");
assertEqual(detail.ledger[1]?.remainingAmount, 20, "ledger normalizes balance field");
assertEqual(detail.relatedOrders[0]?.orderGuid, "order-9", "related orders normalize order guid");
assertEqual(detail.relatedOrders[0]?.amount, 60, "related orders normalize actual amount fallback");

const wrappedDetail = normalizeStoreVoucherDetail({
  success: true,
  data: {
    voucher: {
      ID: 1429,
      VoucherCode: "V20260525161428",
    },
    ledger: [],
    relatedOrders: [],
  },
});
assertEqual(
  wrappedDetail.voucher?.voucherCode,
  "V20260525161428",
  "voucher detail unwraps standard API response data",
);
