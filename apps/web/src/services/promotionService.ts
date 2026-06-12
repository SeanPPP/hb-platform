import type { ApiResponse } from '../types/api'
import type {
  CreatePromotionDto,
  PromotionDetailDto,
  PromotionEvaluateRequest,
  PromotionEvaluateResponse,
  PromotionListDto,
  UpdatePromotionDto,
} from '../types/promotion'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/promotions'

export async function getPromotionGrid(data: Record<string, unknown>) {
  const response = await request.post<ApiResponse<{ items: PromotionListDto[]; total: number }>>(
    `${API_BASE}/grid`,
    data,
  )
  return unwrapApiData(response)
}

export async function getPromotionById(id: string): Promise<PromotionDetailDto> {
  const response = await request.get<ApiResponse<PromotionDetailDto>>(`${API_BASE}/${id}`)
  return unwrapApiData(response)
}

export async function createPromotion(data: CreatePromotionDto): Promise<PromotionDetailDto> {
  const response = await request.post<ApiResponse<PromotionDetailDto>>(API_BASE, data)
  return unwrapApiData(response)
}

export async function updatePromotion(id: string, data: UpdatePromotionDto): Promise<PromotionDetailDto> {
  const response = await request.put<ApiResponse<PromotionDetailDto>>(`${API_BASE}/${id}`, data)
  return unwrapApiData(response)
}

export async function deletePromotion(id: string): Promise<void> {
  await request.delete(`${API_BASE}/${id}`)
}

export async function enablePromotion(id: string, enable: boolean): Promise<void> {
  await request.post(`${API_BASE}/${id}/enable?enable=${enable}`)
}

export async function evaluatePromotion(data: PromotionEvaluateRequest): Promise<PromotionEvaluateResponse> {
  const response = await request.post<ApiResponse<PromotionEvaluateResponse>>(`${API_BASE}/evaluate`, data)
  return unwrapApiData(response)
}
