import { normalizeOperationAuditPage } from '../pages/PosAdmin/OperationLogs/operationLogsLogic'
import type { ApiResponse, PagedResult } from '../types/api'
import type {
  OperationAuditDetail,
  OperationAuditListItem,
  OperationAuditQueryParams,
} from '../types/operationAudit'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/pos-operation-audits'

export async function getOperationAudits(params: OperationAuditQueryParams) {
  const response = await request.get<ApiResponse<PagedResult<OperationAuditListItem>>>(API_BASE, {
    params: params as unknown as Record<string, unknown>,
  })
  return normalizeOperationAuditPage(unwrapApiData(response))
}

export async function getOperationAuditDetail(eventId: string) {
  const response = await request.get<ApiResponse<OperationAuditDetail>>(
    `${API_BASE}/${encodeURIComponent(eventId)}`,
  )
  return unwrapApiData(response)
}
