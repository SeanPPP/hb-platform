import { readFileSync } from "node:fs";

import {
  buildInstallmentOrderListPayload,
  normalizeInstallmentOrderDetail,
  normalizeInstallmentOrdersResponse,
} from "./api";
import type { InstallmentOrderStatus } from "./types";

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
    status: undefined,
    customerPhone: undefined,
    customerName: undefined,
    pageNumber: 1,
    pageSize: 20,
  },
  "默认查询只发送专用分期筛选字段和分页参数"
);

const filteredPayload = buildInstallmentOrderListPayload({
  page: 3,
  pageSize: 50,
  filters: {
    startDate: " 2026-05-01 ",
    endDate: "2026-05-31",
    branchCode: " BNE01 ",
    status: 4,
    customerPhone: " 0412 345 678 ",
    customerName: " Alice ",
  },
});
assertDeepEqual(
  filteredPayload,
  {
    startDate: "2026-05-01",
    endDate: "2026-05-31",
    branchCode: "BNE01",
    status: 4,
    customerPhone: "0412 345 678",
    customerName: "Alice",
    pageNumber: 3,
    pageSize: 50,
  },
  "筛选条件分别保留客户电话和客户姓名"
);

const invalidPayload = buildInstallmentOrderListPayload({
  page: -1,
  pageSize: 77,
  filters: {
    status: 7 as InstallmentOrderStatus,
    customerPhone: "   ",
    customerName: "",
  },
});
assertEqual(invalidPayload.pageNumber, 1, "非法页码回退到第一页");
assertEqual(invalidPayload.pageSize, 20, "非法分页大小回退到 20");
assertEqual(invalidPayload.customerPhone, undefined, "空白客户电话不发送");
assertEqual(invalidPayload.customerName, undefined, "空白客户姓名不发送");
assertEqual(invalidPayload.status, undefined, "WPF 1 至 4 以外的状态不发送");

const lowerBoundaryStatusPayload = buildInstallmentOrderListPayload({ filters: { status: 1 } });
const upperBoundaryStatusPayload = buildInstallmentOrderListPayload({ filters: { status: 4 } });
const legacyStatusPayload = buildInstallmentOrderListPayload({
  filters: { status: 0 as InstallmentOrderStatus },
});
assertEqual(lowerBoundaryStatusPayload.status, 1, "WPF 状态下界 1 可发送");
assertEqual(upperBoundaryStatusPayload.status, 4, "WPF 状态上界 4 可发送");
assertEqual(legacyStatusPayload.status, undefined, "旧销售单状态 0 不发送");

const listResult = normalizeInstallmentOrdersResponse({
  Items: [
    {
      InstallmentGuid: "installment-1",
      InstallmentNumber: "INS-20260510-0001",
      StoreCode: "BNE01",
      StoreName: "Brisbane",
      DeviceCode: "POS-02",
      CashierName: "Chris",
      CustomerName: "Alice",
      CustomerPhone: "0412345678",
      CreatedAt: "2026-05-10T11:00:00Z",
      TotalAmount: "120.50",
      MinimumDownPayment: "24.10",
      DownPaymentAmount: 30,
      PaidAmount: "50.25",
      BalanceAmount: "70.25",
      Status: "1",
      UpdatedAt: "2026-05-11T11:00:00Z",
    },
  ],
  Total: "9",
  PageNumber: "2",
  PageSize: "50",
});
assertEqual(listResult.items.length, 1, "列表响应保留分期记录");
assertDeepEqual(
  listResult.items[0],
  {
    installmentGuid: "installment-1",
    installmentNumber: "INS-20260510-0001",
    storeCode: "BNE01",
    storeName: "Brisbane",
    cashierName: "Chris",
    customerName: "Alice",
    customerPhone: "0412345678",
    createdAt: "2026-05-10T11:00:00Z",
    totalAmount: 120.5,
    minimumDownPayment: 24.1,
    downPaymentAmount: 30,
    paidAmount: 50.25,
    balanceAmount: 70.25,
    status: 1,
    updatedAt: "2026-05-11T11:00:00Z",
  },
  "列表响应映射 WPF 分期核心字段"
);
assertEqual(
  Object.hasOwn(listResult.items[0] ?? {}, "deviceCode"),
  false,
  "列表摘要不得暴露仅详情使用的设备编码"
);
assertEqual(listResult.total, 9, "列表响应转换总数");
assertEqual(listResult.pageNumber, 2, "列表响应转换页码");
assertEqual(listResult.pageSize, 50, "列表响应转换分页大小");

const detail = normalizeInstallmentOrderDetail({
  Order: {
    InstallmentGuid: "installment-2",
    InstallmentNumber: "INS-20260511-0002",
    StoreCode: "SYD01",
    StoreName: "Sydney",
    DeviceCode: "POS-03",
    CashierName: "Jordan",
    CustomerPhone: "0400000000",
    CustomerName: "Bob",
    CreatedAt: "2026-05-11T09:30:00Z",
    Status: 2,
    TotalAmount: "88.80",
    MinimumDownPayment: "17.76",
    DownPaymentAmount: "20.00",
    PaidAmount: "88.80",
    BalanceAmount: "0",
    UpdatedAt: "2026-05-11T10:00:00Z",
    Note: "客户周五到店提货",
  },
  Lines: [
    {
      InstallmentLineGuid: "line-1",
      ProductCode: "P-001",
      ReferenceCode: "REF-P-001",
      DisplayName: "Item One",
      LookupCode: "9312345678901",
      Quantity: "1.25",
      UnitPrice: "30.25",
      DiscountAmount: "1.20",
      ActualAmount: "36.61",
      ItemNumber: "ITEM-001",
    },
  ],
  Payments: [
    {
      PaymentGuid: "payment-1",
      Method: "3",
      Amount: "20.00",
      Reference: "REF-001",
      Status: "1",
      RecordedAt: "2026-05-11T09:35:00Z",
      CashierId: "cashier-1",
      DeviceCode: "POS-03",
    },
    {
      PaymentGuid: "payment-voided",
      Method: "2",
      Amount: "10.00",
      Reference: "REF-VOIDED",
      Status: "2",
      RecordedAt: "2026-05-11T09:40:00Z",
      CashierId: "cashier-2",
      DeviceCode: "POS-04",
    },
  ],
  PickupInfo: {
    PickedUpAt: "2026-05-12T01:15:00Z",
    PickedUpBy: "cashier-pickup",
    PickupNote: "已核对客户证件",
  },
  CancellationInfo: {
    CancellationKind: "2",
    CancelledAt: "2026-05-12T02:30:00Z",
    CancelledBy: "manager-1",
    CancellationReason: "客户取消",
  },
});
assertEqual(detail.order?.installmentGuid, "installment-2", "详情映射分期主键");
assertEqual(detail.order?.deviceCode, "POS-03", "详情主单保留设备编码");
assertEqual(detail.order?.note, "客户周五到店提货", "详情映射主单备注");
assertEqual(detail.lines[0]?.quantity, 1.25, "详情保留小数商品数量");
assertEqual(detail.lines[0]?.referenceCode, "REF-P-001", "详情映射商品参考编码");
assertEqual(detail.lines[0]?.displayName, "Item One", "详情映射商品显示名称");
assertEqual(detail.lines[0]?.lookupCode, "9312345678901", "详情映射商品查询码");
assertEqual(detail.lines[0]?.itemNumber, "ITEM-001", "详情映射商品货号");
assertEqual(detail.lines[0]?.discountAmount, 1.2, "详情保留商品优惠金额供页面展示");
assertEqual(detail.payments.length, 2, "详情保留已记录和已作废付款");
assertEqual(detail.payments[0]?.method, 3, "详情映射分期付款方式");
assertEqual(detail.payments[0]?.amount, 20, "详情映射分期付款金额");
assertEqual(detail.payments[0]?.reference, "REF-001", "详情映射付款参考号");
assertEqual(detail.payments[0]?.status, 1, "详情映射付款状态");
assertEqual(detail.payments[0]?.recordedAt, "2026-05-11T09:35:00Z", "详情映射付款时间");
assertEqual(detail.payments[0]?.cashierId, "cashier-1", "详情映射付款收银员");
assertEqual(detail.payments[0]?.deviceCode, "POS-03", "详情映射付款设备");
assertEqual(detail.payments[1]?.status, 2, "详情明确区分作废付款状态");
assertDeepEqual(
  detail.pickupInfo,
  {
    pickedUpAt: "2026-05-12T01:15:00Z",
    pickedUpBy: "cashier-pickup",
    pickupNote: "已核对客户证件",
  },
  "详情映射提货时间、操作人和备注"
);
assertDeepEqual(
  detail.cancellationInfo,
  {
    cancellationKind: 2,
    cancelledAt: "2026-05-12T02:30:00Z",
    cancelledBy: "manager-1",
    cancellationReason: "客户取消",
  },
  "详情映射取消类型、原因、操作人和时间"
);

// 路由断言用于锁定 Expo 只访问与 WPF 同源的专用分期接口。
const apiSource = readFileSync(require.resolve("./api"), "utf8");
assertEqual(
  /const\s+BASE_PATH\s*=\s*["']\/react\/v1\/installment-orders["']/.test(apiSource),
  true,
  "Expo 使用专用分期接口路由"
);
