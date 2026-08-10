export interface ProductCategoryDto {
  guid: string
  name: string
  parentGuid?: string
  sortOrder?: number
  isActive: boolean
  children?: ProductCategoryDto[]
}

export interface CreateProductCategoryDto {
  name: string
  parentGuid?: string
  sortOrder?: number
  isActive?: boolean
}

export interface UpdateProductCategoryDto {
  name?: string
  parentGuid?: string
  sortOrder?: number
  isActive: boolean
}
