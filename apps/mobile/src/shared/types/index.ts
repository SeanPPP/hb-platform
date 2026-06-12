export interface ApiResponse<T = unknown> {
  success?: boolean;
  isSuccess?: boolean;
  message?: string;
  data?: T;
  code?: string;
  errorCode?: string;
  details?: unknown;
  timestamp?: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages?: number;
  totalCount?: number;
  pageIndex?: number;
}
