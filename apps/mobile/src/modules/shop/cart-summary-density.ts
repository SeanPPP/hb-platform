interface CartSummaryViewport {
  width: number;
  height: number;
}

interface CartSkuCountOptions {
  productCodes: Array<string | null | undefined>;
  reportedSkuCount?: unknown;
}

export function resolveCartSummaryScale({ width, height }: CartSummaryViewport) {
  const widthScale = width > 0 ? Math.min(1, width / 390) : 1;
  const heightScale = height > 0 ? Math.min(1, height / 760) : 1;
  return Math.round(Math.max(0.82, Math.min(widthScale, heightScale)) * 100) / 100;
}

export function resolveCartSkuCount({ productCodes, reportedSkuCount }: CartSkuCountOptions) {
  const parsedReportedCount = Number(reportedSkuCount);
  const distinctProductCount = new Set(
    productCodes.map((code) => code?.trim()).filter((code): code is string => Boolean(code))
  ).size;

  if (Number.isFinite(parsedReportedCount) && parsedReportedCount > 0) {
    return parsedReportedCount;
  }

  return distinctProductCount;
}

export function resolveCheckoutBarMaxHeight({ height }: CartSummaryViewport) {
  return Math.floor(Math.max(0, height) * 0.15);
}
