import { beforeEach, expect, jest, test } from "@jest/globals";
import Storage from "expo-sqlite/kv-store";

import {
  LANGUAGE_PREFERENCE_KEY,
  readSalesToolbarOrder,
  readStoredLanguage,
  SALES_TOOLBAR_ORDER_PREFERENCE_KEY,
  saveSalesToolbarOrder,
  saveStoredLanguage,
} from "./terminal-ui-preferences";

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
