export type ShopPreorderNavigationResolution =
  | { action: 'open'; activationGuid: string }
  | { action: 'refresh' }
  | { action: 'empty' }
  | { action: 'select-store' }

interface ShopPreorderNavigationState {
  storeCode?: string | null
  activationGuid?: string | null
  loading: boolean
  error: boolean
}

export type PreorderPromptPresentation =
  | { mode: 'hidden'; key: '' }
  | { mode: 'checking' | 'error' | 'pending'; key: string }

interface PreorderPromptState {
  storeCode?: string | null
  activationGuids: string[]
  loading: boolean
  error: boolean
  bypassed: boolean
  onPreorderPage: boolean
}

/** 把固定导航入口映射为明确动作，查询异常时仍保留可重试入口。 */
export function resolveShopPreorderNavigation(
  state: ShopPreorderNavigationState,
): ShopPreorderNavigationResolution {
  if (!state.storeCode) return { action: 'select-store' }
  if (state.activationGuid) return { action: 'open', activationGuid: state.activationGuid }
  if (state.loading || state.error) return { action: 'refresh' }
  return { action: 'empty' }
}

/** 首屏只依赖分店与轻量门禁状态决定弹窗，不依赖商品、分类或购物车加载。 */
export function resolvePreorderPromptPresentation(
  state: PreorderPromptState,
): PreorderPromptPresentation {
  if (!state.storeCode || state.bypassed || state.onPreorderPage) {
    return { mode: 'hidden', key: '' }
  }
  if (state.loading) return { mode: 'checking', key: `checking:${state.storeCode}` }
  if (state.error) return { mode: 'error', key: `error:${state.storeCode}` }
  if (state.activationGuids.length) {
    return {
      mode: 'pending',
      key: `pending:${state.storeCode}:${state.activationGuids.join(',')}`,
    }
  }
  return { mode: 'hidden', key: '' }
}
