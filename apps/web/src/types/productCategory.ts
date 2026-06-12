export interface ProductCategoryDto {
  guid: string
  name: string
  parentGuid?: string
  sortOrder?: number
  children?: ProductCategoryDto[]
}

export interface CreateProductCategoryDto {
  name: string
  parentGuid?: string
  sortOrder?: number
}

export interface UpdateProductCategoryDto {
  name?: string
  parentGuid?: string
  sortOrder?: number
}
