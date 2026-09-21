import assert from 'node:assert/strict'
import { boundedPrice, curveError, defaultCurve, finalPrice, nodesFromRules, rawPrice, rulesFromNodes } from './pricingCurve'

const rules = defaultCurve()
assert.equal(curveError(rules), null)
for (const rule of rules) {
  let previous = 0
  for (let cents = Math.round(rule.minPrice * 100); cents <= Math.round(rule.maxPrice * 100); cents++) {
    const cost = cents / 100, price = finalPrice(cost, rawPrice(rule, cost))
    assert.ok(price >= previous, `售价倒挂 ${cost}`)
    assert.ok(price >= cost * 1.5 - 1e-8 && price <= cost * 5 + 1e-8)
    previous = price
  }
  assert.equal(finalPrice(rule.minPrice, rule.startRetailPrice!), rule.startRetailPrice)
  assert.equal(finalPrice(rule.maxPrice, rule.endRetailPrice!), rule.endRetailPrice)
}
// 直接命中自动价格归一档，以及其他旧尾数规则保持不变。
for (const [raw, expected] of [[.99, 1], [1, 1], [1.99, 2], [2, 2], [10, 9.99], [10.51, 10.99], [10.5, 10.5]]) assert.equal(finalPrice(raw / 3, raw), expected)
assert.equal(finalPrice(1, 1.99), 2)
assert.equal(finalPrice(1, 2.99), 2.99)
// 理论价落到 .99 / 1.99 时归一，邻近尾数不受影响。
assert.equal(finalPrice(1, 1.994), 2)
assert.equal(finalPrice(1, 2.004), 2.5)
// 下限/上限尾数卡出 .99 / 1.99 时允许归一后多 1 分钱。
assert.equal(finalPrice(.4, .5), 1)
assert.equal(finalPrice(.398, 10), 2)
assert.equal(boundedPrice(.198, .99), .99)
assert.equal(finalPrice(.198, .99), 1)
assert.equal(finalPrice(1.2, .5), 2)
assert.equal(finalPrice(.3, .75), 1)
assert.equal(finalPrice(.7, 1.75), 2)
const legacyRules = [{ minPrice: .1, maxPrice: .198, startRate: 5, endRate: 5, algorithm: 'Linear' as const }]
const legacyNodeRules = rulesFromNodes(nodesFromRules(legacyRules).map(n => ({ cost: n.cost, price: boundedPrice(n.cost, n.price) })))
assert.equal(legacyNodeRules[0].endRetailPrice, .99)
assert.equal(curveError(legacyNodeRules), null)
const precisionRule = rulesFromNodes([{ cost: .1, price: .5 }, { cost: 1.65, price: 5.5 }])[0]
assert.equal(finalPrice(1.03, rawPrice(precisionRule, 1.03)), 3.5)
assert.equal(finalPrice(1, 3.004), 3.5)
assert.equal(finalPrice(1, 3.504), 3.99)
assert.equal(finalPrice(2, 5.001), 5.5)
assert.equal(finalPrice(2, 5.504), 5.99)
assert.equal(finalPrice(.1, .5), .5)
assert.equal(finalPrice(1, 5), 4.99)
assert.equal(finalPrice(1, 1.1), 1.5)
const narrow = rulesFromNodes([{ cost: 10, price: 39.99 }, { cost: 20, price: 41.99 }])
assert.equal(curveError(narrow), null)
assert.ok(finalPrice(15, rawPrice(narrow[0], 15)) <= finalPrice(20, rawPrice(narrow[0], 20)))
assert.ok(curveError(rulesFromNodes([{ cost: 10, price: 40 }, { cost: 20, price: 35 }])))
assert.ok(curveError(rulesFromNodes([{ cost: 10, price: 20 }, { cost: 20, price: 60 }])))
const arc = { ...rules[1], algorithm: 'ArcUp' as const, curveBend: .1 }
assert.equal(curveError([arc]), null)
assert.ok(rawPrice(arc, 125) > rawPrice(rules[1], 125))
assert.ok(curveError([{ ...arc, curveBend: 1 }]))
const lower = { ...rules[1], algorithm: 'ArcDown' as const, curveBend: -.01 }
assert.equal(curveError([lower]), null)
assert.ok(rawPrice(lower, 125) < rawPrice(rules[1], 125))
console.log('定价曲线：99,801 成本点、边界尾数、弧度和倒挂回归测试通过')
