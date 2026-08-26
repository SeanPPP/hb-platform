function monotonicNow(): number {
  return typeof performance === "undefined" ? Date.now() : performance.now();
}

// 此模块不得引入任何依赖；index.js 将它作为首个 import，记录最早可用 JS 起点。
export const businessStartupOrigin = monotonicNow();
