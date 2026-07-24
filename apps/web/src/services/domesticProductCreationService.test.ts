import assert from 'node:assert/strict'
import {
  deactivateSetProductTemplate,
  getBatchDetail,
  getSetProductTemplate,
  updatePrivateLabelPrice,
  updateSetProductTemplate,
} from './domesticProductCreationService'
import { getBatchDetailErrorMessage } from '../pages/DomesticPurchase/ProductCreation/batchDetailErrorMessage'

const originalFetch = globalThis.fetch
const requests: Array<{ url: string; method: string; body?: unknown }> = []

try {
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    const method = init?.method || 'GET'
    requests.push({
      url,
      method,
      body: init?.body ? JSON.parse(String(init.body)) : undefined,
    })

    if (url.endsWith('/batch/BATCH-1') && method === 'GET') {
      return new Response(JSON.stringify({
        success: true,
        data: {
          batchNumber: 'BATCH-1',
          supplierCode: 'SUP/A',
          supplierName: '测试供应商',
          items: [{
            productCode: 'relation-row-1',
            hbProductNo: 'HB258-340-02',
            privateLabelPrice: 2.99,
          }],
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }

    if (url.endsWith('/batch/BATCH-ERROR/prices') && method === 'PUT') {
      return new Response(JSON.stringify({
        success: false,
        message: '商品 HB258-340-02 不属于当前批次，无法保存价格',
      }), {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      })
    }

    if (url.includes('/templates/template-1') && method === 'GET') {
      return new Response(JSON.stringify({
        success: true,
        data: {
          templateId: 'template-1',
          supplierCode: 'SUP/A',
          templateName: '礼品盒三件套',
          setProductName: '套三标准',
          isEnabled: true,
          subItems: [],
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }

    return new Response(JSON.stringify({ success: true }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const detailResult = await getBatchDetail('BATCH-1')
  assert.equal(detailResult.success, true)
  assert.equal(detailResult.data?.items[0].itemNumber, 'relation-row-1')

  await updatePrivateLabelPrice('BATCH-1', [{
    itemNumber: detailResult.data!.items[0].itemNumber,
    privateLabelPrice: 2.5,
  }])

  assert.deepEqual(
    requests.find((request) => request.url.endsWith('/batch/BATCH-1/prices'))?.body,
    {
      items: [{
        productCode: 'relation-row-1',
        privateLabelPrice: 2.5,
      }],
    },
    '价格保存必须把详情模型的 itemNumber 映射为后端 UpdatePriceItemDto.productCode',
  )

  let visibleSaveError = ''
  try {
    await updatePrivateLabelPrice('BATCH-ERROR', [{
      itemNumber: 'relation-row-1',
      privateLabelPrice: 2.5,
    }])
  } catch (error) {
    visibleSaveError = getBatchDetailErrorMessage(error, '保存失败')
  }
  assert.equal(
    visibleSaveError,
    '商品 HB258-340-02 不属于当前批次，无法保存价格',
    '400 保存失败必须把后端具体业务消息传给批次明细界面',
  )

  await getSetProductTemplate('template-1', 'SUP/A')
  await updateSetProductTemplate('template-1', 'SUP/A', {
    supplierCode: 'SUP/A',
    templateName: '礼品盒三件套',
    setProductName: '套三标准',
    isEnabled: true,
    subItems: [],
  })
  await deactivateSetProductTemplate('template-1', 'SUP/A')

  const scopedTemplateRequests = requests.filter((request) => request.url.includes('/templates/template-1'))
  assert.deepEqual(
    scopedTemplateRequests.map((request) => ({
      method: request.method,
      supplierCode: new URL(request.url, 'https://example.test').searchParams.get('supplierCode'),
    })),
    [
      { method: 'GET', supplierCode: 'SUP/A' },
      { method: 'PUT', supplierCode: 'SUP/A' },
      { method: 'POST', supplierCode: 'SUP/A' },
    ],
    '模板详情、更新和停用请求必须携带 supplierCode 边界',
  )
} finally {
  globalThis.fetch = originalFetch
}
