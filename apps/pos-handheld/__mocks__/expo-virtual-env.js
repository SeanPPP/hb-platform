// Expo 会把 EXPO_PUBLIC_* 读取改写为此虚拟模块；Jest 使用同语义的 CJS 版本。
module.exports = { env: process.env };
