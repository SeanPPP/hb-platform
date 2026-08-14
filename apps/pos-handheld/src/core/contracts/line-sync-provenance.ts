export type BackendPriceSource = 0 | 1 | 2 | 3 | 4;

/**
 * 商品加入购物车时冻结的服务端售卖身份。
 *
 * 该值只能来自已验证目录或 WPF 明确定义的退货映射规则，不能在补传时按当前目录反推。
 */
export type LineSyncProvenance = Readonly<{
  referenceCode: string | null;
  priceSource: BackendPriceSource;
}>;

const SUPPORTED_KEYS = new Set(["referenceCode", "priceSource"]);

export function normalizeLineSyncProvenance(
  input: unknown,
): LineSyncProvenance {
  if (!input || typeof input !== "object" || Array.isArray(input)) {
    throw new TypeError("line sync provenance must be an object");
  }

  for (const key of Object.keys(input)) {
    if (!SUPPORTED_KEYS.has(key)) {
      throw new TypeError(
        `line sync provenance contains unsupported field: ${key}`,
      );
    }
  }

  const candidate = input as {
    referenceCode?: unknown;
    priceSource?: unknown;
  };
  let referenceCode: string | null;
  if (candidate.referenceCode === null) {
    referenceCode = null;
  } else if (typeof candidate.referenceCode === "string") {
    referenceCode = candidate.referenceCode.trim();
    if (!referenceCode) {
      throw new TypeError("line sync reference code must not be blank");
    }
  } else {
    throw new TypeError("line sync reference code must be a string or null");
  }

  const priceSource = candidate.priceSource;
  if (
    priceSource !== 0 &&
    priceSource !== 1 &&
    priceSource !== 2 &&
    priceSource !== 3 &&
    priceSource !== 4
  ) {
    throw new TypeError("line sync backend price source is invalid");
  }

  return Object.freeze({ referenceCode, priceSource });
}
