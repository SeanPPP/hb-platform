import type { ApiResponse } from '../types/api'
import type { NavigationMenuDto } from '../types/auth'
import request, { unwrapApiData } from '../utils/request'

export async function fetchNavigationMenu(): Promise<NavigationMenuDto[]> {
  const response = await request.get<ApiResponse<NavigationMenuDto[]>>('/api/navigation/menu')
  return unwrapApiData(response) ?? []
}
