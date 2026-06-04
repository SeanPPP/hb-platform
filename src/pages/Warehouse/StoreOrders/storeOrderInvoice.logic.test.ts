import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

async function runTest(name: string, execute: () => void | Promise<void>): Promise<string | null> {
  try {
    await execute()
    console.log(`ok - ${name}`)
    return null
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    console.error(`not ok - ${name}`)
    console.error(reason)
    return `${name}: ${reason}`
  }
}

const invoiceFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/Invoice.tsx')
const printCssFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/print.css')
const zhFile = path.resolve(process.cwd(), 'src/i18n/locales/zh.json')
const enFile = path.resolve(process.cwd(), 'src/i18n/locales/en.json')

const invoiceSource = readFileSync(invoiceFile, 'utf8')
const printCssSource = readFileSync(printCssFile, 'utf8')
const zhSource = readFileSync(zhFile, 'utf8')
const enSource = readFileSync(enFile, 'utf8')

function readCssRule(source: string, selector: string) {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const match = source.match(new RegExp(`${escapedSelector}\\s*\\{([\\s\\S]*?)\\}`))
  return match?.[1] ?? ''
}

async function main() {
  const failures: string[] = []

  const emailEntryFailure = await runTest('发票页应提供发送邮件入口并锁定接口调用字符串', () => {
    assert(invoiceSource.includes('sendStoreOrderInvoiceEmail'), '发票页应接入发送邮件 service')
    assert(invoiceSource.includes('getStoreOrderInvoiceEmailJob'), '发票页应接入发票邮件 job 查询 service')
    assert(invoiceSource.includes('createStoreOrderInvoiceEmailJobPoller'), '发票页应使用发票邮件 job 轮询器')
    assert(invoiceSource.includes('stopInvoiceEmailPollingRef.current?.()'), '发票页卸载时应清理邮件 job 轮询')
    assert(invoiceSource.includes("result.status === 'Succeeded'"), '发票页应处理邮件发送成功终态')
    assert(invoiceSource.includes("result.status === 'Failed'"), '发票页应处理邮件发送失败终态')
    assert(invoiceSource.includes("t('warehouse.invoice.emailJobSubmitted')"), '发票页提交 job 后应立即提示任务已提交')
    assert(invoiceSource.includes('updateStoreOrderStoreContact'), '发票页应可把编辑后的邮箱保存为分店默认邮箱')
    assert(invoiceSource.includes('downloadElementAsPdf'), '发票页下载 PDF 按钮应保留前端导出逻辑')
    assert(invoiceSource.includes('downloadInvoiceExcel'), '发票页导出 Excel 按钮应保留前端导出逻辑')
    assert(!invoiceSource.includes('createStoreOrderInvoicePdfBase64'), '发票邮件不应保留前端邮件 PDF 生成 helper')
    assert(
      !invoiceSource.includes('const pdfBase64 = await createStoreOrderInvoicePdfBase64()'),
      '发送邮件时不应再由前端生成 PDF base64',
    )
    assert(!invoiceSource.includes('pdfBase64,'), '发送邮件 payload 不应包含 pdfBase64')
    assert(!invoiceSource.includes('pdfFileName,'), '发送邮件 payload 不应包含 pdfFileName')
    assert(invoiceSource.includes("t('warehouse.invoice.sendEmail')"), '发票页应提供发送邮件按钮文案')
    assert(invoiceSource.includes("t('warehouse.invoice.emailModalTitle')"), '发票页应提供发送邮件弹窗标题')
    assert(invoiceSource.includes("t('warehouse.invoice.saveAsStoreDefault')"), '发票页应提供保存为分店默认邮箱开关')
  })
  if (emailEntryFailure) failures.push(emailEntryFailure)

  const invoicePdfBreakFailure = await runTest('发票 PDF 导出应按明细行和页脚边界切页', () => {
    assert(invoiceSource.includes('collectElementBreakOffsets'), '发票页应引入 PDF 行边界收集工具')
    assert(
      invoiceSource.includes("'.store-order-invoice-table tbody tr'"),
      '发票 PDF 应收集明细表格行边界',
    )
    assert(
      invoiceSource.includes("'.store-order-invoice-footer'"),
      '发票 PDF 应收集发票页脚边界',
    )
    assert(invoiceSource.includes('avoidBreakOffsets: getInvoicePdfBreakOffsets()'), '下载 PDF 应传入发票切页边界')
    assert(!invoiceSource.includes('createElementPdfBase64(printRootRef.current'), '邮件链路不应再生成 PDF base64')
  })
  if (invoicePdfBreakFailure) failures.push(invoicePdfBreakFailure)

  const emailDefaultFailure = await runTest('发票页默认邮箱与地址读取顺序应保持业务约束', () => {
    assert(
      invoiceSource.includes('const storeAddress = order.storeAddress || store?.address || \'--\''),
      '发票地址应优先读取订单地址，再回退到分店地址',
    )
    assert(
      invoiceSource.includes('order.storeContactEmail || store?.contactEmail || \'\''),
      '发送邮件默认收件人应优先读取订单邮箱，再回退到分店默认邮箱',
    )
  })
  if (emailDefaultFailure) failures.push(emailDefaultFailure)

  const invoiceCssFailure = await runTest('发票 print.css 应只调整发票规则且避免横向溢出', () => {
    const paperRule = readCssRule(printCssSource, '.store-order-invoice-paper')
    const tableRule = readCssRule(printCssSource, '.store-order-invoice-table')
    const thRule = readCssRule(printCssSource, '.store-order-invoice-table th')
    const tdRule = readCssRule(printCssSource, '.store-order-invoice-table td')
    const barcodeRule = readCssRule(printCssSource, '.store-order-invoice-table .col-barcode')

    assert(/padding:\s*\d+mm\s+\d+mm/.test(paperRule), '发票纸张 padding 应收窄到更紧凑的毫米级设置')
    assert(/table-layout:\s*fixed/.test(tableRule), '发票表格应使用固定列布局防止撑宽')
    assert(/font-size:\s*1[01]px/.test(tableRule), '发票表格字体应缩小到 10-11px')
    assert(/padding:\s*[45]px/.test(thRule), '发票表头内边距应缩小')
    assert(/padding:\s*[45]px/.test(tdRule), '发票单元格内边距应缩小')
    assert(/overflow:\s*hidden/.test(barcodeRule) || /word-break:\s*break-all/.test(barcodeRule), '条码列应限制溢出')
    assert(!printCssSource.includes('.store-order-detail-table'), 'print.css 不应污染详情页紧凑样式')
    assert(!printCssSource.includes('.store-order-list-table'), 'print.css 不应污染列表页紧凑样式')
  })
  if (invoiceCssFailure) failures.push(invoiceCssFailure)

  const translationFailure = await runTest('发票邮件文案应提供中英文翻译', () => {
    for (const key of [
      'sendEmail',
      'emailModalTitle',
      'recipientEmail',
      'emailSubject',
      'emailBody',
      'saveAsStoreDefault',
      'emailJobSubmitted',
      'emailSendSuccess',
      'emailSendFailed',
      'emailJobPollingFailed',
      'emailJobPollingTimeout',
    ]) {
      assert(zhSource.includes(`\"${key}\"`), `中文翻译缺少 ${key}`)
      assert(enSource.includes(`\"${key}\"`), `英文翻译缺少 ${key}`)
    }
  })
  if (translationFailure) failures.push(translationFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('storeOrderInvoice.logic.test: ok')
}

await main()
