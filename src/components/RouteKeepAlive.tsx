import { KeepAlive, useKeepAliveRef } from 'keepalive-for-react'
import { forwardRef, useImperativeHandle } from 'react'
import type { ReactElement } from 'react'

export interface RouteKeepAliveRef {
  refresh: (cacheKey?: string) => void
  destroy: (cacheKey?: string | string[]) => Promise<void>
  destroyOther: (cacheKey?: string) => Promise<void>
}

interface RouteKeepAliveProps {
  activeKey: string
  include: string[]
  currentElement: ReactElement
}

const RouteKeepAlive = forwardRef<RouteKeepAliveRef, RouteKeepAliveProps>(function RouteKeepAlive(
  { activeKey, include, currentElement },
  ref,
) {
  const aliveRef = useKeepAliveRef()

  useImperativeHandle(ref, () => ({
    refresh: (cacheKey) => {
      aliveRef.current?.refresh(cacheKey)
    },
    destroy: async (cacheKey) => {
      await aliveRef.current?.destroy(cacheKey)
    },
    destroyOther: async (cacheKey) => {
      await aliveRef.current?.destroyOther(cacheKey)
    },
  }))

  return (
    <KeepAlive
      aliveRef={aliveRef}
      activeCacheKey={activeKey}
      include={include}
      max={12}
      transition={false}
      containerClassName="route-keepalive-container"
      cacheNodeClassName="route-keepalive-node"
    >
      {currentElement}
    </KeepAlive>
  )
})

export default RouteKeepAlive
