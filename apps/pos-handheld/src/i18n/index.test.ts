import { beforeEach, expect, jest, test } from "@jest/globals";
import * as Localization from "expo-localization";
import Storage from "expo-sqlite/kv-store";

import i18n, {
  resolveInitialLanguage,
  toggleAppLanguage,
} from "./index";

jest.mock("expo-localization", () => ({
  getLocales: jest.fn(() => [{ languageCode: "en" }]),
}));

jest.mock("expo-sqlite/kv-store", () => ({
  __esModule: true,
  default: {
    getItemSync: jest.fn(),
    setItem: jest.fn(),
  },
}));

const mockGetLocales = jest.mocked(Localization.getLocales);
const mockGetItemSync = jest.mocked(Storage.getItemSync);
const mockSetItem = jest.mocked(Storage.setItem);

function deviceLocales(languageCode: string): ReturnType<typeof Localization.getLocales> {
  return [{ languageCode }] as ReturnType<typeof Localization.getLocales>;
}

beforeEach(async () => {
  jest.clearAllMocks();
  mockGetLocales.mockReturnValue(deviceLocales("en"));
  mockGetItemSync.mockReturnValue(null);
  mockSetItem.mockResolvedValue(undefined);
  await i18n.changeLanguage("zh");
});

test("已保存语言优先于设备语言，否则沿用既有设备规则", () => {
  mockGetItemSync.mockReturnValue("zh");
  mockGetLocales.mockReturnValue(deviceLocales("en"));
  expect(resolveInitialLanguage()).toBe("zh");

  mockGetItemSync.mockReturnValue(null);
  mockGetLocales.mockReturnValue(deviceLocales("zh"));
  expect(resolveInitialLanguage()).toBe("zh");

  mockGetLocales.mockReturnValue(deviceLocales("fr"));
  expect(resolveInitialLanguage()).toBe("en");
});

test("切换语言立即更新会话并持久化，保存失败不回滚", async () => {
  await expect(toggleAppLanguage()).resolves.toBe("en");
  expect(i18n.resolvedLanguage ?? i18n.language).toBe("en");
  expect(mockSetItem).toHaveBeenCalledWith("hb.pos.language.v1", "en");

  mockSetItem.mockRejectedValue(new Error("disk full"));
  await expect(toggleAppLanguage()).resolves.toBe("zh");
  expect(i18n.resolvedLanguage ?? i18n.language).toBe("zh");
});
