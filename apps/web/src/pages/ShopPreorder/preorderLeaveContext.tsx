import { createContext, useCallback, useContext, useMemo, useRef, type ReactNode } from 'react'

export type PreorderDurableLeaveHandler = () => Promise<boolean>

interface PreorderLeaveContextValue {
  registerPreorderDurableLeave: (handler: PreorderDurableLeaveHandler) => () => void
  requestPreorderDurableLeave: () => Promise<boolean>
}

const PreorderLeaveContext = createContext<PreorderLeaveContextValue | null>(null)

export function ShopPreorderLeaveProvider({ children }: { children: ReactNode }) {
  const handlerRef = useRef<PreorderDurableLeaveHandler | null>(null)
  const registerPreorderDurableLeave = useCallback((handler: PreorderDurableLeaveHandler) => {
    handlerRef.current = handler
    return () => {
      if (handlerRef.current === handler) handlerRef.current = null
    }
  }, [])
  const requestPreorderDurableLeave = useCallback(async () => {
    return handlerRef.current ? handlerRef.current() : true
  }, [])
  const value = useMemo(() => ({ registerPreorderDurableLeave, requestPreorderDurableLeave }), [
    registerPreorderDurableLeave,
    requestPreorderDurableLeave,
  ])
  return <PreorderLeaveContext.Provider value={value}>{children}</PreorderLeaveContext.Provider>
}

export function usePreorderLeave() {
  const value = useContext(PreorderLeaveContext)
  if (!value) throw new Error('usePreorderLeave 必须在 ShopPreorderLeaveProvider 内使用')
  return value
}

export async function runAfterDurableLeave(
  requestDurableLeave: () => Promise<boolean>,
  action: () => void | Promise<void>,
) {
  if (!await requestDurableLeave()) return false
  await action()
  return true
}

export async function changeStoreAfterDurableLeave<T>(
  nextStoreCode: T,
  requestDurableLeave: () => Promise<boolean>,
  commitStoreChange: (storeCode: T) => void,
) {
  return runAfterDurableLeave(requestDurableLeave, () => commitStoreChange(nextStoreCode))
}
