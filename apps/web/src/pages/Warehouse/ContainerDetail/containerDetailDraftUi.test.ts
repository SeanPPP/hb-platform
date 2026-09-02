import { readFileSync } from 'node:fs'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const pageSource = readFileSync('src/pages/Warehouse/ContainerDetail/index.tsx', 'utf8')
const styleSource = readFileSync('src/pages/Warehouse/ContainerDetail/index.css', 'utf8')
const restoreDraftSource = pageSource.slice(
  pageSource.indexOf('const restorePendingDetailDraft = () => {'),
  pageSource.indexOf('useEffect(() => {', pageSource.indexOf('const restorePendingDetailDraft = () => {')),
)
const resetDetailSource = pageSource.slice(
  pageSource.indexOf('const isContainerChange = detailRowsContainerGuidRef.current !== containerGuid'),
  pageSource.indexOf('setSelectedRowKeys([])', pageSource.indexOf('const isContainerChange = detailRowsContainerGuidRef.current !== containerGuid')),
)

assertEqual(
  pageSource.includes("const currentUserGuid = useAuthStore((state) => state.currentUser?.userGUID ?? '')")
    && pageSource.includes('readContainerDetailDraft(')
    && pageSource.includes('writeContainerDetailDraft(')
    && pageSource.includes('currentUserGuid,')
    && pageSource.includes('containerGuid,'),
  true,
  '页面应按当前用户和货柜恢复与持久化 localStorage 草稿',
)
assertEqual(
  !restoreDraftSource.includes('detailRowsContainerGuidRef.current = containerGuid')
    && restoreDraftSource.includes("detailRowsContainerGuidRef.current = ''")
    && restoreDraftSource.includes('setRows([])')
    && resetDetailSource.includes('if (isContainerChange)')
    && resetDetailSource.includes('setRows([])')
    && pageSource.includes('scopeContainerDetailRowsToContainer(rows, detailRowsContainerGuidRef.current, containerGuid)'),
  true,
  '草稿恢复不得伪造行已切换；A 到 B 时应立即隐藏并清空 A 行，B 加载失败也不得继续编辑 A',
)
assertEqual(
  pageSource.includes('}, [active, activeLoadQueryKey, currentUserGuid])'),
  true,
  '重新登录后即使路由货柜未变，也应重新加载服务端行并叠加该用户的草稿',
)
assertEqual(
  pageSource.includes('settleContainerDetailDraftSaveSuccess(')
    && pageSource.includes('markContainerDetailDraftSaveFailure(')
    && pageSource.includes("'服务器保存失败，未保存字段已保留在本地草稿'")
    && !pageSource.slice(
      pageSource.indexOf('} catch (error) {', pageSource.indexOf('const executePendingDetailSavePlan')),
      pageSource.indexOf('} finally {', pageSource.indexOf('const executePendingDetailSavePlan')),
    ).includes('reloadCurrentDetailRef.current'),
  true,
  '200 应按字段结算，500 应保留全部草稿且不触发覆盖性重载',
)
assertEqual(
  pageSource.includes('getContainerDetailDraftFieldFailure(')
    && pageSource.includes("status={saveFailure ? 'error' : undefined}")
    && pageSource.includes('aria-invalid={Boolean(saveFailure)}')
    && pageSource.includes('title={saveFailure?.message}'),
  true,
  '价格输入框应显示字段级失败状态和可访问提示',
)
assertEqual(
  pageSource.includes('locateFirstPendingDetailField')
    && pageSource.includes('clearPendingDetailDraft')
    && pageSource.includes('pendingDetailFieldCount')
    && pageSource.includes('项未保存')
    && pageSource.includes('定位未保存项')
    && pageSource.includes('清空草稿'),
  true,
  '紧凑工具栏应提供字段数、定位和显式清空草稿入口',
)
assertEqual(
  styleSource.includes('.container-detail-draft-meta')
    && styleSource.includes('white-space: nowrap'),
  true,
  '草稿操作应保持高密度单行布局',
)
assertEqual(
  pageSource.includes('仅当前页面内存保存，刷新或关闭页面会丢失')
    && pageSource.includes('clearContainerDetailDraftFieldsIfVersionMatches')
    && pageSource.includes("window.addEventListener('storage', onStorage)"),
  true,
  '持久化失败必须持续提示刷新风险，并按版本清理和同步跨标签页草稿',
)
assertEqual(
  pageSource.includes('if (hasDetailDraftFilter)')
    && pageSource.includes('setSelectedTagFilters([])')
    && pageSource.includes('setColumnFilters({})')
    && pageSource.includes('createContainerDetailDraftLocateResetPlan')
    && pageSource.includes('shouldConsumePendingContainerDetailLocate')
    && pageSource.includes('failedDetailAppendRequestKeyRef.current')
    && pageSource.includes('自动追加失败后保留定位意图但不循环重试')
    && pageSource.includes('未找到未保存项，可能已被删除或保存'),
  true,
  '定位草稿应先自动清除筛选并连续加载，耗尽数据后才提示可能已删除',
)
assertEqual(
  pageSource.includes('降级提示是 sticky')
    && pageSource.includes('isDetailDraftMemoryOnlyRef.current = true')
    && pageSource.includes('if (isDetailDraftMemoryOnlyRef.current) return'),
  true,
  '任一字段持久化失败后必须持续告警，并忽略会覆盖未落盘内存草稿的跨标签页事件',
)
assertEqual(
  pageSource.includes('删除持久化失败时必须回滚被清空字段')
    && pageSource.includes('setPendingDetailPatches(recoveredPatches)')
    && pageSource.includes('applyPendingContainerDetailPatches(items, recoveredPatches)')
    && pageSource.includes('const previousRowFailureKey = `${hguid}:*`')
    && pageSource.includes('failedPendingDetailSaveKeysRef.current = new Set(Object.keys(reconciledRecoveredFailures))'),
  true,
  '唯一字段清空删除失败时必须恢复 pending/version/失败键并将旧值叠回受控行显示',
)
assertEqual(
  pageSource.includes('清空草稿字段表示取消该本地编辑')
    && pageSource.includes('} else {\n        if (reloadRemovedFieldBaseline && Object.keys(removedFieldVersions).length > 0)')
    && pageSource.includes('void reloadCurrentDetailRef.current()')
    && pageSource.includes('}, true, updates, true)'),
  true,
  '草稿字段取消并成功删除持久化记录后，应回读服务端基线并重新叠加其余草稿',
)
