import assert from "node:assert/strict";
import {
  buildAppDeviceStatusListParams,
  normalizeAppDeviceStatusListResponse,
  normalizeAppDeviceStatusSummary,
} from "./api";

const params = buildAppDeviceStatusListParams({
  pageNumber: 2,
  pageSize: 50,
  keyword: "  hbmobile  ",
  storeCode: "1004",
  deviceSystem: " iOS ",
  onlineState: "all",
});

assert.deepEqual(params, {
  page: 2,
  pageSize: 50,
  keyword: "hbmobile",
  storeCode: "1004",
  deviceSystem: "iOS",
  onlineState: undefined,
});

const list = normalizeAppDeviceStatusListResponse({
  Success: true,
  Data: {
    Items: [
      {
        Id: "row-1",
        HardwareId: "hbmobile-1",
        SystemDeviceNumber: "SYS-001",
        DeviceSystem: "Android",
        Platform: "android",
        StoreCode: "1004",
        AppVersion: "1.0.2",
        AppBuildVersion: "12",
        RuntimeVersion: "1.0.2",
        Channel: "preview",
        UpdateId: "11111111-1111-1111-8111-111111111111",
        LastSeenAtUtc: "2026-07-08T00:00:00Z",
        IsOnline: true,
        LastSeenUsername: "staff",
        RegisteredDeviceId: 8,
      },
    ],
    Total: 1,
    Page: 1,
    PageSize: 20,
  },
});

assert.equal(list.devices.length, 1);
assert.equal(list.devices[0]?.hardwareId, "hbmobile-1");
assert.equal(list.devices[0]?.deviceSystem, "Android");
assert.equal(list.devices[0]?.isOnline, true);
assert.equal(list.devices[0]?.lastSeenUsername, "staff");
assert.equal(list.devices[0]?.registeredDeviceId, 8);
assert.equal(list.pagination.totalCount, 1);
assert.equal(list.pagination.pageNumber, 1);
assert.equal(list.pagination.pageSize, 20);

const summary = normalizeAppDeviceStatusSummary({
  Data: {
    Total: 5,
    Online: 2,
    Offline: 3,
    Android: 3,
    Ios: 1,
    UnknownSystem: 1,
  },
});

assert.deepEqual(summary, {
  total: 5,
  online: 2,
  offline: 3,
  android: 3,
  ios: 1,
  unknownSystem: 1,
});
