import { expect, jest, test } from "@jest/globals";

jest.mock("expo-audio", () => {
  throw new Error("控件不应加载 expo-audio");
});
jest.mock("expo-sqlite/kv-store", () => {
  throw new Error("控件不应加载 expo-sqlite");
});

const { PosPressable } = require("./pos-pressable") as typeof import("./pos-pressable");

test("导入 PosPressable 不加载原生音频或 SQLite 偏好实现", () => {
  expect(PosPressable).toBeDefined();
});
