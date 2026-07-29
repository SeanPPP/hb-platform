/** @type {import("jest").Config} */
module.exports = {
  preset: "react-native",
  testEnvironment: "node",
  // Expo 模块以 ESM 发布；必须由 Jest 转换，避免测试顺序中的 mock 偶然掩盖解析失败。
  transformIgnorePatterns: [
    "node_modules/(?!((jest-)?react-native|@react-native|expo-localization)/)",
  ],
  moduleNameMapper: {
    "^@/(.*)$": "<rootDir>/src/$1",
  },
};
