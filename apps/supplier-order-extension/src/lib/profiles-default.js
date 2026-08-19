// 内置默认供应商 profile（DATS 列表页）
export const DEFAULT_PROFILES = {
  configVersion: '1',
  profiles: [
    {
      supplierCode: 'DATS',
      displayName: 'DATS',
      enabled: true,
      origins: ['https://www.dats.com.au/*'],
      listPagePatterns: ['https://www.dats.com.au/*'],
      cardSelector: '.product[data-product-code]',
      itemNumber: {
        source: 'attribute',
        selector: null,
        attribute: 'data-product-code',
        transforms: ['trim', 'uppercase'],
      },
      mountSelector: '.widget-productlist-code',
      mountPosition: 'afterend',
    },
  ],
};
