import assert from 'node:assert/strict'

import { renderToStaticMarkup } from 'react-dom/server'

import { OrderType } from '../../types/posmSalesOrder'

import OrderStatusTag, { PaymentMethodsText } from './OrderStatusTag'
import { summarizeStatuses } from './posmSalesOrdersLogic'
import StatusSummaryStrip from './StatusSummaryStrip'

const noop = () => undefined

const view = summarizeStatuses([
  { status: 1, orderCount: 7708, totalAmount: 96782.8, discountAmount: 849.05 },
  { status: 3, orderCount: 24, totalAmount: -425.09, discountAmount: 0 },
  { status: 2, orderCount: 72, totalAmount: 2013.84, discountAmount: 37.81 },
])

// 汇总条：全部显示净实收；当前状态高亮且 aria-pressed，退款金额标红，已取消标明不计入实收。
const strip = renderToStaticMarkup(<StatusSummaryStrip view={view} activeStatus={OrderType.Refunded} onSelect={noop} />)
assert.ok(strip.includes('$95,508.66'), '全部卡片应显示净实收')
assert.ok(strip.includes('净实收'), '全部卡片应标明净实收')
assert.ok(strip.includes('−$425.09'), '退款卡片应显示负数实收')
assert.ok(strip.includes('is-negative'), '退款金额应标红')
assert.ok(strip.includes('不计入实收'), '已取消卡片应标明不计入实收')
assert.equal((strip.match(/aria-pressed="true"/g) ?? []).length, 1, '只有一个状态卡片处于选中态')
assert.ok(/class="posm-orders-status-card is-active"[^>]*aria-pressed="true"/.test(strip), '选中的卡片同时带高亮样式')
assert.ok(strip.includes('$849.05') && strip.includes('$12.45'), '应显示折扣合计与客单价')
assert.equal((strip.match(/<button/g) ?? []).length, 4, '全部、已支付、退款、已取消四个可点的状态卡片')

// 状态：已支付只用圆点加文字，退款、已取消用标签突出。
const paid = renderToStaticMarkup(<OrderStatusTag status={OrderType.Paid} />)
assert.ok(paid.includes('posm-orders-dot is-paid') && !paid.includes('ant-tag'), '已支付不用彩色标签')
const refunded = renderToStaticMarkup(<OrderStatusTag status={OrderType.Refunded} />)
assert.ok(refunded.includes('ant-tag') && refunded.includes('已退款'), '退款用标签显示')

// 支付方式：多种支付合并显示，没有支付记录显示破折号。
assert.ok(renderToStaticMarkup(<PaymentMethodsText methods={[1, 2]} />).includes('现金 / 刷卡'), '多种支付方式合并显示')
assert.ok(renderToStaticMarkup(<PaymentMethodsText methods={[]} />).includes('—'), '没有支付记录显示破折号')

console.log('posmSalesOrdersComponents.test: ok')
