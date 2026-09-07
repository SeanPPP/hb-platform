import {
  buildPolicyPayload,
  buildReleaseQuery,
  buildReleaseUpdatePayload,
  normalizeWpfRelease,
} from "./api";

function assertEqual(actual: unknown, expected: unknown, message: string) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(
      `${message}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`,
    );
  }
}

assertEqual(
  buildReleaseQuery({
    page: 0,
    pageSize: -1,
    channel: " preview ",
    includeDisabled: true,
  }),
  { page: 1, pageSize: 10, channel: "preview", includeDisabled: true },
  "release query should normalize paging and filters",
);
assertEqual(
  buildReleaseUpdatePayload({
    downloadUrl: "  https://example.test/a  ",
    sha256: " ",
    installerArguments: " /S ",
    releaseNotes: "  notes  ",
    isActive: false,
  }),
  {
    downloadUrl: "https://example.test/a",
    sha256: null,
    installerArguments: "/S",
    releaseNotes: "notes",
    isActive: false,
  },
  "metadata payload should trim optional values",
);
assertEqual(
  buildPolicyPayload({
    channel: " Preview ",
    targetVersion: " 1.3.0 ",
    minimumSupportedVersion: "1.2.0",
    forceUpdate: true,
    isRollback: false,
    targetScope: "devices",
    targetStoreGuids: ["ignored"],
    targetDeviceRegistrationIds: [3, 3, 0],
  }),
  {
    channel: "preview",
    targetVersion: "1.3.0",
    minimumSupportedVersion: "1.2.0",
    forceUpdate: true,
    isRollback: false,
    targetScope: "devices",
    targetStoreGuids: [],
    targetDeviceRegistrationIds: [3],
  },
  "policy payload should scope target ids",
);

const release = normalizeWpfRelease({
  id: "r1",
  Version: "1.2.3",
  Active: "true",
  InstallerType: "MSI",
  TargetScope: "devices",
  TargetDeviceRegistrationIds: ["7", 7, 0],
  targetDeviceSummaries: [
    {
      deviceRegistrationId: "7",
      systemDeviceNumber: "POS-7",
      storeCode: "BNE",
      storeName: "Brisbane",
    },
  ],
});
assertEqual(
  release,
  {
    id: "r1",
    version: "1.2.3",
    channel: "production",
    fileName: "",
    fileSize: null,
    sha256: null,
    installerType: "msi",
    installerArguments: null,
    downloadUrl: null,
    objectKey: null,
    releaseNotes: null,
    isActive: true,
    isCurrent: false,
    isRollback: false,
    forceUpdate: false,
    minimumSupportedVersion: null,
    targetVersion: null,
    targetScope: "devices",
    targetStoreGuids: [],
    targetDeviceRegistrationIds: [7],
    targetStoreSummaries: [],
    targetDeviceSummaries: [
      {
        deviceRegistrationId: 7,
        systemDeviceNumber: "POS-7",
        storeCode: "BNE",
        storeName: "Brisbane",
        remarks: null,
      },
    ],
    policyUpdatedAt: null,
    policyUpdatedBy: null,
    createdAt: null,
    updatedAt: null,
  },
  "release normalization should protect the screen model",
);

// 网络函数依赖原生 Expo Router；这里覆盖其使用的请求参数、载荷和响应归一化契约。
assertEqual(
  buildReleaseQuery({
    page: 2,
    pageSize: 10,
    channel: " production ",
    includeDisabled: false,
  }),
  { page: 2, pageSize: 10, channel: "production", includeDisabled: false },
  "release API query should use apiClient-relative endpoint parameters",
);
console.log("wpf versions api tests passed");
