import {
  buildPromotionCopyPayload,
  buildPromotionGridPayload,
  buildPromotionPayload,
  normalizePromotionDetail,
  normalizePromotionsResponse,
} from "./api";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

assertDeepEqual(
  buildPromotionGridPayload({ storeCode: " S01 " }),
  {
    storeCode: "S01",
    globalSearch: undefined,
    startRow: 0,
    pageSize: 20,
    sortModel: undefined,
  },
  "默认促销列表查询使用后端 grid 协议"
);

assertDeepEqual(
  buildPromotionGridPayload({
    page: 2.7,
    pageSize: 49.9,
    keyword: "  周年庆  ",
    storeCode: " STO01 ",
    sortModel: [{ colId: "priority", sort: "desc" }],
  }),
  {
    storeCode: "STO01",
    globalSearch: "周年庆",
    startRow: 20,
    pageSize: 20,
    sortModel: [{ colId: "priority", sort: "desc" }],
  },
  "促销列表查询会裁剪字段并规范分页"
);

assertDeepEqual(
  buildPromotionPayload({
    name: "  清仓促销 ",
    storeCode: " STO01 ",
    priority: 7.8,
    products: [
      { productCode: " SKU01 ", unitWeight: 1.8 },
      { productCode: " ", unitWeight: 9 },
      { productCode: "SKU02", unitWeight: "2.5" },
    ],
  }),
  {
    name: "清仓促销",
    description: undefined,
    effectiveStart: "",
    effectiveEnd: "",
    isEnabled: true,
    isExclusive: true,
    priority: 7,
    applyQuantity: 0,
    fixedPrice: 0,
    maxApplicationsPerOrder: undefined,
    products: [
      { productCode: "SKU01", unitWeight: 1 },
      { productCode: "SKU02", unitWeight: 2 },
    ],
    stores: [{ storeCode: "STO01" }],
  },
  "创建和编辑促销时会清洗门店与商品明细"
);

assertDeepEqual(
  buildPromotionCopyPayload({
    sourcePromotionId: " promo-1 ",
    storeCode: " STO09 ",
  }),
  {
    sourcePromotionId: "promo-1",
    storeCode: "STO09",
  },
  "复制到门店请求会裁剪必要字段"
);

const list = normalizePromotionsResponse({
  success: true,
  data: {
    items: [
      {
        Id: 12,
        Name: "总部主推",
        ScopeType: "Headquarters",
        Priority: "4",
        CanEditInStoreScope: 0,
        CanCopyToStore: 1,
        ProductsCount: 2,
        StoresCount: 0,
      },
    ],
    Total: "3",
  },
});

assertEqual(list.items.length, 1, "促销列表归一化保留记录");
assertEqual(list.items[0]?.id, "12", "促销列表归一化会把 id 转成字符串");
assertEqual(list.items[0]?.scopeType, "Headquarters", "促销列表归一化保留适用范围");
assertEqual(list.items[0]?.priority, 4, "促销列表归一化会转优先级");
assertEqual(list.items[0]?.canEditInStoreScope, false, "促销列表归一化会转换编辑权限");
assertEqual(list.items[0]?.canCopyToStore, true, "促销列表归一化会转换复制权限");
assertEqual(list.items[0]?.productsCount, 2, "促销列表归一化保留商品数量");
assertEqual(list.total, 3, "促销列表归一化保留总数");
assertEqual(list.pageNumber, 1, "促销列表归一化使用默认页码");
assertEqual(list.pageSize, 20, "促销列表归一化使用默认页大小");

const detail = normalizePromotionDetail({
  item: {
    id: "promo-9",
    name: "本店搭配购",
    scopeType: "StoreOnly",
    priority: "0",
    canEditInStoreScope: true,
    canCopyToStore: false,
    products: [{ productCode: "SKU09", unitWeight: "3.25" }],
    stores: [{ storeCode: "MEL01" }],
  },
});

assertEqual(detail?.id, "promo-9", "促销详情归一化解包 item 节点");
assertEqual(detail?.scopeType, "StoreOnly", "促销详情归一化保留本店范围");
assertEqual(detail?.priority, 0, "促销详情归一化保留默认优先级");
assertEqual(detail?.products[0]?.productCode, "SKU09", "促销详情归一化保留商品编码");
assertEqual(detail?.products[0]?.unitWeight, 3, "促销详情归一化保留商品权重");
