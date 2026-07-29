import { describe, expect, it, jest } from "@jest/globals";

import { readSettingsDevicePresentation } from "./expo-pos-runtime";

jest.mock("expo-constants", () => ({
  __esModule: true,
  default: { expoConfig: null },
}));
jest.mock("expo-crypto", () => ({}));
jest.mock("expo-network", () => ({}));
jest.mock("expo-updates", () => ({}));
jest.mock("../db/expo-sqlite-driver", () => ({
  ExpoSqliteDriver: class ExpoSqliteDriver {},
}));
jest.mock("../security/expo-secure-store", () => ({
  ExpoSecureStoreAdapter: class ExpoSecureStoreAdapter {},
}));
jest.mock("../security/sensitive-payload-encryptor", () => ({
  SensitivePayloadEncryptor: class SensitivePayloadEncryptor {},
}));
jest.mock("../peripherals/attendance-security/native", () => ({
  createExpoAttendanceSecurityAdapter: jest.fn(),
}));
jest.mock("../peripherals/customer-display/native", () => ({
  customerDisplayAdvertisementCacheRootUri: "file:///test",
  ExpoAdvertisementFileSystem: class ExpoAdvertisementFileSystem {},
  externalDisplay: {},
}));
jest.mock("./expo-printer-adapter", () => ({
  createLazyExpoPrinterAdapter: jest.fn(),
}));

describe("readSettingsDevicePresentation", () => {
  it("只从公开设备展示身份映射设置页终端身份", async () => {
    const getDevicePresentation = jest.fn(async () => ({
      deviceCode: "POS-01",
      storeCode: "BNE-01",
      storeName: "Brisbane Central",
    }));

    const result = await readSettingsDevicePresentation({
      getDevicePresentation,
    });

    expect(result).toEqual({
      deviceCode: "POS-01",
      storeCode: "BNE-01",
      storeName: "Brisbane Central",
      terminalName: "",
    });
    expect(Object.isFrozen(result)).toBe(true);
    expect(getDevicePresentation).toHaveBeenCalledTimes(1);
  });

  it("公开展示身份缺少分店名称时传空字符串且不拿分店代码回退", async () => {
    await expect(
      readSettingsDevicePresentation({
        getDevicePresentation: async () => ({
          deviceCode: "POS-01",
          storeCode: "BNE-01",
          storeName: null,
        }),
      }),
    ).resolves.toEqual({
      deviceCode: "POS-01",
      storeCode: "BNE-01",
      storeName: "",
      terminalName: "",
    });
  });

  it("公开展示身份不可用时失败关闭", async () => {
    await expect(
      readSettingsDevicePresentation({
        getDevicePresentation: async () => null,
      }),
    ).rejects.toThrow("SETTINGS_DEVICE_IDENTITY_REQUIRED");
  });
});
