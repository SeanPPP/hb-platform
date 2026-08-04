import { buildShopLocalSupplierInvoiceGridRequest } from './logic'

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

assertDeepEqual(
  buildShopLocalSupplierInvoiceGridRequest({
    page: 2,
    pageSize: 50,
    storeCode: ' Bankstown ',
    supplierCode: ' SUP-01 ',
    productKeyword: ' HB 1001 ',
  }),
  {
    startRow: 50,
    endRow: 100,
    pageSize: 50,
    filterModel: {
      StoreCode: {
        filterType: 'text',
        type: 'equals',
        filter: 'Bankstown',
      },
      SupplierCode: {
        filterType: 'text',
        type: 'equals',
        filter: 'SUP-01',
      },
      ProductKeyword: {
        filterType: 'text',
        type: 'contains',
        filter: 'HB 1001',
      },
    },
    sortModel: [{ colId: 'OrderDate', sort: 'desc' }],
  },
  '商城进货单列表请求应包含精确分店/供应商筛选、商品宽搜索和订货日期倒序',
)

assertDeepEqual(
  buildShopLocalSupplierInvoiceGridRequest({
    page: 0,
    pageSize: 999,
    storeCode: ' ',
    supplierCode: undefined,
    productKeyword: '',
  }),
  {
    startRow: 0,
    endRow: 20,
    pageSize: 20,
    filterModel: {},
    sortModel: [{ colId: 'OrderDate', sort: 'desc' }],
  },
  '非法分页和空白筛选应回退到第一页、每页 20 条且不生成过滤条件',
)
