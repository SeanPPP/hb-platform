// 内置默认供应商 profile（DATS 列表页）
export const DEFAULT_PROFILES = {
  configVersion: '2',
  profiles: [
    {
      // DATS 是显示名称；HB 的供应商业务代码是 240。
      supplierCode: '240',
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
