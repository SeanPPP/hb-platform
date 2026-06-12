import {
  buildInstallmentOrderListPayload,
  normalizeInstallmentOrderDetail,
  normalizeInstallmentOrdersResponse,
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

const defaultPayload = buildInstallmentOrderListPayload({});
assertDeepEqual(
  defaultPayload,
  {
    startDate: undefined,
    endDate: undefined,
    branchCode: undefined,
    orderType: 4,
    status: undefined,
    keyword: undefined,
    pageNumber: 1,
    pageSize: 20,
  },
  "default payload keeps installment order type and pagination defaults"
);

const filteredPayload = buildInstallmentOrderListPayload({
  page: 3,
  pageSize: 50,
  filters: {
    startDate: " 2026-05-01 ",
    endDate: "2026-05-31",
    branchCode: " BNE01 ",
    status: 7,
    userPhone: " 0412 345 678 ",
    userName: " Alice ",
  },
});
assertDeepEqual(
  filteredPayload,
  {
    startDate: "2026-05-01",
    endDate: "2026-05-31",
    branchCode: "BNE01",
    orderType: 4,
    status: 7,
    keyword: "0412 345 678 Alice",
    pageNumber: 3,
    pageSize: 50,
  },
  "filtered payload trims values and preserves both phone and name in keyword"
);

const invalidPayload = buildInstallmentOrderListPayload({
  page: -1,
  pageSize: 77,
  filters: {
    orderType: 9,
    userPhone: "   ",
    userName: "",
  },
});
assertEqual(invalidPayload.pageNumber, 1, "invalid page falls back to first page");
assertEqual(invalidPayload.pageSize, 20, "invalid page size falls back to 20");
assertEqual(invalidPayload.keyword, undefined, "blank keyword parts are omitted");
assertEqual(invalidPayload.status, undefined, "blank status is omitted");
assertEqual(invalidPayload.orderType, 9, "explicit orderType is preserved when status is absent");

const listResult = normalizeInstallmentOrdersResponse({
  Items: [
    {
      OrderGuid: "order-1",
      BranchCode: "BNE01",
      BranchName: "Brisbane",
      OrderTime: "2026-05-10T11:00:00Z",
      Status: "4",
      SkuCount: "2",
      ItemCount: 3,
      TotalAmount: "120.50",
      DiscountAmount: "10",
      ActualAmount: 110.5,
    },
  ],
  Total: "9",
  PageNumber: "2",
  PageSize: "50",
});
assertEqual(listResult.items.length, 1, "list response keeps items");
assertEqual(listResult.items[0]?.orderGuid, "order-1", "list response normalizes guid");
assertEqual(listResult.items[0]?.status, 4, "list response normalizes numeric status");
assertEqual(listResult.items[0]?.totalAmount, 120.5, "list response normalizes total amount");
assertEqual(listResult.total, 9, "list response normalizes total");
assertEqual(listResult.pageNumber, 2, "list response normalizes page number");
assertEqual(listResult.pageSize, 50, "list response normalizes page size");

const detail = normalizeInstallmentOrderDetail({
  Order: {
    orderGuid: "order-2",
    branchCode: "SYD01",
    branchName: "Sydney",
    customerPhone: "0400000000",
    customerName: "Bob",
    orderTime: "2026-05-11T09:30:00Z",
    status: 4,
    actualAmount: "88.8",
  },
  OrderDetails: [
    {
      ProductCode: "P-001",
      ProductName: "Item One",
      Quantity: "2",
      UnitPrice: "30.25",
      DiscountAmount: "1.20",
      ActualAmount: "59.30",
    },
  ],
  PaymentDetails: [
    {
      PaymentGuid: "payment-1",
      OrderGuid: "order-2",
      PaymentTime: "2026-05-11T09:35:00Z",
      PaymentMethod: "3",
      PaymentMethodName: "Voucher",
      Amount: "20.00",
      Reference: "REF-001",
      CashierName: "Chris",
    },
  ],
});
assertEqual(detail.order?.orderGuid, "order-2", "detail normalizes nested order guid");
assertEqual(detail.orderDetails[0]?.quantity, 2, "detail normalizes line quantity");
assertEqual(detail.orderDetails[0]?.unitPrice, 30.25, "detail normalizes line unit price");
assertEqual(detail.paymentDetails[0]?.paymentMethod, 3, "detail normalizes payment method");
assertEqual(detail.paymentDetails[0]?.reference, "REF-001", "detail normalizes payment reference");
