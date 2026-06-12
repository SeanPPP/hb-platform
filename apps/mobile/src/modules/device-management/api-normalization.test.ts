import { normalizeDeviceManagementListResponse } from "./api";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const direct = normalizeDeviceManagementListResponse({
  devices: [
    {
      id: "device-1",
      hardwareId: "HW-001",
      deviceName: "Front POS",
      storeCode: "1001",
      storeName: "Sunnybank",
      status: 1,
      lastSeenAt: "2026-05-01T10:00:00Z",
      appVersion: "1.0.0",
      platform: "android",
    },
  ],
  pagination: {
    pageNumber: 2,
    pageSize: 20,
    totalCount: 55,
    totalPages: 3,
  },
});

assertEqual(direct.devices.length, 1, "direct payload keeps devices");
assertEqual(direct.devices[0]?.id, "device-1", "normalizes camel id");
assertEqual(direct.devices[0]?.hardwareId, "HW-001", "normalizes camel hardware id");
assertEqual(direct.devices[0]?.status, 1, "normalizes camel status");
assertEqual(direct.pagination.pageNumber, 2, "normalizes camel page number");
assertEqual(direct.pagination.totalCount, 55, "normalizes camel total count");

const enveloped = normalizeDeviceManagementListResponse({
  data: {
    Devices: [
      {
        Id: 42,
        HardwareId: "HW-042",
        DeviceName: "Warehouse PDA",
        StoreCode: "2002",
        StoreName: "Warehouse",
        Status: -1,
        CreatedAt: "2026-04-30T09:00:00Z",
        LastModified: "2026-05-02T11:00:00Z",
      },
    ],
    Pagination: {
      PageNumber: 1,
      PageSize: 10,
      TotalCount: 1,
      TotalPages: 1,
    },
  },
});

assertEqual(enveloped.devices.length, 1, "enveloped payload keeps devices");
assertEqual(enveloped.devices[0]?.id, "42", "normalizes Pascal numeric id to string");
assertEqual(enveloped.devices[0]?.hardwareId, "HW-042", "normalizes Pascal hardware id");
assertEqual(enveloped.devices[0]?.status, -1, "normalizes Pascal status");
assertEqual(enveloped.devices[0]?.updatedAt, "2026-05-02T11:00:00Z", "normalizes Pascal last modified");
assertEqual(enveloped.pagination.pageSize, 10, "normalizes Pascal page size");
assertEqual(enveloped.pagination.totalPages, 1, "normalizes Pascal total pages");

const empty = normalizeDeviceManagementListResponse({});

assertEqual(empty.devices.length, 0, "missing devices fallback to empty list");
assertEqual(empty.pagination.pageNumber, 1, "missing pagination uses first page");
