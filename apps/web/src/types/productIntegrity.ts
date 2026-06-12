export interface ProductIntegrityCheckResultDto {
  totalProducts: number
  passedCount: number
  failedCount: number
  issues: ProductIntegrityIssueDto[]
}

export interface ProductIntegrityIssueDto {
  productCode: string
  issueType: string
  description: string
  severity: 'Error' | 'Warning'
}

export interface ProductIntegrityFixRequestDto {
  fixAll?: boolean
  productCodes?: string[]
  issueTypes?: string[]
}

export interface ProductIntegrityFixResultDto {
  fixedCount: number
  failedCount: number
  errors: string[]
}
