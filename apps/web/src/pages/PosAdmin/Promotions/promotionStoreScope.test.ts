import assert from 'node:assert/strict'
import {
  getPromotionEditorStoreCodes,
  getPromotionStoreCodes,
  isPromotionAllStoresScope,
} from './promotionStoreScope'

assert.equal(
  isPromotionAllStoresScope({ stores: [] }),
  true,
  '空 stores 应显示为总部/全部分店范围',
)

assert.equal(
  isPromotionAllStoresScope({ scopeType: 'Headquarters', stores: [{ id: '1', storeCode: '' }] }),
  true,
  'Headquarters 范围应显示为全部分店',
)

assert.deepEqual(
  getPromotionStoreCodes([{ id: '1', storeCode: 'S01' }]),
  ['S01'],
  '有具体分店时应回填原始分店编码',
)

assert.deepEqual(
  getPromotionEditorStoreCodes({ scopeType: 'Headquarters', stores: [{ id: '1', storeCode: 'S01' }] }),
  [],
  'Headquarters 范围即使带有分店数据也应按全部分店回填',
)

assert.equal(
  isPromotionAllStoresScope({ stores: [{ id: '1', storeCode: 'S01' }] }),
  false,
  '有具体分店时不应显示全部分店范围提示',
)

console.log('promotionStoreScope.test: ok')
