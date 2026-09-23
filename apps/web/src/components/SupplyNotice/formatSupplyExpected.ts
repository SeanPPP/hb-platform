import dayjs from 'dayjs'
import type { TFunction } from 'i18next'
import type { SupplyExpectedPrecision } from '../../types/supplyNotice'

export interface SupplyExpectedLike {
  expectedFrom: string | null
  expectedTo: string | null
  expectedPrecision: SupplyExpectedPrecision
  isOverdue: boolean
}

/**
 * 把“预计恢复订货”的时间段按录入精度还原成人话：
 * 某日 → 10月5日；范围 → 10月5日 – 10月10日；某月 → 10 月（跨年时带年份）；待定 / 逾期 → 对应提示。
 */
export function formatSupplyExpected(value: SupplyExpectedLike, t: TFunction): string {
  if (value.isOverdue) {
    return t('supplyNotice.expectedOverdue')
  }
  const from = value.expectedFrom ? dayjs(value.expectedFrom) : null
  const to = value.expectedTo ? dayjs(value.expectedTo) : null
  if (!from || !from.isValid()) {
    return t('supplyNotice.expectedUnknown')
  }

  const thisYear = dayjs().year()
  const dayFormat = from.year() === thisYear ? 'M月D日' : 'YYYY年M月D日'
  switch (value.expectedPrecision) {
    case 'Month':
      return from.year() === thisYear
        ? t('supplyNotice.expectedMonth', { month: from.month() + 1 })
        : t('supplyNotice.expectedMonthWithYear', { year: from.year(), month: from.month() + 1 })
    case 'Range':
      if (to && to.isValid() && !to.isSame(from, 'day')) {
        return t('supplyNotice.expectedRange', { from: from.format(dayFormat), to: to.format(dayFormat) })
      }
      return from.format(dayFormat)
    case 'Day':
      return from.format(dayFormat)
    default:
      return t('supplyNotice.expectedUnknown')
  }
}
