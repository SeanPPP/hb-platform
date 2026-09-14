import { Alert, Button, InputNumber, Select, Slider, Space, Tag, Typography } from 'antd'
import { useMemo, useRef, useState } from 'react'
import type { PricingStrategyRuleDto } from '../../../types/pricingStrategy'
import { curveError, finalPrice, nodesFromRules, rawPrice, roundMoney, rulesFromNodes, safeBend, tailCeil, tailFloor } from './pricingCurve'

interface Props { value?: PricingStrategyRuleDto[]; onChange?: (rules: PricingStrategyRuleDto[]) => void }
export default function PricingCurveEditor({ value = [], onChange }: Props) {
  const nodes = useMemo(() => nodesFromRules(value), [value])
  const [selected, setSelected] = useState(0)
  const [zoom, setZoom] = useState(100)
  const [notice, setNotice] = useState('')
  const [hover, setHover] = useState<{ cost: number; price: number } | null>(null)
  const drag = useRef<number | null>(null)
  const svg = useRef<SVGSVGElement>(null)
  if (nodes.length < 2) return <Alert type="error" message="至少需要两个定价节点" />
  const index = Math.min(selected, nodes.length - 1), node = nodes[index]
  const first = nodes[0].cost, last = nodes[nodes.length - 1].cost
  const end = first + (last - first) * zoom / 100, maxY = end * 5
  const X = (x: number) => 60 + (x - first) / (end - first) * 820
  const Y = (y: number) => 330 - y / maxY * 295
  const samples = Array.from({ length: 501 }, (_, i) => {
    const cost = first + (end - first) * i / 500
    const rule = value.find(r => cost >= r.minPrice && cost <= r.maxPrice) ?? value[value.length - 1]
    const price = rawPrice(rule, cost)
    return { cost, price, final: finalPrice(cost, price) }
  })
  const line = samples.map((p, i) => `${i ? 'L' : 'M'}${X(p.cost)},${Y(p.price)}`).join(' ')
  const steps = samples.map((p, i) => i ? `H${X(p.cost)} V${Y(p.final)}` : `M${X(p.cost)},${Y(p.final)}`).join(' ')
  const apply = (next: PricingStrategyRuleDto[]) => {
    const error = curveError(next)
    if (error) { setNotice(error); return false }
    setNotice(''); onChange?.(next); return true
  }
  const updateNode = (i: number, cost: number, wanted: number) => {
    const previous = nodes[i - 1], next = nodes[i + 1]
    if (cost < .1 || (previous && cost <= previous.cost) || (next && cost >= next.cost)) { setNotice('节点成本必须严格递增，且不低于 0.10 元'); return }
    const lo = Math.max(1.5 * cost, previous?.price ?? 0, next ? cost * next.price / next.cost : 0)
    const hi = Math.min(5 * cost, next?.price ?? Infinity, previous ? cost * previous.price / previous.cost : Infinity)
    const bottom = tailCeil(lo), top = tailFloor(hi)
    if (bottom > top + 1e-8) { setNotice('该位置没有满足成率和相邻节点约束的尾数售价'); return }
    const price = roundMoney(Math.min(top, Math.max(bottom, finalPrice(cost, wanted))))
    const changed = nodes.map((n, j) => j === i ? { cost, price } : n)
    apply(rulesFromNodes(changed, value))
  }
  const segment = Math.min(index, value.length - 1), rule = value[segment]
  const d = (rule.endRetailPrice ?? 0) - (rule.startRetailPrice ?? 0)
  const c = rule.minPrice * (rule.endRetailPrice ?? 0) - rule.maxPrice * (rule.startRetailPrice ?? 0)
  const limit = d > 0 ? Math.max(0, Math.min(1, rule.algorithm === 'ArcDown' ? -c / rule.maxPrice / d : -c / rule.minPrice / d)) : 0
  const setShape = (algorithm: 'Linear' | 'ArcUp' | 'ArcDown') => apply(value.map((r, i) => i === segment ? { ...r, algorithm, curveBend: 0 } : r))
  const pointerValue = (clientY: number) => {
    const box = svg.current!.getBoundingClientRect()
    return (330 - (clientY - box.top) * 385 / box.height) / 295 * maxY
  }
  return <Space direction="vertical" size={12} style={{ width: '100%' }}>
    <Alert type="info" showIcon message="拖动节点设置售价，选择区间调整直线或弧线" description="实线为理论售价，橙色阶梯为尾数调整后的实际售价。同一策略覆盖范围内，实际售价随成本不下降，成率保持 1.5～5；尾数跳档时实际成率可能小幅上升。" />
    <Space wrap><Tag color="blue">理论售价</Tag><Tag color="orange">实际售价</Tag><Tag>成本基线</Tag><Tag color="green">1.5～5 倍范围</Tag></Space>
    <svg ref={svg} viewBox="0 0 920 385" role="img" aria-label="成本与零售价曲线，可通过下方数字输入编辑节点" style={{ width: '100%', touchAction: 'none', background: '#fff', border: '1px solid #f0f0f0', borderRadius: 6 }} onPointerMove={e => {
      if (drag.current !== null) { updateNode(drag.current, nodes[drag.current].cost, pointerValue(e.clientY)); return }
      const box = e.currentTarget.getBoundingClientRect(), cost = first + Math.max(0, Math.min(1, ((e.clientX - box.left) * 920 / box.width - 60) / 820)) * (end - first)
      const r = value.find(item => cost >= item.minPrice && cost <= item.maxPrice)
      if (r) setHover({ cost, price: finalPrice(cost, rawPrice(r, cost)) })
    }} onPointerUp={() => { drag.current = null }} onPointerCancel={() => { drag.current = null }} onPointerLeave={() => setHover(null)}>
      <defs><clipPath id="pricing-curve-plot"><rect x="60" y="30" width="820" height="300" /></clipPath></defs>
      {[0, 1, 2, 3, 4].map(i => <g key={i}><line x1="60" x2="880" y1={Y(maxY * i / 4)} y2={Y(maxY * i / 4)} stroke="#f0f0f0"/><text x="52" y={Y(maxY * i / 4) + 4} textAnchor="end" fontSize="11" fill="#666">{(maxY * i / 4).toFixed(0)}</text><text x={60 + 820 * i / 4} y="350" textAnchor="middle" fontSize="11" fill="#666">{(first + (end - first) * i / 4).toFixed(2)}</text></g>)}
      <text x="12" y="18" fontSize="12">零售价（元）</text><text x="800" y="376" fontSize="12">成本（元）</text>
      <g clipPath="url(#pricing-curve-plot)"><path d={`M${X(first)},${Y(first * 1.5)} L${X(end)},${Y(end * 1.5)} L${X(end)},${Y(end * 5)} L${X(first)},${Y(first * 5)} Z`} fill="#f0f9ef"/><path d={`M${X(first)},${Y(first)} L${X(end)},${Y(end)}`} stroke="#999" strokeDasharray="5 5"/><path d={steps} fill="none" stroke="#d46b08" strokeWidth="1.8"/><path d={line} fill="none" stroke="#1677ff" strokeWidth="2"/>
      {nodes.map((n, i) => n.cost <= end && <circle key={i} cx={X(n.cost)} cy={Y(n.price)} r={index === i ? 8 : 6} fill="white" stroke="#1677ff" strokeWidth="3" tabIndex={0} role="slider" aria-label={`节点 ${i + 1} 售价`} aria-valuemin={n.cost * 1.5} aria-valuemax={n.cost * 5} aria-valuenow={n.price} onFocus={() => setSelected(i)} onKeyDown={e => { if (e.key === 'ArrowUp' || e.key === 'ArrowDown') { e.preventDefault(); updateNode(i, n.cost, n.price + (e.key === 'ArrowUp' ? .51 : -.51)) } }} onPointerDown={e => { setSelected(i); drag.current = i; e.currentTarget.setPointerCapture(e.pointerId) }} style={{ cursor: 'ns-resize' }}><title>{`成本 ${n.cost.toFixed(2)} · 售价 ${n.price.toFixed(2)} · 成率 ${(n.price / n.cost).toFixed(4)}`}</title></circle>)}
      </g>
    </svg>
    <Typography.Text style={{ minHeight: 22 }}>{hover ? `成本 ${hover.cost.toFixed(2)} 元　实际售价 ${hover.price.toFixed(2)} 元　成率 ${(hover.price / hover.cost).toFixed(4)}` : '选择节点可输入精确值；使用 ↑ ↓ 键调整售价。'}</Typography.Text>
    <Space wrap><span>显示成本范围</span><Slider ariaLabelForHandle="成本范围缩放" min={1} max={100} value={zoom} onChange={setZoom} style={{ width: 180 }} /><span>{first.toFixed(2)}～{end.toFixed(2)}</span><Button size="small" onClick={() => setZoom(100)}>显示全部</Button></Space>
    {notice && <Alert type="warning" showIcon message={notice} />}
    <Space wrap align="center">
      <Select aria-label="定价节点" value={index} onChange={setSelected} options={nodes.map((n, i) => ({ value: i, label: `节点 ${i + 1} · 成本 ${n.cost.toFixed(2)}` }))} style={{ width: 200 }}/>
      <label>成本 <InputNumber aria-label="节点成本" min={.1} precision={2} value={node.cost} onChange={v => v !== null && updateNode(index, v, node.price)} /></label>
      <label>零售价 <InputNumber aria-label="节点零售价" min={node.cost * 1.5} max={node.cost * 5} precision={2} value={node.price} onChange={v => v !== null && updateNode(index, node.cost, v)} /></label>
      <Tag>成率 {(node.price / node.cost).toFixed(4)}</Tag>
      <Button disabled={index === 0 || index === nodes.length - 1} danger onClick={() => { const kept = value.filter((_, i) => i !== index); kept[index - 1] = { ...kept[index - 1], algorithm: 'Linear', curveBend: 0 }; if (apply(rulesFromNodes(nodes.filter((_, i) => i !== index), kept))) setSelected(Math.max(0, index - 1)) }}>删除节点</Button>
      <Button onClick={() => {
        const r = value[segment], cost = roundMoney((r.minPrice + r.maxPrice) / 2)
        const added = [...nodes]; added.splice(segment + 1, 0, { cost, price: finalPrice(cost, rawPrice(r, cost)) })
        const split = [...value]; split.splice(segment, 1, { ...r, algorithm: 'Linear', curveBend: 0 }, { ...r, algorithm: 'Linear', curveBend: 0 }); if (apply(rulesFromNodes(added, split))) setSelected(segment + 1)
      }}>在本区间添加节点</Button><Typography.Text type="secondary">增删节点仅将相邻区间重置为直线</Typography.Text>
    </Space>
    <Space wrap><span>区间 {segment + 1}：{rule.minPrice}～{rule.maxPrice}</span><Select aria-label="曲线形状" value={rule.algorithm as 'Linear' | 'ArcUp' | 'ArcDown'} onChange={setShape} style={{ width: 150 }} options={[{ value: 'Linear', label: '直线' }, { value: 'ArcUp', label: '上弧线（向上拱）' }, { value: 'ArcDown', label: '下弧线（向下拱）' }]} />
      <span>弧度</span><Slider ariaLabelForHandle="区间弧度" disabled={rule.algorithm === 'Linear' || limit === 0} min={0} max={100} value={limit ? Math.abs(rule.curveBend ?? 0) / limit * 100 : 0} onChange={v => apply(value.map((r, i) => i === segment ? { ...r, curveBend: safeBend((rule.algorithm === 'ArcDown' ? -1 : 1) * limit * v / 100) } : r))} style={{ width: 180 }} /><span>已限制在安全弧度内</span>
    </Space>
  </Space>
}
