import type { ApiResponse } from '../types/api'
import type {
  ContainerAllocationSalesBranchesQuery,
  ContainerAllocationSalesBranchesReport,
  ContainerAllocationSalesQuery,
  ContainerAllocationSalesReport,
} from '../types/containerAllocationSales'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/containers'

function containerAllocationSalesUrl(containerGuid: string, suffix: string) {
  return `${API_BASE}/${encodeURIComponent(containerGuid)}/allocation-sales/${suffix}`
}

export async function queryContainerAllocationSales(
  containerGuid: string,
  query: ContainerAllocationSalesQuery,
): Promise<ContainerAllocationSalesReport> {
  const response = await request.post<ApiResponse<ContainerAllocationSalesReport>>(
    containerAllocationSalesUrl(containerGuid, 'query'),
    query,
  )
  return unwrapApiData(response)
}

export async function queryContainerAllocationSalesBranches(
  containerGuid: string,
  query: ContainerAllocationSalesBranchesQuery,
): Promise<ContainerAllocationSalesBranchesReport> {
  const response = await request.post<ApiResponse<ContainerAllocationSalesBranchesReport>>(
    containerAllocationSalesUrl(containerGuid, 'branches/query'),
    query,
  )
  return unwrapApiData(response)
}
