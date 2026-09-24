import { BellOutlined, CheckOutlined, ShoppingCartOutlined } from '@ant-design/icons'
import { Button, Card, Image, Space, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { useTranslation } from 'react-i18next'
import { registerPageMessages } from '../../i18n/registerPageMessages'
import type { StoreSupplyStatus } from '../../types/supplyNotice'
import { formatSupplyExpected } from './formatSupplyExpected'
import { supplyNoticeMessages, supplyStatusCardMessages } from './supplyNoticeMessages'

registerPageMessages(supplyNoticeMessages)
registerPageMessages(supplyStatusCardMessages)

const { Text } = Typography

export interface SupplyStatusCardProps {
  status: StoreSupplyStatus
  /** 关注 / 取消关注进行中。 */
  busy?: boolean
  onWatch?: (status: StoreSupplyStatus) => void
  onUnwatch?: (status: StoreSupplyStatus) => void
  /** 已恢复订货时：确认提醒。 */
  onAcknowledge?: (status: StoreSupplyStatus) => void
  /** 已恢复订货时：去订货（跳到商品）。 */
  onOrder?: (status: StoreSupplyStatus) => void
  compact?: boolean
}

/**
 * 分店端的商品供货状态卡：搜索 / 扫码零结果、关注列表共用。
 * 回答三个问题——还会不会有、什么时候能订、现在能做什么（关注 / 去订货）。
 */
export default function SupplyStatusCard({ status, busy, onWatch, onUnwatch, onAcknowledge, onOrder, compact }: SupplyStatusCardProps) {
  const { t } = useTranslation()
  const restocked = status.isOrderable
  const showExpected = !restocked && status.supplyPlan !== 'Discontinued'
  const expectedText = formatSupplyExpected(status, t)
  const title = restocked ? t('supplyNotice.restocked') : t(`supplyNotice.storeTitle.${status.supplyPlan}`)
  const tagColor = restocked ? 'success' : status.supplyPlan === 'Discontinued' ? 'default' : status.supplyPlan === 'WillRestock' ? 'processing' : 'warning'

  return (
    <Card size="small" className="supply-status-card" data-testid="supply-status-card" data-product-code={status.productCode}>
      <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start' }}>
        <Image
          src={status.productImage || 'https://via.placeholder.com/96x96?text=No+Image'}
          width={compact ? 56 : 72}
          height={compact ? 56 : 72}
          style={{ objectFit: 'cover', borderRadius: 6, flexShrink: 0 }}
          preview={false}
          alt={status.productName ?? status.productCode}
        />
        <div style={{ flex: 1, minWidth: 0 }}>
          <Text strong ellipsis style={{ display: 'block' }}>{status.productName || status.productCode}</Text>
          <Text type="secondary" style={{ fontSize: 12 }}>{status.itemNumber || status.productCode}</Text>
          <div style={{ marginTop: 6 }}>
            <Tag color={tagColor} style={{ marginInlineEnd: 0 }}>{title}</Tag>
          </div>
          {showExpected ? (
            <div style={{ marginTop: 6, fontSize: 13 }}>
              <Text type="secondary">{t('supplyNotice.expectedLabel')}：</Text>
              <Text type={status.isOverdue ? 'warning' : undefined}>{expectedText}</Text>
            </div>
          ) : null}
          {!restocked && status.storeFacingNote ? (
            <div style={{ marginTop: 4, fontSize: 13 }}>
              <Text type="secondary">{t('supplyNotice.noteLabel')}：</Text>
              <Text>{status.storeFacingNote}</Text>
            </div>
          ) : null}
          {!restocked && status.noticeUpdatedAtUtc ? (
            <Text type="secondary" style={{ display: 'block', marginTop: 4, fontSize: 12 }}>
              {t('supplyNotice.updatedAt', { date: dayjs(status.noticeUpdatedAtUtc).format('M月D日') })}
            </Text>
          ) : null}
          <Space size={8} style={{ marginTop: 10 }} wrap>
            {restocked ? (
              <>
                {onOrder ? (
                  <Button size="small" type="primary" icon={<ShoppingCartOutlined />} onClick={() => onOrder(status)}>
                    {t('supplyStatusCard.goOrder')}
                  </Button>
                ) : null}
                {onAcknowledge ? (
                  <Button size="small" icon={<CheckOutlined />} loading={busy} onClick={() => onAcknowledge(status)}>
                    {t('supplyStatusCard.acknowledge')}
                  </Button>
                ) : null}
              </>
            ) : status.isWatching ? (
              onUnwatch ? (
                <Button size="small" loading={busy} onClick={() => onUnwatch(status)}>
                  {t('supplyStatusCard.watching')}
                </Button>
              ) : null
            ) : (
              onWatch ? (
                <Button size="small" type="primary" ghost icon={<BellOutlined />} loading={busy} onClick={() => onWatch(status)}>
                  {t('supplyStatusCard.watch')}
                </Button>
              ) : null
            )}
          </Space>
        </div>
      </div>
    </Card>
  )
}
