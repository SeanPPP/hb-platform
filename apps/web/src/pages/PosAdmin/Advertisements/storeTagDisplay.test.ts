import { getAdvertisementStoreTagLabels } from './storeTagDisplay'

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${message}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

const localStores = [
  { value: '1002', label: 'Sunnybank' },
  { value: '1003', label: 'Eastwood' },
  { value: '1004', label: 'Chatswood' },
]

assertDeepEqual(
  getAdvertisementStoreTagLabels([], localStores),
  ['--'],
  '空分店范围应显示占位符',
)

assertDeepEqual(
  getAdvertisementStoreTagLabels([{ storeCode: '1002', storeName: '接口分店' }], localStores),
  ['接口分店（1002）'],
  '接口名称应优先于页面分店目录',
)

assertDeepEqual(
  getAdvertisementStoreTagLabels([{ storeCode: '1003' }], localStores),
  ['Eastwood（1003）'],
  '接口未返回名称时应使用页面分店目录',
)

assertDeepEqual(
  getAdvertisementStoreTagLabels([{ storeCode: '9999' }], localStores),
  ['9999'],
  '未知分店应安全回退为分店代码',
)

assertDeepEqual(
  getAdvertisementStoreTagLabels(
    [{ storeCode: 's04' }],
    [{ value: ' S04 ', label: ' Inactive Store ' }],
  ),
  ['Inactive Store（s04）'],
  '本地分店目录匹配应忽略代码大小写和首尾空格',
)

assertDeepEqual(
  getAdvertisementStoreTagLabels([{ storeCode: ' S01 ', storeName: 's01' }], localStores),
  ['S01'],
  '名称仅与代码大小写或空格不同时不得重复显示',
)

assertDeepEqual(
  getAdvertisementStoreTagLabels([
    { storeCode: '1002', storeName: '1002' },
    { storeCode: '1003' },
    { storeCode: '1004' },
  ], localStores),
  ['1002', 'Eastwood（1003）', 'Chatswood（1004）'],
  '名称与代码相同时不得重复显示，且前三项保持原始顺序',
)

assertDeepEqual(
  getAdvertisementStoreTagLabels([
    { storeCode: '1002' },
    { storeCode: '1003' },
    { storeCode: '1004' },
    { storeCode: '1005' },
  ], localStores),
  ['Sunnybank（1002）', 'Eastwood（1003）', 'Chatswood（1004）', '+1'],
  '四项分店范围仅展示前三项和剩余数量',
)

assertDeepEqual(
  getAdvertisementStoreTagLabels(
    Array.from({ length: 35 }, (_, index) => ({ storeCode: String(index + 1000) })),
    localStores,
  ),
  ['1000', '1001', 'Sunnybank（1002）', '+32'],
  '三十五项分店范围应显示前三项和 +32',
)

console.log('storeTagDisplay.test.ts: ok')
