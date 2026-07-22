import assert from "node:assert/strict";
import { createIosReviewDataStore } from "./data-store";
import { IOS_REVIEW_STORES } from "./identity";
import { createIosReviewTransport } from "./transport";
import { resetIosReviewAppRouteState } from "./app-routes";
import { normalizeInvoiceGridResponse } from "../local-supplier-invoices/api";
import {
  normalizeSeasonalCardCatalogResponse,
  normalizeSeasonalCardSubmissionsResponse,
} from "../seasonal-cards/api";
import { normalizeDeviceManagementListResponse } from "../device-management/api";
import {
  normalizeProductBranchRows,
  normalizeSupplierBranchRows,
  normalizeSupplierRows,
} from "../product-report/api";
import { normalizeStatisticsFreshness } from "../reports/statistics-freshness";
import { normalizePromotionsResponse } from "../promotions/api";
import { normalizeWarehousePriceSyncResponse } from "../product-maintenance/warehouse-price-sync";
import { buildInstallmentOrderListPayload } from "../installment-orders/api";

type Method = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

async function run() {
  const dataStore = createIosReviewDataStore(
    new Date("2026-07-16T00:00:00.000Z"),
  );
  const transport = createIosReviewTransport(dataStore);

  const request = async (
    method: Method,
    url: string,
    body?: unknown,
    params?: Record<string, unknown>,
  ) =>
    (
      await transport.dispatch({
        method,
        url,
        data: body,
        params,
      })
    ).data as any;

  const menu = await request("GET", "/navigation/app-menu");
  assert.equal(menu.length, 19, "审核菜单必须覆盖全部 19 个业务入口");

  const stores = await request("GET", "/Users/guid/review-user/stores");
  assert.equal(stores.length, 3);
  assert.equal(
    stores.every((store: { storeGUID?: string }) => Boolean(store.storeGUID)),
    true,
    "演示门店必须携带稳定 storeGUID",
  );

  const preorderGate = await request(
    "GET",
    "/react/v1/preorders/active",
    undefined,
    { storeCode: "REV001" },
  );
  assert.deepEqual(preorderGate, {
    storeCode: "REV001",
    normalOrderBlocked: false,
    activations: [],
  });

  const accessRoles = await request(
    "GET",
    "/Users/guid/review-staff-001/roles",
  );
  assert.deepEqual(
    accessRoles.map((role: { roleName?: string }) => role.roleName),
    ["StoreStaff"],
    "审核模式必须覆盖账号角色读取",
  );
  const accessPermissionState = await request(
    "GET",
    "/Users/guid/review-staff-001/permissions/state",
  );
  assert.equal(accessPermissionState.implicitAllPermissions, false);
  assert.deepEqual(accessPermissionState.inheritedPermissionCodes, [
    "Users.View",
  ]);
  assert.ok(
    (await request("GET", "/Roles/active")).length > 0,
    "审核模式必须提供角色目录",
  );
  assert.ok(
    (await request("GET", "/Roles/permissions")).length > 0,
    "审核模式必须提供权限目录",
  );

  const readContracts: Array<{
    name: string;
    method: Method;
    path: string;
    body?: unknown;
    assertData(data: any): void;
  }> = [
    {
      name: "home products",
      method: "POST",
      path: "/react/v1/store-order/products",
      body: { storeCode: "REV001" },
      assertData: (data) =>
        assert.equal(data.items[0].barcode, "9330000000017"),
    },
    {
      name: "orders",
      method: "POST",
      path: "/react/v1/store-order/list",
      body: { storeCode: "REV001" },
      assertData: (data) => assert.ok(data.items.length > 0),
    },
    {
      name: "cart",
      method: "GET",
      path: "/react/v1/store-order/cart/REV001",
      assertData: (data) => assert.equal(data.storeCode, "REV001"),
    },
    {
      name: "warehouse",
      method: "GET",
      path: "/react/v1/product-warehouse/mobile/lookup?keyword=9330000000017",
      assertData: (data) => assert.equal(data[0].productCode, "REV-PROD-001"),
    },
    {
      name: "containers",
      method: "POST",
      path: "/react/v1/containers/list",
      body: {},
      assertData: (data) => assert.ok(data.items.length > 0),
    },
    {
      name: "domestic purchase",
      method: "GET",
      path: "/v1/domestic-product-creation/batches",
      assertData: (data) => assert.ok(data.items.length > 0),
    },
    {
      name: "local invoices",
      method: "POST",
      path: "/react/v1/local-supplier-invoices/grid",
      body: {},
      assertData: (data) => assert.ok(data.items.length > 0),
    },
    {
      name: "advertisements",
      method: "POST",
      path: "/react/v1/advertisements/grid",
      body: {},
      assertData: (data) => assert.ok(data.items.length > 0),
    },
    {
      name: "promotions",
      method: "POST",
      path: "/react/v1/promotions/store/grid",
      body: {},
      assertData: (data) => assert.ok(data.items.length > 0),
    },
    {
      name: "product maintenance",
      method: "POST",
      path: "/react/v1/store-product-maintenance/lookup",
      body: { keyword: "9330000000017" },
      assertData: (data) => assert.equal(data[0].barcode, "9330000000017"),
    },
    {
      name: "installment orders",
      method: "POST",
      path: "/react/v1/installment-orders/list",
      body: {},
      assertData: (data) => {
        assert.equal(data.items[0]?.installmentGuid, "review-installment-001");
        assert.equal(data.items[0]?.installmentNumber, "REV-INS-0001");
        assert.equal(data.items[0]?.balanceAmount, 0);
        assert.equal(Object.hasOwn(data.items[0], "deviceCode"), false);
        assert.equal(Object.hasOwn(data.items[0], "note"), false);
        assert.equal(Object.hasOwn(data.items[0], "pickupInfo"), false);
      },
    },
    {
      name: "installment order detail",
      method: "GET",
      path: "/react/v1/installment-orders/detail/review-installment-001",
      assertData: (data) => {
        assert.equal(data.order.installmentGuid, "review-installment-001");
        assert.equal(data.lines[0]?.quantity, 1.25);
        assert.equal(data.payments[0]?.method, 1);
        assert.equal(
          data.payments.some(
            (payment: { status?: number }) => payment.status === 2,
          ),
          true,
        );
        assert.equal(data.pickupInfo?.pickedUpBy, "App Review Demo");
      },
    },
    {
      name: "vouchers",
      method: "POST",
      path: "/react/v1/store-vouchers/list",
      body: {},
      assertData: (data) => assert.ok(data.items.length > 0),
    },
    {
      name: "seasonal cards",
      method: "GET",
      path: "/react/v1/seasonal-card-remaining/catalog",
      assertData: (data) => assert.ok(data.length > 0),
    },
    {
      name: "attendance personal",
      method: "GET",
      path: "/react/v1/attendance/my/today?storeCode=REV001",
      assertData: (data) =>
        assert.equal(data.storeTimeZone, "Australia/Brisbane"),
    },
    {
      name: "attendance management",
      method: "GET",
      path: "/react/v1/attendance/schedules/week?storeCode=REV001",
      assertData: (data) => assert.ok(data.length > 0),
    },
    {
      name: "users",
      method: "POST",
      path: "/react/v1/store-users/grid",
      body: { storeCode: "REV001" },
      assertData: (data) => assert.ok(data.items.length > 0),
    },
    {
      name: "employee profile",
      method: "GET",
      path: "/EmployeeProfiles/me",
      assertData: (data) => assert.equal(data.username, "ios_app_review"),
    },
    {
      name: "device management",
      method: "GET",
      path: "/mobile/device-management/paged",
      assertData: (data) => assert.ok(data.devices.length > 0),
    },
    {
      name: "reports",
      method: "GET",
      path: "/react/v1/dashboard/executive-branch-performance",
      assertData: (data) => assert.ok(data.items.length > 0),
    },
  ];

  for (const contract of readContracts) {
    const data = await request(contract.method, contract.path, contract.body);
    contract.assertData(data);
  }

  const filteredInstallments = await request(
    "POST",
    "/react/v1/installment-orders/list",
    buildInstallmentOrderListPayload({
      page: 1,
      pageSize: 20,
      filters: {
        branchCode: "REV001",
        status: 3,
        customerName: "Demo Customer",
        customerPhone: "0400",
        startDate: "2026-07-16",
        endDate: "2026-07-16",
      },
    }),
  );
  assert.equal(
    filteredInstallments.total,
    2,
    "Brisbane 本地自然日必须包含前一 UTC 日 23:30 的分期",
  );
  assert.equal(filteredInstallments.items.length, 2);
  assert.equal(filteredInstallments.pageNumber, 1);
  assert.equal(filteredInstallments.pageSize, 20);
  assert.deepEqual(
    filteredInstallments.items.map(
      (item: { installmentGuid?: string }) => item.installmentGuid,
    ),
    ["review-installment-001", "review-installment-002"],
    "筛选结果顺序必须稳定",
  );

  const rejectedInstallmentFilters: Array<{
    label: string;
    body: Record<string, unknown>;
  }> = [
    { label: "分店", body: { branchCode: "REV999" } },
    { label: "状态", body: { status: 2 } },
    { label: "客户姓名", body: { customerName: "不存在客户" } },
    { label: "客户电话", body: { customerPhone: "0499999999" } },
    { label: "开始日期", body: { startDate: "2026-07-17" } },
    { label: "结束日期", body: { endDate: "2026-07-15" } },
  ];
  for (const filter of rejectedInstallmentFilters) {
    const result = await request(
      "POST",
      "/react/v1/installment-orders/list",
      filter.body,
    );
    assert.equal(result.total, 0, `分期审核列表必须执行${filter.label}筛选`);
    assert.equal(result.items.length, 0);
  }

  const secondInstallmentPage = await request(
    "POST",
    "/react/v1/installment-orders/list",
    buildInstallmentOrderListPayload({ page: 2, pageSize: 20 }),
  );
  assert.equal(secondInstallmentPage.total, 2, "分页后总数仍为筛选结果总数");
  assert.equal(
    secondInstallmentPage.items.length,
    0,
    "超出末页不得重复返回分期",
  );
  assert.equal(secondInstallmentPage.pageNumber, 2);
  assert.equal(secondInstallmentPage.pageSize, 20);

  const firstSingleInstallmentPage = await request(
    "POST",
    "/react/v1/installment-orders/list",
    { ...buildInstallmentOrderListPayload({ page: 1 }), pageSize: 1 },
  );
  const secondSingleInstallmentPage = await request(
    "POST",
    "/react/v1/installment-orders/list",
    { ...buildInstallmentOrderListPayload({ page: 2 }), pageSize: 1 },
  );
  assert.equal(
    firstSingleInstallmentPage.items[0]?.installmentGuid,
    "review-installment-001",
  );
  assert.equal(
    secondSingleInstallmentPage.items[0]?.installmentGuid,
    "review-installment-002",
  );

  const installmentDetail = await request(
    "GET",
    "/react/v1/installment-orders/detail/review-installment-001",
  );
  assert.equal(
    Object.hasOwn(installmentDetail.order, "lines"),
    false,
    "详情主单不得重复嵌套商品行",
  );
  assert.equal(
    Object.hasOwn(installmentDetail.order, "payments"),
    false,
    "详情主单不得重复嵌套付款",
  );

  const normalizedInvoices = normalizeInvoiceGridResponse(
    await request("POST", "/react/v1/local-supplier-invoices/grid", {}),
  );
  assert.equal(
    normalizedInvoices.items[0]?.invoiceNo,
    "REV-INV-0001",
    "离线发票 fixture 必须符合现有业务 normalizer 契约",
  );

  const seasonalCatalogRaw = await request(
    "GET",
    "/react/v1/seasonal-card-remaining/catalog",
  );
  assert.equal(seasonalCatalogRaw[0]?.cardType, 1);
  assert.equal(seasonalCatalogRaw[0]?.priceOption, 1);
  const seasonalCatalog =
    normalizeSeasonalCardCatalogResponse(seasonalCatalogRaw);
  assert.equal(seasonalCatalog[0]?.cardType, 1);
  assert.equal(
    seasonalCatalog[0]?.priceOption,
    1,
    "季节卡 fixture 必须使用业务 normalizer 接受的数字枚举",
  );
  const seasonalSubmissions = normalizeSeasonalCardSubmissionsResponse(
    await request("GET", "/react/v1/seasonal-card-remaining/submissions"),
  );
  assert.equal(seasonalSubmissions.items[0]?.cardType, 1);

  const secondStoreSchedule = await request(
    "POST",
    "/react/v1/attendance/schedules",
    {
      storeCode: "REV002",
      userGuid: "review-user",
      workDate: "2026-07-16",
      startTime: "09:00",
      endTime: "17:00",
      status: "Active",
    },
  );
  const secondStoreToday = await request(
    "GET",
    "/react/v1/attendance/my/today?storeCode=REV002&workDate=2026-07-16",
  );
  assert.equal(secondStoreToday.workDate, "2026-07-16");
  assert.equal(secondStoreToday.schedules.length, 1);
  assert.equal(secondStoreToday.schedules[0]?.storeCode, "REV002");
  assert.equal(secondStoreToday.punches.length, 0);
  assert.equal(secondStoreToday.holidays.length, 0);

  const historicalToday = await request(
    "GET",
    "/react/v1/attendance/my/today?storeCode=REV001&workDate=2026-07-15",
  );
  assert.equal(historicalToday.workDate, "2026-07-15");
  assert.equal(historicalToday.schedules.length, 0, "历史日期不能泄漏当天排班");
  assert.equal(historicalToday.punches.length, 0, "历史日期不能泄漏当天打卡");
  assert.equal(historicalToday.holidays.length, 0, "历史日期不能泄漏当天节假日");

  const outOnlyPayload = {
    storeCode: "REV001",
    scheduleGuid: "review-schedule-001",
    punchType: "ClockOut",
    requestedPunchTimeLocal: "2026-07-16T09:00:00",
    reason: "Invalid out-only review fixture",
  };
  const outOnlyPreview = await request(
    "POST",
    "/react/v1/attendance/my/punch-adjustments/preview",
    outOnlyPayload,
  );
  assert.equal(outOnlyPreview.isValid, false);
  assert.equal(outOnlyPreview.validationErrorCode, "PUNCH_SEQUENCE_OUT_WITHOUT_IN");
  await assert.rejects(
    () => request(
      "POST",
      "/react/v1/attendance/my/punch-adjustments",
      outOnlyPayload,
    ),
    /PUNCH_SEQUENCE_OUT_WITHOUT_IN/,
    "create 不能绕过 preview 的 out-only 校验",
  );

  const utcPriorityPreview = await request(
    "POST",
    "/react/v1/attendance/my/punch-adjustments/preview",
    {
      storeCode: "REV001",
      scheduleGuid: "review-schedule-001",
      punchType: "ClockIn",
      requestedPunchTimeLocal: "2026-07-16T09:00:00",
      // 故意与 local 的 Brisbane 推导值不同：Review mock 必须把 UTC 作为权威 instant。
      requestedPunchTimeUtc: "2026-07-16T01:00:00Z",
      reason: "UTC priority review fixture",
    },
  );
  assert.equal(utcPriorityPreview.isValid, true);
  assert.equal(
    utcPriorityPreview.proposedSession.segments[0]?.clockIn?.punchTimeUtc,
    "2026-07-16T01:00:00Z",
    "iOS Review mock 必须优先使用 requestedPunchTimeUtc，而不是重新从 local 推导 instant",
  );

  let todayAttendance = await request(
    "GET",
    "/react/v1/attendance/my/today?storeCode=REV001",
  );
  assert.deepEqual(
    {
      punchCount: todayAttendance.punches.length,
      nextPunchType: todayAttendance.nextPunchType,
      canClockIn: todayAttendance.canClockIn,
      canClockOut: todayAttendance.canClockOut,
    },
    {
      punchCount: 0,
      nextPunchType: "ClockIn",
      canClockIn: true,
      canClockOut: false,
    },
  );
  await request("POST", "/react/v1/attendance/punch", {
    storeCode: "REV001",
    punchType: "ClockIn",
  });
  todayAttendance = await request(
    "GET",
    "/react/v1/attendance/my/today?storeCode=REV001",
  );
  assert.deepEqual(
    {
      punchCount: todayAttendance.punches.length,
      nextPunchType: todayAttendance.nextPunchType,
      canClockIn: todayAttendance.canClockIn,
      canClockOut: todayAttendance.canClockOut,
    },
    {
      punchCount: 1,
      nextPunchType: "ClockOut",
      canClockIn: false,
      canClockOut: true,
    },
    "ClockIn 后 refetch 必须显示已打卡并允许 ClockOut",
  );
  const consecutiveClockInPreview = await request(
    "POST",
    "/react/v1/attendance/my/punch-adjustments/preview",
    {
      storeCode: "REV001",
      scheduleGuid: "review-schedule-001",
      punchType: "ClockIn",
      requestedPunchTimeLocal: "2026-07-16T10:15:00",
      reason: "Invalid consecutive clock-in",
    },
  );
  assert.equal(consecutiveClockInPreview.isValid, false);
  assert.equal(
    consecutiveClockInPreview.validationErrorCode,
    "PUNCH_SEQUENCE_CONSECUTIVE_TYPE",
  );
  await request("POST", "/react/v1/attendance/punch", {
    storeCode: "REV001",
    punchType: "ClockOut",
  });
  todayAttendance = await request(
    "GET",
    "/react/v1/attendance/my/today?storeCode=REV001",
  );
  assert.equal(todayAttendance.punches.length, 2);
  assert.deepEqual(
    {
      nextPunchType: todayAttendance.nextPunchType,
      canClockIn: todayAttendance.canClockIn,
      canClockOut: todayAttendance.canClockOut,
    },
    {
      nextPunchType: "ClockIn",
      canClockIn: true,
      canClockOut: false,
    },
    "第一段完成后必须允许继续第二段 ClockIn",
  );

  const originalClockOut = todayAttendance.punches.find(
    (punch: { punchType?: string }) => punch.punchType === "ClockOut",
  );
  const adjustmentPayload = {
    storeCode: "REV001",
    scheduleGuid: "review-schedule-001",
    originalPunchGuid: originalClockOut.punchGuid,
    punchType: "ClockOut",
    requestedPunchTimeLocal: "2026-07-16T17:00:00",
    reason: "App Review correction",
  };
  const adjustmentPreview = await request(
    "POST",
    "/react/v1/attendance/my/punch-adjustments/preview",
    adjustmentPayload,
  );
  assert.equal(adjustmentPreview.isValid, true);
  assert.equal(adjustmentPreview.wouldAutoApprove, true);
  assert.equal(adjustmentPreview.existingSession.scheduleGuid, "review-schedule-001");
  assert.equal(adjustmentPreview.existingSession.segmentLimit, 3);
  assert.equal(adjustmentPreview.proposedSession.segmentLimit, 3);
  assert.equal(adjustmentPreview.proposedSession.segments[0].clockOut.punchTimeLocal, "2026-07-16T17:00:00");
  assert.equal(adjustmentPreview.existingSession.workedMinutes, 0);
  assert.equal(adjustmentPreview.proposedSession.workedMinutes, 420);
  assert.equal(adjustmentPreview.workedMinutesDelta, 420);
  assert.equal(adjustmentPreview.candidateOvertimeMinutesDelta, 0);

  const createdAdjustment = await request(
    "POST",
    "/react/v1/attendance/my/punch-adjustments",
    adjustmentPayload,
  );
  assert.equal(createdAdjustment.status, "Applied");
  assert.equal(createdAdjustment.originalPunchGuid, originalClockOut.punchGuid);

  const adjustmentRows = await request(
    "GET",
    "/react/v1/attendance/my/punch-adjustments",
  );
  assert.equal(adjustmentRows.length, 1, "补卡提交后必须保存在 Review route state");
  assert.equal(adjustmentRows[0].adjustmentGuid, createdAdjustment.adjustmentGuid);

  todayAttendance = await request(
    "GET",
    "/react/v1/attendance/my/today?storeCode=REV001",
  );
  assert.equal(todayAttendance.punches.length, 2, "修改原打卡不能同时保留旧卡造成重复班段");
  assert.equal(
    todayAttendance.punches.find(
      (punch: { punchType?: string }) => punch.punchType === "ClockOut",
    )?.punchTimeLocal,
    "2026-07-16T17:00:00",
    "补卡提交后的 Today refetch 必须立即反映修正时间",
  );
  assert.equal(todayAttendance.canClockIn, true);
  assert.equal(todayAttendance.canClockOut, false);

  const createMissingPunch = async (
    punchType: "ClockIn" | "ClockOut",
    requestedPunchTimeLocal: string,
  ) => {
    const payload = {
      storeCode: "REV001",
      scheduleGuid: "review-schedule-001",
      punchType,
      requestedPunchTimeLocal,
      reason: `Create review ${punchType}`,
    };
    const preview = await request(
      "POST",
      "/react/v1/attendance/my/punch-adjustments/preview",
      payload,
    );
    assert.equal(preview.isValid, true, `${requestedPunchTimeLocal} 应属于店长允许的三段内`);
    return request(
      "POST",
      "/react/v1/attendance/my/punch-adjustments",
      payload,
    );
  };
  await createMissingPunch("ClockIn", "2026-07-16T17:15:00");
  await createMissingPunch("ClockOut", "2026-07-16T17:30:00");
  await createMissingPunch("ClockIn", "2026-07-16T17:45:00");
  await createMissingPunch("ClockOut", "2026-07-16T18:00:00");
  todayAttendance = await request(
    "GET",
    "/react/v1/attendance/my/today?storeCode=REV001&workDate=2026-07-16",
  );
  assert.deepEqual(
    {
      nextPunchType: todayAttendance.nextPunchType,
      canClockIn: todayAttendance.canClockIn,
      canClockOut: todayAttendance.canClockOut,
    },
    {
      nextPunchType: "ClockIn",
      canClockIn: false,
      canClockOut: false,
    },
    "第三段完成后必须关闭继续 ClockIn",
  );

  const fourthSegmentPayload = {
    storeCode: "REV001",
    scheduleGuid: "review-schedule-001",
    punchType: "ClockIn",
    requestedPunchTimeLocal: "2026-07-16T18:15:00",
    reason: "Fourth manager segment must fail",
  };
  const fourthSegmentPreview = await request(
    "POST",
    "/react/v1/attendance/my/punch-adjustments/preview",
    fourthSegmentPayload,
  );
  assert.equal(fourthSegmentPreview.isValid, false);
  assert.equal(
    fourthSegmentPreview.validationErrorCode,
    "PUNCH_SEGMENT_LIMIT_EXCEEDED",
  );
  await assert.rejects(
    () => request(
      "POST",
      "/react/v1/attendance/my/punch-adjustments",
      fourthSegmentPayload,
    ),
    /PUNCH_SEGMENT_LIMIT_EXCEEDED/,
    "create 不能绕过店长每日每店三段上限",
  );

  const secondStorePreview = await request(
    "POST",
    "/react/v1/attendance/my/punch-adjustments/preview",
    {
      storeCode: "REV002",
      scheduleGuid: secondStoreSchedule.scheduleGuid,
      punchType: "ClockIn",
      requestedPunchTimeLocal: "2026-07-16T09:00:00",
      reason: "Cross-store independent segment count",
    },
  );
  assert.equal(secondStorePreview.isValid, true, "REV001 三段不能占用 REV002 的独立上限");

  const getNormalizedDevices = async () =>
    normalizeDeviceManagementListResponse(
      await request("GET", "/mobile/device-management/paged"),
    ).devices;
  assert.equal((await getNormalizedDevices())[0]?.status, 1);
  await request("POST", "/mobile/device-management/review-device-001/disable");
  assert.equal((await getNormalizedDevices())[0]?.status, 0);
  await request("POST", "/mobile/device-management/review-device-001/lock");
  assert.equal((await getNormalizedDevices())[0]?.status, 2);
  await request("POST", "/mobile/device-management/review-device-001/activate");
  assert.equal((await getNormalizedDevices())[0]?.status, 1);

  const supplierRows = normalizeSupplierRows(
    await request("GET", "/react/v1/dashboard/supplier-sales-rank"),
  );
  assert.equal(supplierRows[0]?.revenue, 1250);
  assert.equal(supplierRows[0]?.totalQuantity, 36);
  assert.equal(supplierRows[0]?.orderCount, 8);

  const freshness = normalizeStatisticsFreshness(
    await request("GET", "/react/v1/dashboard/statistics-freshness"),
  );
  assert.ok(freshness.lastSuccessfulAtUtc);
  assert.equal(freshness.latestRunStatus, "Success");

  const promotions = normalizePromotionsResponse(
    await request("POST", "/react/v1/promotions/store/grid", {}),
  );
  assert.equal(
    promotions.items[0]?.scopeType,
    "StoreOnly",
    "分店促销 fixture 必须使用页面可识别的 StoreOnly scope",
  );

  const getProductDetail = () =>
    request(
      "GET",
      "/react/v1/store-product-maintenance/REV-PROD-001?storeCode=REV001&includeCodes=true",
    );
  let productDetail = await getProductDetail();
  assert.equal(
    productDetail.storePrice.uuid,
    "review-store-price-REV-PROD-001",
  );
  assert.equal(productDetail.setCodes.length, 1);
  assert.equal(productDetail.multiCodes.length, 1);

  await request(
    "PUT",
    `/react/v1/store-product-maintenance/store-prices/${productDetail.storePrice.uuid}`,
    { retailPrice: 13.75, purchasePrice: 5.5 },
  );
  productDetail = await getProductDetail();
  assert.equal(productDetail.storePrice.retailPrice, 13.75);
  assert.equal(productDetail.storePrice.purchasePrice, 5.5);

  const syncResult = normalizeWarehousePriceSyncResponse(
    await request(
      "POST",
      `/react/v1/store-product-maintenance/store-prices/${productDetail.storePrice.uuid}/sync-warehouse`,
      {
        confirmRetailPrice: true,
        expectedWarehousePurchasePrice: 5.8,
        expectedWarehouseRetailPrice: 14.5,
      },
    ),
  );
  assert.equal(syncResult.status, "synced");
  productDetail = await getProductDetail();
  assert.equal(productDetail.storePrice.purchasePrice, 5.8);
  assert.equal(productDetail.storePrice.retailPrice, 14.5);

  await request(
    "PUT",
    "/react/v1/store-product-maintenance/products/REV-PROD-001/clearance-price",
    { storeCode: "REV001", clearancePrice: 9.25 },
  );
  productDetail = await getProductDetail();
  assert.equal(productDetail.clearancePrice.clearancePrice, 9.25);

  const createdSetCode = await request(
    "POST",
    "/react/v1/store-product-maintenance/set-codes",
    {
      productCode: "REV-PROD-001",
      storeCode: "REV001",
      productType: 1,
      barcode: "9330000000093",
      retailPrice: 18,
      isActive: true,
    },
  );
  const createdMultiCode = await request(
    "POST",
    "/react/v1/store-product-maintenance/set-codes",
    {
      productCode: "REV-PROD-001",
      storeCode: "REV001",
      productType: 2,
      barcode: "9330000000079",
      retailPrice: 16.5,
      isActive: true,
    },
  );
  assert.ok(createdMultiCode.uuid, "productType=2 必须返回 multi code uuid");
  const multiCodePage = await request(
    "GET",
    "/react/v1/store-product-maintenance/REV-PROD-001/codes?productType=2&page=1&pageSize=50",
  );
  assert.equal(
    multiCodePage.items.some(
      (item: { uuid: string; barcode: string }) =>
        item.uuid === createdMultiCode.uuid && item.barcode === "9330000000079",
    ),
    true,
    "productType=2 创建后必须在 Multi Code refetch 中可见",
  );
  let setCodePage = await request(
    "GET",
    "/react/v1/store-product-maintenance/REV-PROD-001/codes?type=1&page=1&pageSize=50",
  );
  assert.equal(
    setCodePage.items.some(
      (item: { setCodeId: string }) =>
        item.setCodeId === createdSetCode.setCodeId,
    ),
    true,
  );
  await request(
    "PUT",
    `/react/v1/store-product-maintenance/set-codes/${createdSetCode.setCodeId}`,
    { storeCode: "REV001", barcode: "9330000000086", retailPrice: 19 },
  );
  setCodePage = await request(
    "GET",
    "/react/v1/store-product-maintenance/REV-PROD-001/codes?type=1&page=1&pageSize=50",
  );
  assert.equal(
    setCodePage.items.find(
      (item: { setCodeId: string }) =>
        item.setCodeId === createdSetCode.setCodeId,
    )?.setBarcode,
    "9330000000086",
  );
  await request(
    "DELETE",
    `/react/v1/store-product-maintenance/set-codes/${createdSetCode.setCodeId}`,
  );
  setCodePage = await request(
    "GET",
    "/react/v1/store-product-maintenance/REV-PROD-001/codes?type=1&page=1&pageSize=50",
  );
  assert.equal(
    setCodePage.items.some(
      (item: { setCodeId: string }) =>
        item.setCodeId === createdSetCode.setCodeId,
    ),
    false,
  );

  await request(
    "PUT",
    "/react/v1/store-product-maintenance/set-codes/review-multi-001",
    { retailPrice: 11.25, isActive: true },
  );
  const updatedMultiCodePage = await request(
    "GET",
    "/react/v1/store-product-maintenance/REV-PROD-001/codes?type=2&page=1&pageSize=50",
  );
  assert.equal(
    updatedMultiCodePage.items.find(
      (item: { uuid: string }) => item.uuid === "review-multi-001",
    )?.retailPrice,
    11.25,
  );

  const createdLeave = await request(
    "POST",
    "/react/v1/attendance/my/leave-requests",
    {
      storeCode: "REV001",
      leaveType: "AnnualLeave",
      startDate: "2026-07-20",
      endDate: "2026-07-20",
      reason: "Review approval flow",
    },
  );
  let pendingApprovals = await request(
    "GET",
    "/react/v1/attendance/approvals/pending?storeCode=REV001",
  );
  const createdApproval = pendingApprovals.find(
    (item: { sourceGuid: string }) =>
      item.sourceGuid === createdLeave.leaveGuid,
  );
  assert.ok(createdApproval, "创建请假后管理端必须立即出现待审批项");
  await request(
    "POST",
    `/react/v1/attendance/approvals/${createdApproval.approvalGuid}/approve`,
    { reviewRemark: "Approved in demo" },
  );
  let leaveRequests = await request(
    "GET",
    "/react/v1/attendance/my/leave-requests",
  );
  assert.equal(
    leaveRequests.find(
      (item: { leaveGuid: string }) =>
        item.leaveGuid === createdLeave.leaveGuid,
    )?.status,
    "Approved",
  );

  const rejectedLeave = await request(
    "POST",
    "/react/v1/attendance/managed/leave-requests",
    {
      userGuid: "review-staff-001",
      storeCode: "REV001",
      leaveType: "PersonalLeave",
      startDate: "2026-07-21",
      endDate: "2026-07-21",
      reason: "Review reject flow",
    },
  );
  pendingApprovals = await request(
    "GET",
    "/react/v1/attendance/approvals/pending?storeCode=REV001",
  );
  const rejectedApproval = pendingApprovals.find(
    (item: { sourceGuid: string }) =>
      item.sourceGuid === rejectedLeave.leaveGuid,
  );
  assert.ok(rejectedApproval);
  await request(
    "POST",
    `/react/v1/attendance/approvals/${rejectedApproval.approvalGuid}/reject`,
    { reviewRemark: "Rejected in demo" },
  );
  leaveRequests = await request(
    "GET",
    "/react/v1/attendance/my/leave-requests",
  );
  assert.equal(
    leaveRequests.find(
      (item: { leaveGuid: string }) =>
        item.leaveGuid === rejectedLeave.leaveGuid,
    )?.status,
    "Rejected",
  );

  const supplierBranchRows = normalizeSupplierBranchRows(
    await request("GET", "/react/v1/dashboard/supplier-store-sales"),
  );
  assert.equal(supplierBranchRows[0]?.supplierCode, "REV-SUP-001");
  assert.equal(supplierBranchRows[0]?.revenue, 720);
  assert.equal(supplierBranchRows[0]?.totalQuantity, 24);
  assert.equal(supplierBranchRows[0]?.orderCount, 6);

  const productBranchRows = normalizeProductBranchRows(
    await request("GET", "/react/v1/dashboard/product-sales-by-branches"),
  );
  assert.equal(productBranchRows[0]?.quantity, 12);
  assert.equal(productBranchRows[0]?.salesAmount, 240);

  const scan = await request(
    "POST",
    "/react/v1/store-order/products/scan-lookup",
    { barcode: "9330000000017", storeCode: "REV001" },
  );
  assert.equal(scan.items[0].productCode, "REV-PROD-001");

  const scanAdd = await request(
    "POST",
    "/react/v1/store-order/cart/scan-lookup-add",
    { barcode: "9330000000017", storeCode: "REV002" },
  );
  assert.equal(scanAdd.added, true);
  assert.equal(scanAdd.items[0].productCode, "REV-PROD-001");

  await request("POST", "/react/v1/store-order/cart/add", {
    storeCode: "REV001",
    productCode: "REV-PROD-002",
    quantity: 2,
  });
  let cart = await request("GET", "/react/v1/store-order/cart/REV001");
  assert.equal(
    cart.items.some(
      (item: { productCode: string; quantity: number }) =>
        item.productCode === "REV-PROD-002" && item.quantity === 2,
    ),
    true,
    "购物车写入必须在后续读取中立即可见",
  );

  await request("POST", "/react/v1/store-order/cart/update", {
    storeCode: "REV001",
    productCode: "REV-PROD-002",
    quantity: 5,
  });
  cart = await request("GET", "/react/v1/store-order/cart/REV001");
  assert.equal(
    cart.items.find(
      (item: { productCode: string }) => item.productCode === "REV-PROD-002",
    ).quantity,
    5,
  );

  const createdSchedule = await request(
    "POST",
    "/react/v1/attendance/schedules",
    {
      storeCode: "REV001",
      userGuid: "review-user",
      workDate: "2026-07-17",
      startTime: "09:00",
      endTime: "17:00",
    },
  );
  assert.equal(createdSchedule.storeCode, "REV001");
  const updatedSchedule = await request(
    "PUT",
    `/react/v1/attendance/schedules/${createdSchedule.scheduleGuid}`,
    { status: "Active" },
  );
  assert.equal(updatedSchedule.status, "Active");
  await request(
    "DELETE",
    `/react/v1/attendance/schedules/${createdSchedule.scheduleGuid}`,
  );

  const permissionsPath =
    "/Users/guid/review-user/stores/review-store/pos-terminal-permissions";
  const permissions = await request("PUT", permissionsPath, {
    grantedPermissionCodes: ["PosTerminal.Sales.AddItem"],
  });
  assert.deepEqual(permissions.grantedPermissionCodes, [
    "PosTerminal.Sales.AddItem",
  ]);

  await request("POST", "/Users/guid/review-staff-001/stores", [
    {
      StoreGUID: IOS_REVIEW_STORES[1].storeGUID,
      IsPrimary: true,
    },
  ]);
  const updatedAccessStores = await request(
    "GET",
    "/Users/guid/review-staff-001/stores",
  );
  assert.equal(
    updatedAccessStores[0]?.storeGUID,
    IOS_REVIEW_STORES[1].storeGUID,
  );
  assert.equal(updatedAccessStores[0]?.isPrimary, true);

  await request("POST", "/Users/guid/review-staff-001/roles", {
    RoleGuids: ["review-role-store-staff"],
  });
  await request("POST", "/Users/guid/review-staff-001/permissions", {
    permissions: ["PosTerminal.Sales.AddItem"],
  });
  const updatedPermissionState = await request(
    "GET",
    "/Users/guid/review-staff-001/permissions/state",
  );
  assert.deepEqual(updatedPermissionState.directPermissionCodes, [
    "PosTerminal.Sales.AddItem",
  ]);

  const profile = await request("PUT", "/EmployeeProfiles/me", {
    displayName: "Updated Demo Reviewer",
  });
  assert.equal(profile.displayName, "Updated Demo Reviewer");

  const exportData = await request(
    "POST",
    "/react/v1/containers/review-container/products/export",
    { format: "xlsx" },
  );
  assert.equal(
    exportData instanceof ArrayBuffer,
    true,
    "导出必须返回二进制数据",
  );

  const productMirrorCount = dataStore.list("carts").length;
  assert.ok(productMirrorCount > 1, "代表性写操作必须同步到 ReviewDataStore");

  const overtimeDataStore = createIosReviewDataStore(
    new Date("2026-07-16T00:00:00.000Z"),
  );
  const overtimeTransport = createIosReviewTransport(overtimeDataStore);
  const overtimeRequest = async (method: Method, url: string, body?: unknown) =>
    (await overtimeTransport.dispatch({ method, url, data: body })).data as any;
  const earlyClockIn = await overtimeRequest(
    "POST",
    "/react/v1/attendance/my/punch-adjustments",
    {
      storeCode: "REV001",
      scheduleGuid: "review-schedule-001",
      punchType: "ClockIn",
      requestedPunchTimeLocal: "2000-01-01T00:00:00",
      requestedPunchTimeUtc: "2026-07-15T22:30:00Z",
      reason: "REV001 store local 08:30",
    },
  );
  assert.equal(earlyClockIn.status, "Applied");
  const overtimePreview = await overtimeRequest(
    "POST",
    "/react/v1/attendance/my/punch-adjustments/preview",
    {
      storeCode: "REV001",
      scheduleGuid: "review-schedule-001",
      punchType: "ClockOut",
      requestedPunchTimeLocal: "2000-01-01T00:00:00",
      requestedPunchTimeUtc: "2026-07-16T08:00:00Z",
      reason: "REV001 store local 18:00",
    },
  );
  assert.equal(overtimePreview.isValid, true);
  assert.equal(overtimePreview.proposedSession.earlyOvertimeMinutes, 30);
  assert.equal(overtimePreview.proposedSession.lateOvertimeMinutes, 60);
  assert.equal(overtimePreview.proposedSession.candidateOvertimeMinutes, 90,
    "REV001 的 08:30/18:00 必须按 Brisbane 门店本地时间相对 09:00-17:00 计算加班");

  resetIosReviewAppRouteState(dataStore);
  const resetCart = await request("GET", "/react/v1/store-order/cart/REV001");
  assert.equal(
    resetCart.items.some(
      (item: { productCode: string }) => item.productCode === "REV-PROD-002",
    ),
    false,
    "退出或重启时必须恢复路由 fixture 初始快照",
  );
  assert.equal(
    (await request("GET", "/react/v1/attendance/my/punch-adjustments")).length,
    0,
    "退出或重启时必须清除 Review 内存中的补卡记录",
  );

  await assert.rejects(
    () => request("GET", "/react/v1/not-a-real-endpoint"),
    /IOS_REVIEW_UNHANDLED_REQUEST/,
    "宽泛 prefix 路由不能吞掉未登记 endpoint",
  );

  console.log("app-routes.test.ts: ok");
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
