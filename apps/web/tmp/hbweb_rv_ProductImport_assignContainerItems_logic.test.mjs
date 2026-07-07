// src/pages/DomesticPurchase/ProductImport/utils.ts
function firstDefinedNumber(...values) {
  return values.find((value) => value !== void 0);
}
function firstPositiveNumber(...values) {
  return values.find((value) => value !== void 0 && value > 0);
}
function isPositiveNumber(value) {
  return value !== void 0 && Number.isFinite(value) && value > 0;
}
function isMissingText(value) {
  return !value?.trim();
}
function buildAssignContainerItems(products, notes) {
  return products.map((product) => ({
    hbProductNo: product.newProduct.productCode,
    productCode: product.matchedProduct?.productCode,
    quantity: product.newProduct.quantity,
    packingQuantity: firstPositiveNumber(product.newProduct.casePackQuantity, product.matchedProduct?.packingQuantity),
    unitVolume: firstPositiveNumber(product.newProduct.volume, product.matchedProduct?.unitVolume),
    domesticPrice: firstDefinedNumber(product.newProduct.domesticPrice, product.matchedProduct?.domesticPrice),
    oemPrice: firstDefinedNumber(product.newProduct.oemPrice, product.matchedProduct?.oemPrice),
    notes
  }));
}
function stripAssignContainerItemsForRequest(items) {
  return items.map(({ hbProductNo, productCode, quantity, packingQuantity, unitVolume, domesticPrice, oemPrice, notes }) => ({
    hbProductNo,
    productCode,
    quantity,
    packingQuantity,
    unitVolume,
    domesticPrice,
    oemPrice,
    notes
  }));
}
function findInvalidAssignContainerItems(items) {
  return items.map((item) => {
    const fields = [];
    const reasons = [];
    if (isMissingText(item.productCode)) {
      fields.push("\u672C\u5730\u5546\u54C1\u7F16\u7801");
      reasons.push("\u672A\u5339\u914D\u672C\u5730\u5546\u54C1\u7F16\u7801");
    }
    if (!isPositiveNumber(item.quantity)) fields.push("\u4EF6\u6570");
    if (!isPositiveNumber(item.domesticPrice)) fields.push("\u56FD\u5185\u4EF7\u683C");
    if (!isPositiveNumber(item.packingQuantity)) fields.push("\u88C5\u7BB1\u6570");
    if (!isPositiveNumber(item.unitVolume)) fields.push("\u4F53\u79EF");
    return { hbProductNo: item.hbProductNo, productCode: item.productCode, fields, reasons };
  }).filter((item) => item.fields.length > 0);
}
function summarizeAssignProductsResult(response, items = []) {
  const created = response.data?.created ?? 0;
  const updated = response.data?.updated ?? 0;
  const failedItems = response.data?.failed ?? [];
  const succeeded = created + updated;
  const failedCount = failedItems.length;
  const hbProductNoByProductCode = /* @__PURE__ */ new Map();
  items.forEach((item) => {
    const productCode = item.productCode?.trim();
    const hbProductNo = item.hbProductNo?.trim();
    if (productCode && hbProductNo) {
      hbProductNoByProductCode.set(productCode, hbProductNo);
    }
  });
  const failed = failedItems.map((item) => ({
    hbProductNo: item.productCode ? hbProductNoByProductCode.get(item.productCode.trim()) : void 0,
    productCode: item.productCode,
    reason: item.error || "\u672A\u77E5\u539F\u56E0"
  }));
  if (!response.success) {
    return {
      status: "apiError",
      success: false,
      message: response.message,
      created,
      updated,
      succeeded,
      failedCount,
      failed
    };
  }
  if (failedCount > 0 && succeeded === 0) {
    return {
      status: "failed",
      success: false,
      message: response.message,
      created,
      updated,
      succeeded,
      failedCount,
      failed
    };
  }
  if (failedCount > 0) {
    return {
      status: "partial",
      success: true,
      message: response.message,
      created,
      updated,
      succeeded,
      failedCount,
      failed
    };
  }
  return {
    status: "success",
    success: true,
    message: response.message,
    created,
    updated,
    succeeded,
    failedCount,
    failed
  };
}

// src/pages/DomesticPurchase/ProductImport/ProductImport.assignContainerItems.logic.test.ts
import { readFileSync } from "node:fs";
function assertDeepEqual(actual, expected, label) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
function createProduct(overrides) {
  return {
    id: "row-1",
    selected: true,
    imageUrl: "",
    customImage: false,
    imageLoadStatus: "success",
    newProduct: {
      quantity: 1,
      productCode: "HB001",
      productName: "\u6D4B\u8BD5\u5546\u54C1"
    },
    status: "unchanged",
    isDuplicate: false,
    calculated: { totalProducts: 1, totalVolume: 0 },
    ...overrides
  };
}
var pageSource = readFileSync("src/pages/DomesticPurchase/ProductImport/index.tsx", "utf8");
var pageStyleSource = readFileSync("src/pages/DomesticPurchase/ProductImport/styles.css", "utf8");
var zhLocaleSource = readFileSync("src/i18n/locales/zh.json", "utf8");
var enLocaleSource = readFileSync("src/i18n/locales/en.json", "utf8");
assertDeepEqual(
  [
    pageSource.includes("const loadContainers = useCallback(async () => {"),
    pageSource.includes("onDropdownVisibleChange={(open) => {"),
    pageSource.includes("if (open) void loadContainers()")
  ],
  [true, true, true],
  "\u5546\u54C1\u5BFC\u5165\u8D27\u67DC\u4E0B\u62C9\u6253\u5F00\u65F6\u5E94\u81EA\u52A8\u5237\u65B0\u8D27\u67DC\u5217\u8868"
);
assertDeepEqual(
  [
    pageSource.includes("const productImportTableRef = useRef<HTMLDivElement | null>(null)"),
    pageSource.includes("const [tableScrollY, setTableScrollY] = useState(500)"),
    pageSource.includes("window.addEventListener('resize', updateTableScrollY)"),
    pageSource.includes("window.removeEventListener('resize', updateTableScrollY)"),
    pageSource.includes('className="product-import-table"'),
    pageSource.includes("const PRODUCT_IMPORT_BASE_TABLE_SCROLL_X = 1280"),
    pageSource.includes("const PRODUCT_IMPORT_DETECTED_TABLE_SCROLL_X = 2500"),
    pageSource.includes("const productImportTableScrollX = showStatistics ? PRODUCT_IMPORT_DETECTED_TABLE_SCROLL_X : PRODUCT_IMPORT_BASE_TABLE_SCROLL_X"),
    pageSource.includes("scroll={{ x: productImportTableScrollX, y: tableScrollY }}"),
    pageSource.includes("'--product-import-table-body-height': `${tableScrollY}px`"),
    pageStyleSource.includes(".product-import-table .ant-table-body"),
    pageStyleSource.includes("height: var(--product-import-table-body-height) !important;"),
    pageSource.includes("rowSelection={{ selectedRowKeys: state.selectedIds"),
    pageSource.includes("const handlePaste = useCallback((e: ClipboardEvent) => {"),
    pageSource.includes("const resolveColumnKeyFromTd = useCallback((td: HTMLTableCellElement): string | null => {")
  ],
  [true, true, true, true, true, true, true, true, true, true, true, true, true, true, true],
  "\u5546\u54C1\u5BFC\u5165\u8868\u683C\u5E94\u4F7F\u7528\u89C6\u53E3\u5269\u4F59\u9AD8\u5EA6\u548C\u5F53\u524D\u5217\u5BBD\u586B\u6EE1\u9875\u9762\u5E76\u4FDD\u7559\u7C98\u8D34\u548C\u9009\u62E9\u4EA4\u4E92"
);
assertDeepEqual(
  [
    pageSource.includes("searchText: [s.supplierCode, s.supplierName, s.shopNumber]"),
    pageSource.includes("String(option?.searchText || option?.label || option?.value || '').toLowerCase()"),
    pageSource.includes("label: `${s.supplierCode} - ${s.supplierName}${s.shopNumber ? ` - ${s.shopNumber}` : ''}")
  ],
  [true, true, true],
  "\u5546\u54C1\u5BFC\u5165\u4F9B\u5E94\u5546\u641C\u7D22\u5E94\u8986\u76D6\u7F16\u7801\u3001\u540D\u79F0\u3001\u5E97\u53F7\u548C\u5B8C\u6574\u5C55\u793A\u6587\u672C"
);
assertDeepEqual(
  [
    pageSource.includes("const deleteAllRows = useCallback(() => {"),
    pageSource.includes("title: t('productImport.deleteAllConfirmTitle'"),
    pageSource.includes("content: t('productImport.deleteAllConfirmContent'"),
    pageSource.includes("products: []"),
    pageSource.includes("selectedIds: []"),
    pageSource.includes("statistics: calculateStatistics([], [])"),
    pageSource.includes("setShowStatistics(false)"),
    pageSource.includes("setDuplicateGroups([])"),
    pageSource.includes("disabled={state.products.length === 0}"),
    pageSource.includes("productImport.deleteAll"),
    zhLocaleSource.includes('"deleteAll": "\u5220\u9664\u5168\u90E8"'),
    zhLocaleSource.includes('"deleteAllConfirmTitle": "\u5220\u9664\u6240\u6709\u8868\u683C\u884C"'),
    enLocaleSource.includes('"deleteAll": "Delete All"'),
    enLocaleSource.includes('"deleteAllConfirmTitle": "Delete all table rows"')
  ],
  [true, true, true, true, true, true, true, true, true, true, true, true, true, true],
  "\u5546\u54C1\u5BFC\u5165\u5E94\u63D0\u4F9B\u5220\u9664\u5168\u90E8\u8868\u683C\u884C\u6309\u94AE\uFF0C\u4E8C\u6B21\u786E\u8BA4\u540E\u6E05\u7A7A\u884C\u3001\u9009\u4E2D\u548C\u7EDF\u8BA1\u72B6\u6001"
);
assertDeepEqual(
  [
    pageSource.includes("import { isValidEAN13 } from '../../../utils/barcode'"),
    pageSource.includes("const isNonEan13Barcode = Boolean(barcode && !isValidEAN13(barcode))"),
    pageSource.includes("status={isNonEan13Barcode ? 'warning' : undefined}"),
    pageSource.includes("t('productImport.notEan13Barcode', '\u4E0D\u662F EAN13 \u6761\u7801')")
  ],
  [true, true, true, true],
  "\u5546\u54C1\u5BFC\u5165\u6761\u7801\u5217\u5E94\u6807\u660E\u975E EAN13 \u6761\u7801\uFF0C\u4F46\u4E0D\u963B\u65AD\u68C0\u6D4B\u548C\u4FDD\u5B58\u6D41\u7A0B"
);
assertDeepEqual(
  buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 17,
        productCode: "JM-018",
        productName: "1pc Baby Muffin",
        domesticPrice: 11.6,
        oemPrice: 6.99,
        casePackQuantity: 48,
        volume: 0.118
      },
      matchedProduct: { productCode: "P-JM-018" }
    })
  ], "\u8865\u4EF7\u683C"),
  [
    {
      hbProductNo: "JM-018",
      productCode: "P-JM-018",
      quantity: 17,
      packingQuantity: 48,
      unitVolume: 0.118,
      domesticPrice: 11.6,
      oemPrice: 6.99,
      notes: "\u8865\u4EF7\u683C"
    }
  ],
  "\u53D1\u9001\u8D27\u67DC\u672C\u5730\u6821\u9A8C\u5BF9\u8C61\u5E94\u4FDD\u7559\u5BFC\u5165\u884C\u56FD\u5185\u4EF7\u683C\u548C\u96F6\u552E\u4EF7"
);
assertDeepEqual(
  stripAssignContainerItemsForRequest(buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 17,
        productCode: "JM-018",
        productName: "1pc Baby Muffin",
        domesticPrice: 11.6,
        oemPrice: 6.99,
        casePackQuantity: 48,
        volume: 0.118
      },
      matchedProduct: { productCode: "P-JM-018" }
    })
  ], "\u8865\u4EF7\u683C")),
  [
    {
      hbProductNo: "JM-018",
      productCode: "P-JM-018",
      quantity: 17,
      packingQuantity: 48,
      unitVolume: 0.118,
      domesticPrice: 11.6,
      oemPrice: 6.99,
      notes: "\u8865\u4EF7\u683C"
    }
  ],
  "\u53D1\u9001 assign-products \u524D\u5E94\u4FDD\u7559\u540E\u7AEF DTO \u652F\u6301\u7684\u4E1A\u52A1\u5B57\u6BB5"
);
assertDeepEqual(
  buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 2,
        productCode: "HB002",
        productName: "\u65E7\u4EF7\u515C\u5E95\u5546\u54C1"
      },
      matchedProduct: {
        productCode: "P-HB002",
        domesticPrice: 3.2,
        oemPrice: 1.8,
        packingQuantity: 24,
        unitVolume: 0.086
      }
    })
  ], ""),
  [
    {
      hbProductNo: "HB002",
      productCode: "P-HB002",
      quantity: 2,
      packingQuantity: 24,
      unitVolume: 0.086,
      domesticPrice: 3.2,
      oemPrice: 1.8,
      notes: ""
    }
  ],
  "\u53D1\u9001\u8D27\u67DC\u4E1A\u52A1\u5B57\u6BB5\u7F3A\u5931\u65F6\u5E94\u4F7F\u7528\u5339\u914D\u5546\u54C1\u5B57\u6BB5\u515C\u5E95"
);
assertDeepEqual(
  stripAssignContainerItemsForRequest(buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 2,
        productCode: "HB002",
        productName: "\u65E7\u88C5\u7BB1\u6570\u515C\u5E95\u5546\u54C1"
      },
      matchedProduct: {
        productCode: "P-HB002",
        domesticPrice: 3.2,
        packingQuantity: 24,
        unitVolume: 0.086
      }
    })
  ], "\u65E7\u5B57\u6BB5\u515C\u5E95")),
  [
    {
      hbProductNo: "HB002",
      productCode: "P-HB002",
      quantity: 2,
      packingQuantity: 24,
      unitVolume: 0.086,
      domesticPrice: 3.2,
      oemPrice: void 0,
      notes: "\u65E7\u5B57\u6BB5\u515C\u5E95"
    }
  ],
  "\u53D1\u9001 assign-products \u8BF7\u6C42\u4F53\u5E94\u4FDD\u7559\u65E7\u5546\u54C1\u6258\u5E95\u540E\u7684\u88C5\u7BB1\u6570\uFF0C\u4F9B\u540E\u7AEF\u8BA1\u7B97\u88C5\u67DC\u6570\u91CF"
);
assertDeepEqual(
  buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 5,
        productCode: "HB004",
        productName: "\u96F6\u88C5\u7BB1\u6570\u6258\u5E95\u5546\u54C1",
        domesticPrice: 0,
        oemPrice: 0,
        casePackQuantity: 0,
        volume: 0
      },
      matchedProduct: {
        productCode: "P-HB004",
        domesticPrice: 9.9,
        oemPrice: 8.8,
        packingQuantity: 36,
        unitVolume: 0.125
      }
    })
  ], void 0),
  [
    {
      hbProductNo: "HB004",
      productCode: "P-HB004",
      quantity: 5,
      packingQuantity: 36,
      unitVolume: 0.125,
      domesticPrice: 0,
      oemPrice: 0,
      notes: void 0
    }
  ],
  "\u53D1\u9001\u8D27\u67DC\u88C5\u7BB1\u6570\u548C\u4F53\u79EF\u4E3A 0 \u65F6\u5E94\u6258\u5E95\uFF0C\u4EF7\u683C\u4E3A 0 \u65F6\u5E94\u4FDD\u7559\u5F53\u524D\u503C"
);
assertDeepEqual(
  buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 3,
        productCode: "HB003",
        productName: "\u65E0\u4EF7\u683C\u5546\u54C1"
      },
      matchedProduct: { productCode: "P-HB003" }
    })
  ], void 0),
  [
    {
      hbProductNo: "HB003",
      productCode: "P-HB003",
      quantity: 3,
      packingQuantity: void 0,
      unitVolume: void 0,
      domesticPrice: void 0,
      oemPrice: void 0,
      notes: void 0
    }
  ],
  "\u53D1\u9001\u8D27\u67DC\u4E0D\u5E94\u628A\u7F3A\u5931\u4EF7\u683C\u8F6C\u6210 0"
);
assertDeepEqual(
  findInvalidAssignContainerItems(buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 0,
        productCode: "HB005",
        productName: "\u4E1A\u52A1\u5B57\u6BB5\u7F3A\u5931\u5546\u54C1",
        domesticPrice: 0,
        casePackQuantity: 0,
        volume: 0
      },
      matchedProduct: {
        productCode: "P-HB005"
      }
    })
  ])),
  [
    {
      hbProductNo: "HB005",
      productCode: "P-HB005",
      fields: ["\u4EF6\u6570", "\u56FD\u5185\u4EF7\u683C", "\u88C5\u7BB1\u6570", "\u4F53\u79EF"],
      reasons: []
    }
  ],
  "\u53D1\u9001\u8D27\u67DC\u5E94\u62E6\u622A\u4EF6\u6570\u3001\u56FD\u5185\u4EF7\u683C\u3001\u88C5\u7BB1\u6570\u3001\u4F53\u79EF\u4E3A\u7A7A\u6216\u4E3A 0 \u7684\u5546\u54C1"
);
assertDeepEqual(
  findInvalidAssignContainerItems(buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 4,
        productCode: "HB006",
        productName: "\u65E7\u5B57\u6BB5\u6258\u5E95\u901A\u8FC7\u5546\u54C1"
      },
      matchedProduct: {
        productCode: "P-HB006",
        domesticPrice: 6.6,
        packingQuantity: 12,
        unitVolume: 0.08
      }
    })
  ])),
  [],
  "\u53D1\u9001\u8D27\u67DC\u5E94\u5141\u8BB8\u4E1A\u52A1\u5B57\u6BB5\u901A\u8FC7\u65E7\u5546\u54C1\u6258\u5E95\u540E\u53D1\u9001"
);
assertDeepEqual(
  findInvalidAssignContainerItems(buildAssignContainerItems([
    createProduct({
      newProduct: {
        quantity: 6,
        productCode: "HB007",
        productName: "\u672A\u5339\u914D\u672C\u5730\u7F16\u7801\u5546\u54C1",
        domesticPrice: 6.8,
        casePackQuantity: 20,
        volume: 0.09
      },
      matchedProduct: void 0
    })
  ])),
  [
    {
      hbProductNo: "HB007",
      productCode: void 0,
      fields: ["\u672C\u5730\u5546\u54C1\u7F16\u7801"],
      reasons: ["\u672A\u5339\u914D\u672C\u5730\u5546\u54C1\u7F16\u7801"]
    }
  ],
  "\u53D1\u9001\u8D27\u67DC\u5E94\u62E6\u622A\u672A\u5339\u914D\u672C\u5730\u5546\u54C1\u7F16\u7801\u7684\u5546\u54C1"
);
assertDeepEqual(
  summarizeAssignProductsResult(
    {
      success: true,
      data: {
        created: 2,
        updated: 1,
        failed: []
      }
    },
    buildAssignContainerItems([
      createProduct({
        newProduct: {
          quantity: 2,
          productCode: "HB008",
          productName: "\u5168\u6210\u529F\u5546\u54C1",
          domesticPrice: 9.1,
          casePackQuantity: 18,
          volume: 0.05
        },
        matchedProduct: {
          productCode: "P-HB008"
        }
      })
    ])
  ),
  {
    status: "success",
    success: true,
    message: void 0,
    created: 2,
    updated: 1,
    succeeded: 3,
    failedCount: 0,
    failed: []
  },
  "assign-products \u5168\u6210\u529F\u65F6\u5E94\u5F52\u7EB3\u4E3A success"
);
assertDeepEqual(
  summarizeAssignProductsResult(
    {
      success: true,
      data: {
        created: 1,
        updated: 1,
        failed: [
          { productCode: "P-HB009", error: "\u56FD\u5185\u4EF7\u683C\u7F3A\u5931" }
        ]
      }
    },
    buildAssignContainerItems([
      createProduct({
        id: "row-9",
        newProduct: {
          quantity: 4,
          productCode: "HB009",
          productName: "\u90E8\u5206\u5931\u8D25\u5546\u54C1",
          domesticPrice: 10.5,
          casePackQuantity: 16,
          volume: 0.07
        },
        matchedProduct: {
          productCode: "P-HB009"
        }
      })
    ])
  ),
  {
    status: "partial",
    success: true,
    message: void 0,
    created: 1,
    updated: 1,
    succeeded: 2,
    failedCount: 1,
    failed: [
      {
        hbProductNo: "HB009",
        productCode: "P-HB009",
        reason: "\u56FD\u5185\u4EF7\u683C\u7F3A\u5931"
      }
    ]
  },
  "assign-products \u90E8\u5206\u5931\u8D25\u65F6\u5E94\u4FDD\u7559\u6210\u529F\u7EDF\u8BA1\u5E76\u5E26\u51FA\u5931\u8D25\u660E\u7EC6"
);
assertDeepEqual(
  summarizeAssignProductsResult(
    {
      success: true,
      data: {
        created: 0,
        updated: 0,
        failed: [
          { productCode: "P-HB010", error: "\u5546\u54C1\u4E0D\u5B58\u5728" }
        ]
      }
    },
    buildAssignContainerItems([
      createProduct({
        id: "row-10",
        newProduct: {
          quantity: 5,
          productCode: "HB010",
          productName: "\u5168\u5931\u8D25\u5546\u54C1",
          domesticPrice: 8.8,
          casePackQuantity: 30,
          volume: 0.06
        },
        matchedProduct: {
          productCode: "P-HB010"
        }
      })
    ])
  ),
  {
    status: "failed",
    success: false,
    message: void 0,
    created: 0,
    updated: 0,
    succeeded: 0,
    failedCount: 1,
    failed: [
      {
        hbProductNo: "HB010",
        productCode: "P-HB010",
        reason: "\u5546\u54C1\u4E0D\u5B58\u5728"
      }
    ]
  },
  "assign-products \u5168\u5931\u8D25\u65F6\u4E0D\u5E94\u88AB\u89C6\u4E3A\u6210\u529F"
);
assertDeepEqual(
  summarizeAssignProductsResult(
    {
      success: false,
      message: "\u53D1\u9001\u5931\u8D25",
      data: {
        created: 0,
        updated: 0,
        failed: []
      }
    },
    []
  ),
  {
    status: "apiError",
    success: false,
    message: "\u53D1\u9001\u5931\u8D25",
    created: 0,
    updated: 0,
    succeeded: 0,
    failedCount: 0,
    failed: []
  },
  "assign-products \u63A5\u53E3\u7EA7\u5931\u8D25\u65F6\u5E94\u5F52\u7EB3\u4E3A apiError"
);
