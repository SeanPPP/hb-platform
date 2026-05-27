import { buildAccess, PERMISSIONS } from "./access";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function createUser(
  permissions: string[],
  roleNames = ["User"]
): NonNullable<Parameters<typeof buildAccess>[0]> {
  return {
    userGuid: "user-1",
    userGUID: "user-1",
    username: "tester",
    email: "tester@example.com",
    permissions,
    roleNames,
    storeNames: [],
    stores: [],
  };
}

const purchaseLegacyAccess = buildAccess(
  createUser(["LocalInvocie.View", "LocalInvocie.Edit"])
);

assertEqual(
  purchaseLegacyAccess.hasPermission(PERMISSIONS.LocalPurchase.View),
  true,
  "legacy LocalInvocie.View aliases to LocalPurchase.View"
);

assertEqual(
  purchaseLegacyAccess.hasPermission(PERMISSIONS.LocalPurchase.Edit),
  true,
  "legacy LocalInvocie.Edit aliases to LocalPurchase.Edit"
);

assertEqual(
  purchaseLegacyAccess.canViewLocalPurchase,
  true,
  "legacy LocalInvocie.View enables local purchase view capability"
);

assertEqual(
  purchaseLegacyAccess.canEditLocalPurchase,
  true,
  "legacy LocalInvocie.Edit enables local purchase edit capability"
);

const deviceViewerAccess = buildAccess(
  createUser([PERMISSIONS.DeviceRegistration.View])
);

assertEqual(
  deviceViewerAccess.canViewDeviceRegistration,
  true,
  "DeviceRegistration.View enables device management entrance capability"
);

assertEqual(
  deviceViewerAccess.canManageDeviceRegistration,
  false,
  "DeviceRegistration.View does not enable device management action capability"
);

const deviceManagerAccess = buildAccess(
  createUser([PERMISSIONS.DeviceRegistration.Manage])
);

assertEqual(
  deviceManagerAccess.canViewDeviceRegistration,
  true,
  "DeviceRegistration.Manage implies device management entrance capability"
);

assertEqual(
  deviceManagerAccess.canManageDeviceRegistration,
  true,
  "DeviceRegistration.Manage enables device management action capability"
);

const attendancePersonalAccess = buildAccess(
  createUser([PERMISSIONS.Attendance.ScheduleViewSelf])
);

assertEqual(
  attendancePersonalAccess.canViewAttendancePersonal,
  true,
  "Attendance.Schedule.ViewSelf enables personal attendance capability"
);

assertEqual(
  attendancePersonalAccess.canViewAttendanceManagement,
  false,
  "personal attendance permission does not enable management attendance capability"
);

const attendanceManagerAccess = buildAccess(
  createUser([PERMISSIONS.Attendance.ApprovalReviewManagedStore])
);

assertEqual(
  attendanceManagerAccess.canViewAttendanceManagement,
  true,
  "managed attendance permissions enable attendance management capability"
);

assertEqual(
  attendanceManagerAccess.canReviewAttendance,
  true,
  "review managed attendance permission enables review capability"
);

const attendanceHolidayEditorAccess = buildAccess(
  createUser([PERMISSIONS.Attendance.HolidayEditManagedStore])
);

assertEqual(
  attendanceHolidayEditorAccess.canEditAttendanceHoliday,
  true,
  "holiday edit permission enables attendance holiday edit capability"
);

const attendanceSettingsEditorAccess = buildAccess(
  createUser([PERMISSIONS.Attendance.SettingsEdit])
);

assertEqual(
  attendanceSettingsEditorAccess.canEditAttendanceSettings,
  true,
  "attendance settings edit permission enables attendance settings capability"
);

const storeFinanceAccess = buildAccess(
  createUser([
    PERMISSIONS.LocalPurchase.PushToHq,
    PERMISSIONS.StoreProducts.Create,
    PERMISSIONS.InstallmentOrders.View,
    PERMISSIONS.StoreVouchers.View,
    PERMISSIONS.SeasonalCards.Remaining.ViewManagedStore,
    PERMISSIONS.SeasonalCards.Remaining.SubmitManagedStore,
  ])
);

assertEqual(
  storeFinanceAccess.canPushLocalPurchaseToHq,
  true,
  "LocalPurchase.PushToHq enables local purchase push capability"
);

assertEqual(
  storeFinanceAccess.canCreateStoreProducts,
  true,
  "StoreProducts.Create enables store product creation capability"
);

assertEqual(
  buildAccess(createUser([])).canCreateStoreProducts,
  false,
  "missing StoreProducts.Create keeps store product creation capability disabled"
);

assertEqual(
  storeFinanceAccess.canViewInstallmentOrders,
  true,
  "InstallmentOrders.View enables installment orders capability"
);

assertEqual(
  storeFinanceAccess.canViewStoreVouchers,
  true,
  "StoreVouchers.View enables store vouchers capability"
);

assertEqual(
  storeFinanceAccess.canViewSeasonalCardRemaining,
  true,
  "SeasonalCards.Remaining.ViewManagedStore enables seasonal card remaining history capability"
);

assertEqual(
  storeFinanceAccess.canSubmitSeasonalCardRemaining,
  true,
  "SeasonalCards.Remaining.SubmitManagedStore enables seasonal card remaining submission capability"
);
