import type { ApiResponse } from '../types/api'
import type {
  CreatePricingStrategyDto,
  PricingEvaluateRequest,
  PricingEvaluateResponse,
  PricingStrategyDetailDto,
  PricingStrategyListDto,
  UpdatePricingStrategyDto,
} from '../types/pricingStrategy'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/pricing-strategies'

export async function getStrategyGrid(data: Record<string, unknown>) {
  const response = await request.post<ApiResponse<{ items: PricingStrategyListDto[]; total: number }>>(
    `${API_BASE}/grid`,
    data,
  )
  return unwrapApiData(response)
}

export async function getStrategyById(id: string): Promise<PricingStrategyDetailDto> {
  const response = await request.get<ApiResponse<PricingStrategyDetailDto>>(`${API_BASE}/${id}`)
  return unwrapApiData(response)
}

export async function createStrategy(data: CreatePricingStrategyDto): Promise<PricingStrategyDetailDto> {
  const response = await request.post<ApiResponse<PricingStrategyDetailDto>>(API_BASE, data)
  return unwrapApiData(response)
}

export async function updateStrategy(id: string, data: UpdatePricingStrategyDto): Promise<PricingStrategyDetailDto> {
  const response = await request.put<ApiResponse<PricingStrategyDetailDto>>(`${API_BASE}/${id}`, data)
  return unwrapApiData(response)
}

export async function deleteStrategy(id: string): Promise<void> {
  await request.delete(`${API_BASE}/${id}`)
}

export async function evaluatePricing(data: PricingEvaluateRequest): Promise<PricingEvaluateResponse> {
  const response = await request.post<ApiResponse<PricingEvaluateResponse>>(`${API_BASE}/evaluate`, data)
  return unwrapApiData(response)
}
