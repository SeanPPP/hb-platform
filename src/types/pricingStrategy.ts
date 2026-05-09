export type PricingStrategyLevel = 'Supplier' | 'Store' | 'Global'
export type PricingStrategyAlgorithm = 'Linear' | 'Exponential' | 'Step'

export interface PricingStrategyListDto {
  id: string
  name: string
  level: PricingStrategyLevel
  priority: number
  isEnabled: boolean
  storeCodes: string[]
  supplierCodes: string[]
  detailsCount: number
  targetsCount: number
}

export interface PricingStrategyRuleDto {
  id?: string
  minPrice: number
  maxPrice: number
  startRate: number
  endRate: number
  algorithm: PricingStrategyAlgorithm
}

export interface PricingStrategyTargetDto {
  id?: string
  targetType: 'Store' | 'Supplier' | 'Global'
  targetCode?: string
}

export interface PricingStrategyDetailDto {
  id: string
  name: string
  level: PricingStrategyLevel
  priority: number
  isEnabled: boolean
  details: PricingStrategyRuleDto[]
  targets: PricingStrategyTargetDto[]
}

export interface CreatePricingStrategyDto {
  name: string
  level?: PricingStrategyLevel
  priority?: number
  isEnabled?: boolean
  details: PricingStrategyRuleDto[]
  targets: PricingStrategyTargetDto[]
}

export type UpdatePricingStrategyDto = CreatePricingStrategyDto

export interface PricingEvaluateRequest {
  storeCode?: string
  supplierCode?: string
  purchasePrice: number
}

export interface PricingEvaluateResponse {
  storeCode?: string
  supplierCode?: string
  purchasePrice: number
  retailPrice: number
  appliedStrategy?: string
  appliedRule?: string
}
