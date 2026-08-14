import type { CashDrawerPort } from "../contracts/drawer";
import type { PrinterPort } from "../contracts/printer";
import { createDefaultHbPrinterAdapter } from "../peripherals/printer/native";

export type RuntimePrinterAdapter = PrinterPort &
  Pick<CashDrawerPort, "open">;

export type HbPrinterModuleLoader = (moduleName: "HbPrinter") => unknown;

/**
 * 将 Expo Modules 的解析延后至真正的连接、打印或开钱箱动作；运行时构造不触碰硬件。
 */
export function createLazyHbPrinterAdapter(
  loadNativeModule: HbPrinterModuleLoader,
): RuntimePrinterAdapter {
  let adapter: RuntimePrinterAdapter | undefined;
  const resolveAdapter = (): RuntimePrinterAdapter => {
    adapter ??= createDefaultHbPrinterAdapter(
      () => loadNativeModule("HbPrinter") as never,
    );
    return adapter;
  };

  return {
    getStatus: () => resolveAdapter().getStatus(),
    scan: (timeoutMs) => resolveAdapter().scan(timeoutMs),
    connect: (peripheralId) => resolveAdapter().connect(peripheralId),
    disconnect: () => resolveAdapter().disconnect(),
    print: (operationId, bytes) => resolveAdapter().print(operationId, bytes),
    open: (operationId) => resolveAdapter().open(operationId),
    subscribe: (listener) => resolveAdapter().subscribe(listener),
  };
}
