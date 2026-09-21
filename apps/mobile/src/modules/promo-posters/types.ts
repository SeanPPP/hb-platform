/** 海报类型：特价 / 多件价 / 新品 / 清仓。 */
export type PromoPosterKind = "special" | "multibuy" | "new" | "clearance";

/** 海报风格：经典（直角卡片 + 横幅）/ 现代（圆角色块 + 贴纸）。 */
export type PromoPosterStyle = "classic" | "modern";

/** 海报成品尺寸。 */
export type PromoPosterSize = "A4" | "A5" | "A6" | "A7";

export const PROMO_POSTER_KINDS: readonly PromoPosterKind[] = ["special", "multibuy", "new", "clearance"];
export const PROMO_POSTER_STYLES: readonly PromoPosterStyle[] = ["classic", "modern"];
export const PROMO_POSTER_SIZES: readonly PromoPosterSize[] = ["A4", "A5", "A6", "A7"];

/** 商品当前生效的多件促销（来自 defaults 接口）。 */
export interface PromoPosterMultiBuyOffer {
  promotionId: string;
  name: string;
  applyQuantity: number;
  fixedPrice: number;
  effectiveStart: string;
  effectiveEnd: string;
  productsCount: number;
}

/** GET /promo-posters/defaults 返回的商品海报默认值。 */
export interface PromoPosterDefaults {
  productCode: string;
  itemNumber: string;
  productName: string;
  englishName: string;
  /** 空字符串表示没有可打印的英文名，需要店员手填。 */
  posterTitle: string;
  retailPrice: number | null;
  discountRate: number | null;
  discountedPrice: number | null;
  clearancePrice: number | null;
  multiBuyOffers: PromoPosterMultiBuyOffer[];
  canSpecial: boolean;
  canMultiBuy: boolean;
  canClearance: boolean;
}

/**
 * 编辑页草稿：价格、日期都保持输入框原文，提交前再统一解析校验。
 * 日期统一为 YYYY-MM-DD；空字符串表示未填写。
 */
export interface PromoPosterDraft {
  kind: PromoPosterKind;
  style: PromoPosterStyle;
  size: PromoPosterSize;
  title: string;
  price: string;
  wasPrice: string;
  quantity: string;
  unitPrice: string;
  mixAndMatch: boolean;
  /** 多件价选中的促销；其它类型为 null。 */
  offerId: string | null;
  validFrom: string;
  validTo: string;
  inStoreSince: string;
}

/** 单张海报的最终内容，字段与 POST /promo-posters/pdf 的 posters[] 一致。 */
export interface PromoPosterSpec {
  kind: PromoPosterKind;
  style: PromoPosterStyle;
  size: PromoPosterSize;
  productCode: string;
  itemNumber: string;
  title: string;
  price: number;
  wasPrice?: number;
  quantity?: number;
  unitPrice?: number;
  mixAndMatch?: boolean;
  validFrom?: string;
  validTo?: string;
  inStoreSince?: string;
}

/** 待打印队列中的一项。 */
export interface PromoPosterQueueItem {
  id: string;
  storeCode: string;
  /** 商品中文名，仅用于 App 内展示，不印到海报上。 */
  productName: string;
  addedAt: string;
  poster: PromoPosterSpec;
}

export interface PromoPosterPdfRequest {
  storeCode: string;
  impose: boolean;
  showLogo: boolean;
  posters: PromoPosterSpec[];
}
