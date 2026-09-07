import { ProductCreationType } from "./types";
import type {
  CreateDomesticProductBatchItem,
  DomesticSetTemplateDetail,
  SaveDomesticSetTemplateRequest,
} from "./types";

export interface SetSubItemDraft {
  key: string;
  productName: string;
  privateLabelPrice: string;
}

export interface ProductDraft extends SetSubItemDraft {
  productType: ProductCreationType.Normal | ProductCreationType.Set;
  createCount: string;
  setQuantity: string;
  setPrice: string;
  subItems: SetSubItemDraft[];
  // 保存列表虚拟化后的展开状态；构造接口请求时不会提交此字段。
  subItemsExpanded?: boolean;
}

export const MAX_BATCH_PARENT_ITEMS = 100;
export const MAX_BATCH_EXPANDED_ITEMS = 10_000;

let nextKey = 0;
const draftKey = () => `draft-${Date.now()}-${++nextKey}`;

export function newSubItem(): SetSubItemDraft {
  return { key: draftKey(), productName: "", privateLabelPrice: "" };
}

export function newProduct(productType = ProductCreationType.Normal, price = ""): ProductDraft {
  if (productType === ProductCreationType.SetSubItem) throw new Error("Invalid parent product type");
  return {
    ...newSubItem(), productType, privateLabelPrice: price,
    createCount: "1", setQuantity: "1", setPrice: "",
    subItems: productType === ProductCreationType.Set ? [newSubItem()] : [],
  };
}

export function parsePrice(value: string): number | null {
  if (!value.trim()) return null;
  const number = Number(value);
  return Number.isFinite(number) && number >= 0 ? number : NaN;
}

export function positiveInteger(value: string): number {
  const number = Number(value);
  return Number.isSafeInteger(number) && number >= 1 ? number : NaN;
}

export function validSubItems(product: ProductDraft) {
  return product.subItems.filter((item) => item.productName.trim() || item.privateLabelPrice.trim());
}

export type DraftErrorCode = "emptyProducts" | "invalidPrice" | "invalidSetCount" | "invalidSetQuantity" | "missingSubItems" | "invalidBatchCount" | "tooManyParentItems" | "tooManyExpandedItems" | "templateNameRequired" | "setNameRequired" | "subNameRequired" | "subPriceRequired";

export class DraftValidationError extends Error {
  constructor(public code: DraftErrorCode, public row = 0, public subRow = 0) {
    super(code);
  }
}

function checkedPrice(value: string, row: number, subRow = 0) {
  const price = parsePrice(value);
  if (price !== null && !Number.isFinite(price)) throw new DraftValidationError("invalidPrice", row, subRow);
  return price;
}

export function buildBatchItems(products: ProductDraft[]): CreateDomesticProductBatchItem[] {
  if (!products.length) throw new DraftValidationError("emptyProducts");
  let parentItems = 0;
  let expandedItems = 0;
  return products.map((product, index) => {
    const row = index + 1;
    const item: CreateDomesticProductBatchItem = {
      productName: product.productName.trim() || undefined,
      productType: product.productType,
      privateLabelPrice: checkedPrice(product.privateLabelPrice, row),
    };
    if (product.productType === ProductCreationType.Normal) {
      parentItems++;
      expandedItems++;
      if (parentItems > MAX_BATCH_PARENT_ITEMS) throw new DraftValidationError("tooManyParentItems", row);
      if (expandedItems > MAX_BATCH_EXPANDED_ITEMS) throw new DraftValidationError("tooManyExpandedItems", row);
      return item;
    }
    const createCount = positiveInteger(product.createCount);
    if (!Number.isFinite(createCount)) throw new DraftValidationError("invalidSetCount", row);
    const setQuantity = positiveInteger(product.setQuantity);
    if (!Number.isFinite(setQuantity)) throw new DraftValidationError("invalidSetQuantity", row);
    const subItems = validSubItems(product);
    if (!subItems.length) throw new DraftValidationError("missingSubItems", row);
    // 只累计请求会实际展开的父项与子项，避免先构造大数组再发现越界。
    parentItems += createCount;
    expandedItems += createCount * (1 + subItems.length);
    if (parentItems > MAX_BATCH_PARENT_ITEMS) throw new DraftValidationError("tooManyParentItems", row);
    if (expandedItems > MAX_BATCH_EXPANDED_ITEMS) throw new DraftValidationError("tooManyExpandedItems", row);
    return {
      ...item, createCount, setQuantity, setPrice: checkedPrice(product.setPrice, row),
      subItems: subItems.map((sub, subIndex) => ({
        productType: ProductCreationType.SetSubItem,
        productName: sub.productName.trim() || undefined,
        privateLabelPrice: checkedPrice(sub.privateLabelPrice, row, subIndex + 1),
      })),
    };
  });
}

export function summarizeBatch(products: ProductDraft[]) {
  let normal = 0;
  let sets = 0;
  let subItems = 0;
  for (const product of products) {
    if (product.productType === ProductCreationType.Normal) normal++;
    else {
      const count = positiveInteger(product.createCount);
      if (Number.isFinite(count)) {
        sets += count;
        subItems += count * validSubItems(product).length;
      }
    }
  }
  return { normal, sets, subItems, total: normal + sets + subItems };
}

export function batchAddDrafts(products: ProductDraft[], type: ProductCreationType, countText: string, price: string, mode: "append" | "overwrite") {
  const count = positiveInteger(countText);
  if (!Number.isFinite(count) || count > MAX_BATCH_PARENT_ITEMS) throw new DraftValidationError("invalidBatchCount");
  checkedPrice(price, 0);
  if (mode === "append") {
    const currentSummary = summarizeBatch(products);
    const currentParents = currentSummary.normal + currentSummary.sets;
    if (currentParents + count > MAX_BATCH_PARENT_ITEMS) throw new DraftValidationError("tooManyParentItems");
  }
  const added = Array.from({ length: count }, () => newProduct(type, price));
  return mode === "append" ? [...products, ...added] : added;
}

const EXPLICIT_CREATE_ERROR_CODES = new Set([
  "VALIDATION_ERROR",
  "CREATE_BATCH_LIMIT_EXCEEDED",
]);

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === "object" && value !== null ? value as Record<string, unknown> : null;
}

/** 只有服务端明确拒绝创建时才允许本会话重试；超时、取消与服务端异常都视为结果未知。 */
export function isExplicitCreateBusinessRejection(error: unknown): boolean {
  const source = record(error);
  const response = record(source?.response);
  const payload = record(response?.data) ?? source;
  const code = payload?.errorCode ?? payload?.ErrorCode ?? payload?.code ?? payload?.Code;
  const hasSafeBusinessCode = typeof code === "string" && EXPLICIT_CREATE_ERROR_CODES.has(code.trim());
  const rawStatus = response?.status ?? source?.status;
  const status = typeof rawStatus === "number" ? rawStatus : Number(rawStatus);
  if (Number.isFinite(status)) {
    if (status >= 500 || status === 408) return false;
    if (status < 200 || status >= 500) return false;
  }
  // 创建接口会把通用 CREATE_BATCH_ERROR 也包装成 400；只有参数和数量拒绝能证明没有写入。
  return hasSafeBusinessCode;
}

export function draftFromTemplate(template: DomesticSetTemplateDetail): ProductDraft {
  const product = newProduct(ProductCreationType.Set);
  return {
    ...product, productName: template.setProductName,
    setQuantity: String(template.subItems.length),
    subItems: [...template.subItems].sort((a, b) => a.sortOrder - b.sortOrder).map((item) => ({
      ...newSubItem(), productName: item.productName, privateLabelPrice: String(item.privateLabelPrice),
    })),
  };
}

export function applyTemplate(products: ProductDraft[], template: DomesticSetTemplateDetail, placeholderKey: string) {
  // 只替换系统初始化的空普通行；手动新增的空行也是用户草稿。
  const retained = products.filter((product) => product.key !== placeholderKey
    || product.productType !== ProductCreationType.Normal
    || product.productName.trim() || product.privateLabelPrice.trim());
  return [...retained, draftFromTemplate(template)];
}

export function buildTemplatePayload(supplierCode: string, templateName: string, product: ProductDraft): SaveDomesticSetTemplateRequest {
  if (!templateName.trim()) throw new DraftValidationError("templateNameRequired");
  if (!product.productName.trim()) throw new DraftValidationError("setNameRequired");
  if (!product.subItems.length) throw new DraftValidationError("missingSubItems");
  return {
    supplierCode, templateName: templateName.trim(), setProductName: product.productName.trim(), isEnabled: true,
    subItems: product.subItems.map((item, index) => {
      if (!item.productName.trim()) throw new DraftValidationError("subNameRequired", 0, index + 1);
      const price = checkedPrice(item.privateLabelPrice, 0, index + 1);
      if (price === null) throw new DraftValidationError("subPriceRequired", 0, index + 1);
      return { productName: item.productName.trim(), privateLabelPrice: price };
    }),
  };
}

/** 切换供应商、重试或退出时使旧请求失效，旧结果不再回填新表单。 */
export function createRequestScope() {
  let version = 0;
  return {
    begin() { const requestVersion = ++version; return () => requestVersion === version; },
    invalidate() { version++; },
  };
}

/** 同步上锁先于 React 重绘，防止快速连点产生两次写请求。 */
export function createSubmissionGate() {
  let busy = false;
  return {
    get busy() { return busy; },
    async run<T>(action: () => Promise<T>): Promise<T | undefined> {
      if (busy) return undefined;
      busy = true;
      try { return await action(); } finally { busy = false; }
    },
  };
}
