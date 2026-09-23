import { message } from 'antd'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { lookupStoreSupplyStatus, unwatchStoreSupply, watchStoreSupply } from '../../services/supplyNoticeService'
import type { StoreSupplyStatus } from '../../types/supplyNotice'

/**
 * 搜索 / 扫码零结果时的补充查询：按输入的码精确查暂停供货商品，并提供关注 / 取消关注。
 * 只在零结果时调用，正常命中的列表完全不受影响。
 */
export function useSupplyStatusLookup(storeCode: string | null) {
  const { t } = useTranslation()
  const [items, setItems] = useState<StoreSupplyStatus[]>([])
  const [lookedUpCode, setLookedUpCode] = useState<string | null>(null)
  const [busyCode, setBusyCode] = useState<string | null>(null)
  const requestSeq = useRef(0)

  const lookup = useCallback(async (code: string | null | undefined) => {
    const trimmed = code?.trim() ?? ''
    const seq = ++requestSeq.current
    if (!storeCode || !trimmed) {
      setItems([])
      setLookedUpCode(null)
      return []
    }
    try {
      const result = await lookupStoreSupplyStatus(storeCode, trimmed)
      // 只接受最后一次请求的结果，避免快速连续扫码时旧结果覆盖新结果。
      if (seq !== requestSeq.current) {
        return []
      }
      setItems(result)
      setLookedUpCode(trimmed)
      return result
    } catch (error) {
      console.error(error)
      if (seq === requestSeq.current) {
        setItems([])
        setLookedUpCode(null)
      }
      return []
    }
  }, [storeCode])

  const clear = useCallback(() => {
    requestSeq.current += 1
    setItems([])
    setLookedUpCode(null)
  }, [])

  // 切换分店后关注状态不再可信，清空。
  useEffect(() => {
    clear()
  }, [storeCode, clear])

  const toggleWatch = useCallback(async (status: StoreSupplyStatus, watch: boolean) => {
    if (!storeCode) {
      return
    }
    setBusyCode(status.productCode)
    try {
      if (watch) {
        await watchStoreSupply(storeCode, status.productCode)
      } else {
        await unwatchStoreSupply(storeCode, status.productCode)
      }
      setItems((current) => current.map((item) => (item.productCode === status.productCode ? { ...item, isWatching: watch } : item)))
      message.success(t(watch ? 'supplyStatusCard.watchDone' : 'supplyStatusCard.unwatchDone'))
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('supplyStatusCard.actionFailed'))
    } finally {
      setBusyCode(null)
    }
  }, [storeCode, t])

  return { items, lookedUpCode, busyCode, lookup, clear, toggleWatch }
}
