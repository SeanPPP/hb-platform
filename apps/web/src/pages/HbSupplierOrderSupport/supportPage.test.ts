import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { HB_SUPPLIER_ORDER_SUPPORT } from './supportContent'

const appSource = readFileSync('src/App.tsx', 'utf8')

assert.ok(appSource.includes("import HbSupplierOrderSupportPage from './pages/HbSupplierOrderSupport'"))
assert.ok(appSource.includes('path="/support/hb-supplier-order"'))
assert.equal(HB_SUPPLIER_ORDER_SUPPORT.publicUrl, 'https://hotbargain.vip/support/hb-supplier-order')
assert.equal(HB_SUPPLIER_ORDER_SUPPORT.privacyUrl, 'https://hotbargain.vip/privacy/browser-extension')
assert.equal(HB_SUPPLIER_ORDER_SUPPORT.contactEmail, 'inquiries@hotbargain.com.au')
assert.equal(
  HB_SUPPLIER_ORDER_SUPPORT.testSupplierUrl,
  'https://www.meteorparty.com.au/Party-Favors/Party-Favors-Allfavors',
)
assert.ok(HB_SUPPLIER_ORDER_SUPPORT.locales.en.steps.length >= 4)
assert.ok(HB_SUPPLIER_ORDER_SUPPORT.locales.zh.steps.length >= 4)
assert.ok(HB_SUPPLIER_ORDER_SUPPORT.locales.en.faqs.length >= 3)
assert.ok(HB_SUPPLIER_ORDER_SUPPORT.locales.zh.faqs.length >= 3)

console.log('HB Supplier Order support page tests passed')
