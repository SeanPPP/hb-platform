let printing = false;

export class PriceLabelPrintBusyError extends Error {
  constructor() {
    super("PRICE_LABEL_PRINT_BUSY");
    this.name = "PriceLabelPrintBusyError";
  }
}

export function isPriceLabelPrintBusy() {
  return printing;
}

export async function runPriceLabelPrintExclusive<T>(operation: () => Promise<T>): Promise<T> {
  // 批量打印、单条打印和失败重试共用互斥；并发请求直接拒绝，不暗中排队以免重复出纸。
  if (printing) throw new PriceLabelPrintBusyError();
  printing = true;
  try {
    return await operation();
  } finally {
    printing = false;
  }
}
