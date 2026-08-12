import assert from 'node:assert/strict'
import { shouldIgnoreShopBarcodeSubmit } from './shopBarcodeSubmitGate'

assert.equal(
  shouldIgnoreShopBarcodeSubmit({
    barcode: '930000000001',
    busy: false,
    cameraActive: true,
    pickerOpen: false,
    source: 'camera',
  }),
  false,
  '相机活动时应允许相机自身提交',
)
assert.equal(
  shouldIgnoreShopBarcodeSubmit({
    barcode: '930000000001',
    busy: false,
    cameraActive: false,
    pickerOpen: false,
    source: 'camera',
  }),
  true,
  '已关闭相机的迟到回调不得继续提交',
)

for (const source of ['hid', 'manual'] as const) {
  assert.equal(
    shouldIgnoreShopBarcodeSubmit({
      barcode: '930000000001',
      busy: false,
      cameraActive: true,
      pickerOpen: false,
      source,
    }),
    true,
    `相机活动时应同步拦截 ${source} 输入源`,
  )
}

assert.equal(
  shouldIgnoreShopBarcodeSubmit({
    barcode: '930000000001',
    busy: true,
    cameraActive: false,
    pickerOpen: false,
    source: 'hid',
  }),
  true,
  '处理中不得并发提交',
)
assert.equal(
  shouldIgnoreShopBarcodeSubmit({
    barcode: '930000000001',
    busy: false,
    cameraActive: false,
    pickerOpen: true,
    source: 'manual',
  }),
  true,
  '选择器打开时不得提交',
)
assert.equal(
  shouldIgnoreShopBarcodeSubmit({
    barcode: '   ',
    busy: false,
    cameraActive: false,
    pickerOpen: false,
    source: 'manual',
  }),
  true,
  '空条码不得提交',
)

console.log('shopBarcodeSubmitGate.test.ts: ok')
