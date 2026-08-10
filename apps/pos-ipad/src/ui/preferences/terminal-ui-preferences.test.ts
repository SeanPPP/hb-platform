import { beforeEach, expect, jest, test } from "@jest/globals";
import Storage from "expo-sqlite/kv-store";

import {
  BUTTON_SOUND_PREFERENCE_KEY,
  CAMERA_SCAN_MODE_PREFERENCE_KEY,
  LANGUAGE_PREFERENCE_KEY,
  SPECIAL_NODE_SOUND_PREFERENCE_KEY,
  TOUCH_SOUND_PREFERENCE_KEY,
  readButtonSoundEnabled,
  readCameraScanMode,
  readSalesToolbarOrder,
  readSpecialNodeSoundEnabled,
  readStoredLanguage,
  SALES_TOOLBAR_ORDER_PREFERENCE_KEY,
  saveButtonSoundEnabled,
  saveCameraScanMode,
  saveSalesToolbarOrder,
  saveSpecialNodeSoundEnabled,
  saveStoredLanguage,
} from "./terminal-ui-preferences";

import type { CameraScanMode } from "@/core/contracts/scanner";

jest.mock("expo-sqlite/kv-store", () => ({
  __esModule: true,
  default: {
    getItemSync: jest.fn(),
    setItem: jest.fn(),
  },
}));

const mockGetItemSync = jest.mocked(Storage.getItemSync);
const mockSetItem = jest.mocked(Storage.setItem);

beforeEach(() => {
  jest.clearAllMocks();
  mockGetItemSync.mockReturnValue(null);
  mockSetItem.mockResolvedValue(undefined);
});

test("语言偏好只接受 zh/en，读取异常时安全回退", () => {
  mockGetItemSync.mockReturnValue("zh");
  expect(readStoredLanguage()).toBe("zh");
  expect(mockGetItemSync).toHaveBeenCalledWith(LANGUAGE_PREFERENCE_KEY);

  mockGetItemSync.mockReturnValue("fr");
  expect(readStoredLanguage()).toBeNull();

  mockGetItemSync.mockImplementation(() => {
    throw new Error("SQLite unavailable");
  });
  expect(readStoredLanguage()).toBeNull();
});

test("工具栏排序只接受字符串数组，并在损坏或读取异常时忽略", () => {
  mockGetItemSync.mockReturnValue(JSON.stringify(["search", "payment"]));
  expect(readSalesToolbarOrder()).toEqual(["search", "payment"]);
  expect(mockGetItemSync).toHaveBeenCalledWith(
    SALES_TOOLBAR_ORDER_PREFERENCE_KEY,
  );

  mockGetItemSync.mockReturnValue('["search", 1]');
  expect(readSalesToolbarOrder()).toBeNull();

  mockGetItemSync.mockReturnValue("{");
  expect(readSalesToolbarOrder()).toBeNull();

  mockGetItemSync.mockImplementation(() => {
    throw new Error("SQLite unavailable");
  });
  expect(readSalesToolbarOrder()).toBeNull();
});

test("异步保存使用固定键且吞掉存储异常", async () => {
  await saveStoredLanguage("en");
  await saveSalesToolbarOrder(["search", "payment"]);

  expect(mockSetItem).toHaveBeenNthCalledWith(
    1,
    LANGUAGE_PREFERENCE_KEY,
    "en",
  );
  expect(mockSetItem).toHaveBeenNthCalledWith(
    2,
    SALES_TOOLBAR_ORDER_PREFERENCE_KEY,
    '["search","payment"]',
  );

  mockSetItem.mockRejectedValue(new Error("disk full"));
  await expect(saveStoredLanguage("zh")).resolves.toBeUndefined();
  await expect(saveSalesToolbarOrder(["payment"])).resolves.toBeUndefined();
});

test("相机扫码模式只接受 single/continuous，缺失、损坏或读取异常时回退单次", () => {
  mockGetItemSync.mockReturnValue("continuous");
  expect(readCameraScanMode()).toBe("continuous");
  expect(mockGetItemSync).toHaveBeenCalledWith(
    CAMERA_SCAN_MODE_PREFERENCE_KEY,
  );

  mockGetItemSync.mockReturnValue("single");
  expect(readCameraScanMode()).toBe("single");

  mockGetItemSync.mockReturnValue(null);
  expect(readCameraScanMode()).toBe("single");

  mockGetItemSync.mockReturnValue("invalid");
  expect(readCameraScanMode()).toBe("single");

  mockGetItemSync.mockImplementation(() => {
    throw new Error("SQLite unavailable");
  });
  expect(readCameraScanMode()).toBe("single");
});

test("相机扫码模式仅保存合法值，非法运行时值和写入异常均安全忽略", async () => {
  await saveCameraScanMode("continuous");
  await saveCameraScanMode("single");

  expect(mockSetItem).toHaveBeenNthCalledWith(
    1,
    CAMERA_SCAN_MODE_PREFERENCE_KEY,
    "continuous",
  );
  expect(mockSetItem).toHaveBeenNthCalledWith(
    2,
    CAMERA_SCAN_MODE_PREFERENCE_KEY,
    "single",
  );

  await saveCameraScanMode("invalid" as CameraScanMode);
  expect(mockSetItem).toHaveBeenCalledTimes(2);

  mockSetItem.mockRejectedValue(new Error("disk full"));
  await expect(saveCameraScanMode("continuous")).resolves.toBeUndefined();
});

test("双音效新键严格读取 true/false，且有效新值优先于旧总开关", () => {
  mockGetItemSync.mockImplementation((key) => {
    if (key === BUTTON_SOUND_PREFERENCE_KEY) return "false";
    if (key === SPECIAL_NODE_SOUND_PREFERENCE_KEY) return "true";
    if (key === TOUCH_SOUND_PREFERENCE_KEY) return "false";
    return null;
  });

  expect(readButtonSoundEnabled()).toBe(false);
  expect(readSpecialNodeSoundEnabled()).toBe(true);
  expect(mockGetItemSync).not.toHaveBeenCalledWith(TOUCH_SOUND_PREFERENCE_KEY);
});

test("每个新音效键缺失或损坏时独立只读回退旧总开关", () => {
  mockGetItemSync.mockImplementation((key) => {
    if (key === BUTTON_SOUND_PREFERENCE_KEY) return null;
    if (key === SPECIAL_NODE_SOUND_PREFERENCE_KEY) return "TRUE";
    if (key === TOUCH_SOUND_PREFERENCE_KEY) return "false";
    return null;
  });

  expect(readButtonSoundEnabled()).toBe(false);
  expect(readSpecialNodeSoundEnabled()).toBe(false);
  expect(mockGetItemSync).toHaveBeenCalledWith(BUTTON_SOUND_PREFERENCE_KEY);
  expect(mockGetItemSync).toHaveBeenCalledWith(
    SPECIAL_NODE_SOUND_PREFERENCE_KEY,
  );
  expect(mockGetItemSync).toHaveBeenCalledTimes(4);
});

test("新安装、旧值损坏或读取异常时双音效均默认开启", () => {
  mockGetItemSync.mockReturnValue(null);
  expect(readButtonSoundEnabled()).toBe(true);
  expect(readSpecialNodeSoundEnabled()).toBe(true);

  mockGetItemSync.mockReturnValue("TRUE");
  expect(readButtonSoundEnabled()).toBe(true);

  mockGetItemSync.mockImplementation(() => {
    throw new Error("SQLite unavailable");
  });
  expect(readButtonSoundEnabled()).toBe(true);
  expect(readSpecialNodeSoundEnabled()).toBe(true);
});

test("双音效只写各自新键，写入异常不抛错且不触碰旧键", async () => {
  await saveButtonSoundEnabled(false);
  await saveSpecialNodeSoundEnabled(true);

  expect(mockSetItem).toHaveBeenNthCalledWith(
    1,
    BUTTON_SOUND_PREFERENCE_KEY,
    "false",
  );
  expect(mockSetItem).toHaveBeenNthCalledWith(
    2,
    SPECIAL_NODE_SOUND_PREFERENCE_KEY,
    "true",
  );
  expect(mockSetItem).not.toHaveBeenCalledWith(
    TOUCH_SOUND_PREFERENCE_KEY,
    expect.any(String),
  );

  mockSetItem.mockRejectedValue(new Error("disk full"));
  await expect(saveButtonSoundEnabled(true)).resolves.toBeUndefined();
  await expect(saveSpecialNodeSoundEnabled(false)).resolves.toBeUndefined();
});
