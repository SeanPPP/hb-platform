import { requireNativeModule } from "expo";

import {
  createLazyHbPrinterAdapter,
  type RuntimePrinterAdapter,
} from "./lazy-printer-adapter";

/**
 * Expo Modules 在首次使用硬件前才解析，避免启动阶段创建蓝牙模块。缺少 Development
 * Build 原生模块时，底层 bridge 会将加载异常转换为明确的不可用结果。
 */
export function createLazyExpoPrinterAdapter(): RuntimePrinterAdapter {
  return createLazyHbPrinterAdapter(requireNativeModule);
}
