import assert from 'node:assert/strict'
import { ProductCreationType } from '../../../types/domesticProductCreation'
import { buildSetProductTemplatePayload, createSetDraftFromTemplate, validateSetTemplateProduct } from './setTemplateRules'

const template = {
  templateId: 'template-1',
  supplierCode: 'SUP-001',
  templateName: '冬季组合',
  setProductName: '冬季套装',
  isEnabled: true,
  setQuantity: 2,
  privateLabelPrice: 99,
  setPrice: 199,
  subItems: [
    { productName: '第二个子项', privateLabelPrice: 8.5, sortOrder: 2 },
    { productName: '第一个子项', privateLabelPrice: 5, sortOrder: 1 },
  ],
}

const draft = createSetDraftFromTemplate(template, 3)

assert.equal(draft.productType, ProductCreationType.SET)
assert.equal(draft.productName, '冬季套装')
assert.equal(draft.createCount, 1)
assert.equal(draft.setQuantity, 2)
assert.equal(draft.privateLabelPrice, undefined)
assert.equal(draft.setPrice, undefined)
assert.deepEqual(
  draft.subItems?.map((item) => ({ productName: item.productName, privateLabelPrice: item.privateLabelPrice })),
  [
    { productName: '第一个子项', privateLabelPrice: 5 },
    { productName: '第二个子项', privateLabelPrice: 8.5 },
  ],
)
assert.notEqual(draft.subItems?.[0], template.subItems[1])

template.subItems[1].productName = '模板后来被修改'
assert.equal(draft.subItems?.[0].productName, '第一个子项')

const payload = buildSetProductTemplatePayload('SUP-001', '  保存的模板  ', {
  key: 'set-draft',
  productType: ProductCreationType.SET,
  productName: '  新套装  ',
  privateLabelPrice: 77,
  setPrice: 88,
  setQuantity: 999,
  createCount: 3,
  subItems: [
    { key: 'sub-1', productName: ' 子项一 ', privateLabelPrice: 1.5 },
    { key: 'sub-2', productName: '子项二', privateLabelPrice: 2.5 },
  ],
})

assert.deepEqual(payload, {
  supplierCode: 'SUP-001',
  templateName: '保存的模板',
  setProductName: '新套装',
  isEnabled: true,
  subItems: [
    { productName: '子项一', privateLabelPrice: 1.5 },
    { productName: '子项二', privateLabelPrice: 2.5 },
  ],
})

assert.equal(validateSetTemplateProduct({
  key: 'missing-price',
  productType: ProductCreationType.SET,
  productName: '套装',
  subItems: [{ key: 'sub', productName: '子项', privateLabelPrice: null }],
}), 'missing_sub_item_price')

assert.equal(validateSetTemplateProduct({
  key: 'zero-price',
  productType: ProductCreationType.SET,
  productName: '套装',
  subItems: [{ key: 'sub', productName: '子项', privateLabelPrice: 0 }],
}), undefined)

assert.equal(validateSetTemplateProduct({
  key: 'negative-price',
  productType: ProductCreationType.SET,
  productName: '套装',
  subItems: [{ key: 'sub', productName: '子项', privateLabelPrice: -0.01 }],
}), 'invalid_sub_item_price')
