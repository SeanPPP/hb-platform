import {
  ArrowLeftOutlined,
  DeleteOutlined,
  DownloadOutlined,
  FilePdfOutlined,
  PrinterOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Input,
  InputNumber,
  message,
  Modal,
  Segmented,
  Select,
  Space,
  Spin,
  Switch,
  Tag,
  Tooltip,
  Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

// 统一用带埋点的表格封装：仓库门禁禁止页面直接从 antd 引入 Table
import { MeasuredTable } from '../../../components/MeasuredTable'
import { registerPageMessages } from '../../../i18n/registerPageMessages'
import { createPromoPosterPdf, fetchPromoPosterDefaults } from '../../../services/promoPosterService'
import { RequestError } from '../../../utils/request'

import {
  applyMultiBuyOffer,
  applyPosterKind,
  buildPosterPdfFallbackFileName,
  buildPromoPosterPdfRequest,
  canGeneratePosters,
  countPosterPages,
  createLoadingPosterRow,
  createPosterDraft,
  findPosterOffer,
  formatPosterMoney,
  preparePosterProducts,
  PROMO_POSTER_DEFAULTS_CONCURRENCY,
  PROMO_POSTER_KIND_PRIORITY,
  PROMO_POSTER_MAX_COUNT,
  PROMO_POSTER_PRICE_MAX,
  PROMO_POSTER_SIZES,
  resolvePosterKindAvailability,
  resolvePosterPriceMismatch,
  runWithConcurrency,
  summarizePosterBlockers,
  validatePosterDraft,
  type PromoPosterDraft,
  type PromoPosterIssue,
  type PromoPosterKind,
  type PromoPosterProduct,
  type PromoPosterRowState,
  type PromoPosterSize,
  type PromoPosterStyle,
} from './promoPosterLogic'
import promoPosterMessagesEn from './promoPosterMessages.en.json'
import promoPosterMessagesZh from './promoPosterMessages.zh.json'

// 海报文案随门店商品价格页代码块懒注册，不进首屏 i18n 包（页面按钮文案也用这里的 key）
registerPageMessages({ zh: promoPosterMessagesZh, en: promoPosterMessagesEn })

const I18N = 'posAdmin.productPrice.promoPoster'

type ReadyRow = Extract<PromoPosterRowState, { status: 'ready' }>

interface PosterPreview {
  url: string
  fileName: string
  pageCount: number
  posterCount: number
}

interface PromoPosterModalProps {
  storeCode: string
  /** 打开时选中的商品快照；弹窗打开期间列表选择变化不影响这里。 */
  products: PromoPosterProduct[]
  /** 关闭动画结束后回调，由页面卸载本组件（下次打开状态全新）。 */
  onClose: () => void
}

function waitForAnimationFrame() {
  return new Promise<void>((resolve) => {
    window.requestAnimationFrame(() => resolve())
  })
}

/**
 * 打印 iframe 里的 PDF：时序照搬分店订单 printUtils.printPdfFrameAfterLayout（等两帧布局完成再 focus + print）。
 * 不直接 import printUtils：它与发票 / 拣货单共用 print.css 分块，多一个引用方会把该分块拆成纯 CSS 分块，
 * 触发 verify:bundle 的「dependency map 与 manifest 不一致」门禁。
 */
async function printPdfFrame(frame: HTMLIFrameElement) {
  await waitForAnimationFrame()
  await waitForAnimationFrame()
  frame.contentWindow?.focus()
  frame.contentWindow?.print()
}

interface PriceFieldProps {
  label: string
  value: number | null
  invalid: boolean
  onChange: (value: number | null) => void
}

function PriceField({ label, value, invalid, onChange }: PriceFieldProps) {
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
      <Typography.Text type="secondary" style={{ fontSize: 12, whiteSpace: 'nowrap' }}>{label}</Typography.Text>
      <InputNumber<number>
        size="small"
        prefix="$"
        min={0}
        max={PROMO_POSTER_PRICE_MAX}
        precision={2}
        step={0.1}
        value={value}
        status={invalid ? 'error' : undefined}
        style={{ width: 96 }}
        onChange={(next) => onChange(typeof next === 'number' && Number.isFinite(next) ? next : null)}
      />
    </span>
  )
}

/**
 * 门店商品价格页「打印促销海报」批量弹窗：
 * 1. 打开时按 6 并发读取每个商品的海报默认值（门店价 / 折后价 / 清仓价 / 多件价促销 / 建议英文名）；
 * 2. 每行可改类型、英文品名、价格，行级校验不通过时禁止生成；
 * 3. 生成的 PDF 在弹窗内 iframe 预览，可直接打印或下载。
 */
export default function PromoPosterModal({ storeCode, products, onClose }: PromoPosterModalProps) {
  const { t } = useTranslation()
  // 只在挂载时取一次快照：去重并截断到 200 个
  const [initial] = useState(() => preparePosterProducts(products))
  const [open, setOpen] = useState(true)
  const [rows, setRows] = useState<PromoPosterRowState[]>(() => initial.products.map(createLoadingPosterRow))
  const [style, setStyle] = useState<PromoPosterStyle>('classic')
  const [size, setSize] = useState<PromoPosterSize>('A6')
  const [impose, setImpose] = useState(true)
  const [showLogo, setShowLogo] = useState(true)
  const [generating, setGenerating] = useState(false)
  const [preview, setPreview] = useState<PosterPreview | null>(null)
  const [frameReady, setFrameReady] = useState(false)
  const lifetimeRef = useRef<AbortController | null>(null)
  const frameRef = useRef<HTMLIFrameElement | null>(null)

  const kindLabels: Record<PromoPosterKind, string> = useMemo(() => ({
    special: t(`${I18N}.kindSpecial`, '特价'),
    clearance: t(`${I18N}.kindClearance`, '清仓'),
    multibuy: t(`${I18N}.kindMultiBuy`, '多件价'),
    new: t(`${I18N}.kindNew`, '新品'),
  }), [t])

  const describeError = useCallback((status: number | null, rawMessage: string, fallback: string) => {
    if (status === 403) return t(`${I18N}.noStoreAccess`, '没有该分店的权限')
    if (status === 401) return t(`${I18N}.sessionExpired`, '登录已过期，请刷新页面后重新登录')
    return rawMessage || fallback
  }, [t])

  const describeIssue = useCallback((issue: PromoPosterIssue) => {
    switch (issue.code) {
      case 'kindUnavailable':
        return t(`${I18N}.issueKindUnavailable`, '该商品当前不能打印此类型')
      case 'titleRequired':
        return t(`${I18N}.issueTitleRequired`, '请填写英文品名（海报不能印中文）')
      case 'titleUnprintable':
        return t(`${I18N}.issueTitleUnprintable`, '含无法打印的字符：{{chars}}', { chars: issue.chars })
      case 'titleTooLong':
        return t(`${I18N}.issueTitleTooLong`, '英文品名最多 {{max}} 个字符', { max: issue.max })
      case 'priceRequired':
        return t(`${I18N}.issuePriceRequired`, '请填写海报价格')
      case 'priceInvalid':
        return t(`${I18N}.issuePriceInvalid`, '海报价格需大于 0 且不超过 {{max}}', { max: issue.max })
      case 'wasPriceRequired':
        return t(`${I18N}.issueWasPriceRequired`, '请填写原价')
      case 'wasPriceInvalid':
        return t(`${I18N}.issueWasPriceInvalid`, '原价需大于 0 且不超过 {{max}}', { max: issue.max })
      case 'wasPriceNotHigher':
        return t(`${I18N}.issueWasPriceNotHigher`, '原价需高于海报价')
      case 'offerRequired':
        return t(`${I18N}.issueOfferRequired`, '请选择多件价促销')
    }
  }, [t])

  /** 按并发读取默认值；结果只回填仍在列表里的行（期间被移除的行直接丢弃）。 */
  const loadDefaults = useCallback((targets: PromoPosterProduct[]) => {
    const signal = lifetimeRef.current?.signal
    if (!signal || targets.length === 0) return
    void runWithConcurrency(targets, PROMO_POSTER_DEFAULTS_CONCURRENCY, async (product) => {
      let next: PromoPosterRowState
      try {
        const defaults = await fetchPromoPosterDefaults(storeCode, product.productCode, signal)
        next = { key: product.productCode, product, status: 'ready', defaults, draft: createPosterDraft(defaults) }
      } catch (error) {
        next = {
          key: product.productCode,
          product,
          status: 'error',
          errorMessage: error instanceof Error ? error.message : '',
          errorStatus: error instanceof RequestError ? error.status : null,
        }
      }
      if (signal.aborted) return
      setRows((prev) => prev.map((row) => (row.key === product.productCode ? next : row)))
    }, signal)
  }, [storeCode])

  // 挂载即加载；卸载（含 StrictMode 的预演卸载）时取消所有进行中的请求
  useEffect(() => {
    const controller = new AbortController()
    lifetimeRef.current = controller
    loadDefaults(initial.products)
    return () => {
      controller.abort()
      if (lifetimeRef.current === controller) lifetimeRef.current = null
    }
  }, [initial.products, loadDefaults])

  // 预览地址变化或弹窗卸载时释放上一份 PDF 的 Blob URL
  useEffect(() => {
    if (!preview) return undefined
    return () => URL.revokeObjectURL(preview.url)
  }, [preview])

  const updateDraft = useCallback((key: string, update: (row: ReadyRow) => PromoPosterDraft) => {
    setRows((prev) => prev.map((row) => (row.key === key && row.status === 'ready' ? { ...row, draft: update(row) } : row)))
  }, [])

  const removeRow = useCallback((key: string) => {
    setRows((prev) => prev.filter((row) => row.key !== key))
  }, [])

  const retryRow = useCallback((product: PromoPosterProduct) => {
    setRows((prev) => prev.map((row) => (row.key === product.productCode ? createLoadingPosterRow(product) : row)))
    loadDefaults([product])
  }, [loadDefaults])

  const blockers = useMemo(() => summarizePosterBlockers(rows), [rows])
  const canGenerate = canGeneratePosters(blockers)
  const estimatedPages = countPosterPages(rows.length, size, impose)

  const blockerText = useMemo(() => {
    if (blockers.total === 0) return t(`${I18N}.blockerEmpty`, '没有可生成的海报，请关闭后重新选择商品')
    const parts: string[] = []
    if (blockers.loading > 0) parts.push(t(`${I18N}.blockerLoading`, '正在读取 {{count}} 个商品的价格…', { count: blockers.loading }))
    if (blockers.failed > 0) parts.push(t(`${I18N}.blockerFailed`, '{{count}} 个商品读取失败，请重试或移除', { count: blockers.failed }))
    if (blockers.invalid > 0) parts.push(t(`${I18N}.blockerInvalid`, '{{count}} 行未通过校验，请按红色提示修正', { count: blockers.invalid }))
    return parts.join('；')
  }, [blockers, t])

  const columns: ColumnsType<PromoPosterRowState> = useMemo(() => [
    {
      title: t(`${I18N}.columnProduct`, '商品'),
      key: 'product',
      width: 210,
      render: (_: unknown, row) => {
        const name = row.product.productName || (row.status === 'ready' ? row.defaults.productName : '') || row.product.productCode
        const itemNumber = (row.status === 'ready' ? row.defaults.itemNumber : '') || row.product.itemNumber
        return (
          <div style={{ minWidth: 0 }}>
            <Typography.Text ellipsis={{ tooltip: name }} style={{ display: 'block' }}>{name}</Typography.Text>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {itemNumber ? `${t('posAdmin.productPrice.itemNumber', '货号')} ${itemNumber}` : row.product.productCode}
            </Typography.Text>
          </div>
        )
      },
    },
    {
      title: t(`${I18N}.columnKind`, '类型'),
      key: 'kind',
      width: 116,
      render: (_: unknown, row) => {
        if (row.status === 'loading') {
          return <Space size={6}><Spin size="small" /><Typography.Text type="secondary">{t(`${I18N}.rowLoading`, '读取中…')}</Typography.Text></Space>
        }
        if (row.status === 'error') return <Tag color="error">{t(`${I18N}.rowLoadFailed`, '读取失败')}</Tag>
        const availability = resolvePosterKindAvailability(row.defaults)
        return (
          <Select<PromoPosterKind>
            size="small"
            style={{ width: '100%' }}
            value={row.draft.kind}
            options={PROMO_POSTER_KIND_PRIORITY.map((kind) => ({ value: kind, label: kindLabels[kind], disabled: !availability[kind] }))}
            onChange={(kind) => updateDraft(row.key, (current) => applyPosterKind(current.draft, current.defaults, kind))}
          />
        )
      },
    },
    {
      title: t(`${I18N}.columnTitle`, '英文品名（印在海报上）'),
      key: 'title',
      width: 270,
      render: (_: unknown, row) => {
        if (row.status !== 'ready') return null
        const issue = validatePosterDraft(row.draft, row.defaults).find((item) => item.code.startsWith('title'))
        return (
          <div>
            <Input
              size="small"
              value={row.draft.title}
              status={issue ? 'error' : undefined}
              placeholder={t(`${I18N}.titlePlaceholder`, '如 Stainless Steel Mug 350ml')}
              onChange={(event) => {
                const title = event.target.value
                updateDraft(row.key, (current) => ({ ...current.draft, title }))
              }}
            />
            {issue ? <Typography.Text type="danger" style={{ fontSize: 12 }}>{describeIssue(issue)}</Typography.Text> : null}
          </div>
        )
      },
    },
    {
      title: t(`${I18N}.columnPrice`, '海报价格'),
      key: 'price',
      width: 330,
      render: (_: unknown, row) => {
        if (row.status !== 'ready') return null
        const { draft, defaults } = row
        const issues = validatePosterDraft(draft, defaults)
        const priceIssues = issues.filter((item) => item.code.startsWith('price') || item.code.startsWith('wasPrice') || item.code === 'offerRequired' || item.code === 'kindUnavailable')
        const priceInvalid = issues.some((item) => item.code.startsWith('price'))
        const wasInvalid = issues.some((item) => item.code.startsWith('wasPrice'))
        const setPrice = (price: number | null) => updateDraft(row.key, (current) => ({ ...current.draft, price }))
        const setWasPrice = (wasPrice: number | null) => updateDraft(row.key, (current) => ({ ...current.draft, wasPrice }))
        const wasLabel = t(`${I18N}.priceWas`, '原价')
        let fields: ReactNode
        if (draft.kind === 'special' || draft.kind === 'clearance') {
          fields = (
            <Space size={8} wrap>
              <PriceField
                label={draft.kind === 'special' ? t(`${I18N}.priceNow`, '现价') : t(`${I18N}.priceClearance`, '清仓价')}
                value={draft.price}
                invalid={priceInvalid}
                onChange={setPrice}
              />
              <PriceField
                label={draft.kind === 'special' ? t(`${I18N}.priceWasOptional`, '原价（可选）') : wasLabel}
                value={draft.wasPrice}
                invalid={wasInvalid}
                onChange={setWasPrice}
              />
            </Space>
          )
        } else if (draft.kind === 'new') {
          fields = <PriceField label={t(`${I18N}.priceSale`, '售价')} value={draft.price} invalid={priceInvalid} onChange={setPrice} />
        } else {
          const offer = findPosterOffer(draft, defaults)
          fields = (
            <Space size={8} wrap>
              {defaults.multiBuyOffers.length > 1 ? (
                <Select<string>
                  size="small"
                  style={{ width: 150 }}
                  value={draft.offerId ?? undefined}
                  placeholder={t(`${I18N}.offerPlaceholder`, '选择促销')}
                  status={offer ? undefined : 'error'}
                  options={defaults.multiBuyOffers.map((item) => ({
                    value: item.promotionId,
                    label: `${item.applyQuantity} for ${formatPosterMoney(item.fixedPrice)}${item.name ? ` · ${item.name}` : ''}`,
                  }))}
                  onChange={(offerId) => updateDraft(row.key, (current) => applyMultiBuyOffer(current.draft, current.defaults, offerId))}
                />
              ) : null}
              <PriceField
                label={t(`${I18N}.priceBundle`, '{{quantity}} 件组合价', { quantity: offer?.applyQuantity ?? '-' })}
                value={draft.price}
                invalid={priceInvalid}
                onChange={setPrice}
              />
            </Space>
          )
        }
        return (
          <div>
            {fields}
            {priceIssues.length > 0 ? (
              <Typography.Text type="danger" style={{ display: 'block', fontSize: 12 }}>
                {priceIssues.map(describeIssue).join('；')}
              </Typography.Text>
            ) : null}
          </div>
        )
      },
    },
    {
      title: t(`${I18N}.columnHints`, '提示'),
      key: 'hints',
      width: 150,
      render: (_: unknown, row) => {
        if (row.status === 'error') {
          return (
            <Typography.Text type="danger" style={{ fontSize: 12 }}>
              {describeError(row.errorStatus, row.errorMessage, t(`${I18N}.rowLoadFailed`, '读取失败'))}
            </Typography.Text>
          )
        }
        if (row.status !== 'ready') return null
        const mismatch = resolvePosterPriceMismatch(row.draft, row.defaults)
        const offer = row.draft.kind === 'multibuy' ? findPosterOffer(row.draft, row.defaults) : null
        return (
          <Space size={[4, 4]} wrap>
            {mismatch ? (
              // 海报价与收银价不一致只提醒不拦截：店员可能有意手动改价
              <Tag color="warning" style={{ marginInlineEnd: 0, whiteSpace: 'normal' }}>
                {mismatch.kind === 'special'
                  ? t(`${I18N}.mismatchSpecial`, '与系统折后价 {{price}} 不一致', { price: formatPosterMoney(mismatch.expected) })
                  : t(`${I18N}.mismatchClearance`, '与系统清仓价 {{price}} 不一致', { price: formatPosterMoney(mismatch.expected) })}
              </Tag>
            ) : null}
            {offer && offer.productsCount > 1 ? (
              <Tag color="blue" style={{ marginInlineEnd: 0 }}>{t(`${I18N}.mixAndMatch`, '可混搭 Mix & match')}</Tag>
            ) : null}
          </Space>
        )
      },
    },
    {
      title: t(`${I18N}.columnActions`, '操作'),
      key: 'actions',
      width: 76,
      align: 'center',
      // 固定在右侧：窄屏出现横向滚动时，删除 / 重试按钮仍然可见
      fixed: 'right',
      render: (_: unknown, row) => (
        <Space size={0}>
          {row.status === 'error' ? (
            <Tooltip title={t('common.retry', '重试')}>
              <Button type="text" size="small" icon={<ReloadOutlined />} aria-label={t('common.retry', '重试')} onClick={() => retryRow(row.product)} />
            </Tooltip>
          ) : null}
          <Tooltip title={t('common.remove', '移除')}>
            <Button type="text" size="small" danger icon={<DeleteOutlined />} aria-label={t('common.remove', '移除')} onClick={() => removeRow(row.key)} />
          </Tooltip>
        </Space>
      ),
    },
  ], [t, kindLabels, describeIssue, describeError, updateDraft, retryRow, removeRow])

  const handleGenerate = async () => {
    if (!canGenerate || generating) return
    const signal = lifetimeRef.current?.signal
    const body = buildPromoPosterPdfRequest(storeCode, rows, {
      style,
      size,
      impose,
      showLogo,
      today: dayjs().format('YYYY-MM-DD'),
    })
    setGenerating(true)
    try {
      const result = await createPromoPosterPdf(body, signal)
      if (signal?.aborted) return
      setFrameReady(false)
      setPreview({
        url: URL.createObjectURL(result.blob),
        fileName: result.fileName ?? buildPosterPdfFallbackFileName(new Date()),
        // 跨域部署时自定义响应头可能读不到，用本地估算兜底
        pageCount: result.pageCount ?? estimatedPages,
        posterCount: body.posters.length,
      })
    } catch (error) {
      if (signal?.aborted) return
      message.error(describeError(
        error instanceof RequestError ? error.status : null,
        error instanceof Error ? error.message : '',
        t(`${I18N}.generateFailed`, '生成海报 PDF 失败'),
      ))
    } finally {
      if (!signal?.aborted) setGenerating(false)
    }
  }

  const handlePrint = async () => {
    const frame = frameRef.current
    if (!frame || !frameReady) return
    try {
      // 直接打印已完成布局的预览 iframe（同源 Blob URL）
      await printPdfFrame(frame)
    } catch (error) {
      console.error(error)
      message.error(t(`${I18N}.printFailed`, '无法调起打印，请先下载 PDF 再打印'))
    }
  }

  const handleDownload = () => {
    if (!preview) return
    const link = document.createElement('a')
    link.href = preview.url
    link.download = preview.fileName
    link.style.display = 'none'
    document.body.appendChild(link)
    link.click()
    link.remove()
  }

  const backToEdit = () => {
    setPreview(null)
    setFrameReady(false)
  }

  const sizeOptions = PROMO_POSTER_SIZES.map((value) => ({ value, label: value }))
  const styleOptions: { value: PromoPosterStyle; label: string }[] = [
    { value: 'classic', label: t(`${I18N}.styleClassic`, '经典') },
    { value: 'modern', label: t(`${I18N}.styleModern`, '现代') },
  ]

  const editingFooter = (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
      <Typography.Text
        type={blockers.failed > 0 || blockers.invalid > 0 || blockers.total === 0 ? 'danger' : 'secondary'}
        style={{ flex: '1 1 240px', textAlign: 'left', fontSize: 12 }}
      >
        {canGenerate ? t(`${I18N}.readyHint`, '校验通过；标黄的行表示海报价与系统价不一致，仅提醒不影响生成') : blockerText}
      </Typography.Text>
      <Button onClick={() => setOpen(false)}>{t('common.cancel', '取消')}</Button>
      <Button type="primary" icon={<FilePdfOutlined />} disabled={!canGenerate} loading={generating} onClick={() => void handleGenerate()}>
        {t(`${I18N}.generate`, '生成 PDF')}
      </Button>
    </div>
  )

  const previewFooter = (
    <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, flexWrap: 'wrap' }}>
      <Button icon={<ArrowLeftOutlined />} onClick={backToEdit}>{t(`${I18N}.backToEdit`, '返回修改')}</Button>
      <Button icon={<DownloadOutlined />} onClick={handleDownload}>{t('common.download', '下载')}</Button>
      <Button type="primary" icon={<PrinterOutlined />} disabled={!frameReady} onClick={() => void handlePrint()}>
        {t('common.print', '打印')}
      </Button>
    </div>
  )

  return (
    <Modal
      open={open}
      title={t(`${I18N}.title`, '打印促销海报（分店 {{storeCode}}）', { storeCode })}
      width="min(1200px, calc(100vw - 32px))"
      onCancel={() => setOpen(false)}
      afterClose={onClose}
      maskClosable={false}
      footer={preview ? previewFooter : editingFooter}
    >
      {preview ? (
        <Space direction="vertical" size={10} style={{ width: '100%' }}>
          <Typography.Text>
            {t(`${I18N}.previewSummary`, '已生成 {{count}} 张海报，共 {{pages}} 页', { count: preview.posterCount, pages: preview.pageCount })}
          </Typography.Text>
          <Alert
            type="info"
            showIcon
            message={t(`${I18N}.printScaleHint`, '打印时缩放请选「实际大小 / 100%」，否则海报会被缩小、裁切线错位')}
          />
          <iframe
            ref={frameRef}
            src={preview.url}
            title={t(`${I18N}.previewFrameTitle`, '促销海报 PDF 预览')}
            style={{ width: '100%', height: 'min(70vh, 760px)', border: '1px solid rgba(5, 5, 5, 0.06)', borderRadius: 6 }}
            onLoad={() => setFrameReady(true)}
          />
        </Space>
      ) : (
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          {initial.truncatedCount > 0 ? (
            <Alert
              type="warning"
              showIcon
              message={t(`${I18N}.truncated`, '一次最多生成 {{max}} 张海报，已忽略超出的 {{count}} 个商品', {
                max: PROMO_POSTER_MAX_COUNT,
                count: initial.truncatedCount,
              })}
            />
          ) : null}
          {/* 全局设置：同一批海报共用风格与尺寸，页数随尺寸 / 拼版实时估算 */}
          <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '8px 20px' }}>
            <Space size={8}>
              <Typography.Text type="secondary">{t(`${I18N}.style`, '风格')}</Typography.Text>
              <Segmented size="small" value={style} options={styleOptions} onChange={(value) => setStyle(value as PromoPosterStyle)} />
            </Space>
            <Space size={8}>
              <Typography.Text type="secondary">{t(`${I18N}.size`, '尺寸')}</Typography.Text>
              <Segmented size="small" value={size} options={sizeOptions} onChange={(value) => setSize(value as PromoPosterSize)} />
            </Space>
            <Tooltip title={size === 'A4' ? t(`${I18N}.imposeA4Hint`, 'A4 海报每页一张，无需拼版') : undefined}>
              <Space size={8}>
                <Switch size="small" checked={impose} disabled={size === 'A4'} onChange={setImpose} />
                <Typography.Text type={size === 'A4' ? 'secondary' : undefined}>{t(`${I18N}.impose`, '小尺寸拼到 A4 纸（带裁切线）')}</Typography.Text>
              </Space>
            </Tooltip>
            <Space size={8}>
              <Switch
                size="small"
                checked={showLogo}
                onChange={setShowLogo}
                aria-label={t(`${I18N}.showLogo`, '显示 Logo')}
              />
              <Typography.Text>{t(`${I18N}.showLogo`, '显示 Logo')}</Typography.Text>
            </Space>
            <Typography.Text strong style={{ marginLeft: 'auto' }}>
              {t(`${I18N}.pageEstimate`, '预计 {{pages}} 页 · {{count}} 张海报', { pages: estimatedPages, count: rows.length })}
            </Typography.Text>
          </div>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {t(`${I18N}.englishOnlyHint`, '海报内容为英文：品名只能使用英文字母、数字和常见标点；没有英文名的商品需手动填写。')}
          </Typography.Text>
          <MeasuredTable<PromoPosterRowState>
            metricId="pos-admin.store-product-price.promo-poster-table"
            rowKey="key"
            size="small"
            columns={columns}
            dataSource={rows}
            pagination={false}
            virtual
            scroll={{ x: 1152, y: 440 }}
          />
        </Space>
      )}
    </Modal>
  )
}
