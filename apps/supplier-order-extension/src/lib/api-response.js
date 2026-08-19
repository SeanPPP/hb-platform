// ApiResponse 统一信封 success/data/message/errorCode 判定
export const AUTH_ERROR_CODES = [
  'UNAUTHORIZED',
  'TOKEN_EXPIRED',
  'AUTH_FAILED',
  'INVALID_TOKEN',
  'LOGIN_REQUIRED',
  'EXPIRED_TOKEN',
];

export function isApiSuccess(resp) {
  return !!(resp && resp.success === true);
}

// HTTP 401 或业务层鉴权失败 errorCode 均视为鉴权失败
export function isAuthFailure(resp, httpStatus) {
  if (httpStatus === 401) return true;
  if (
    resp &&
    resp.success === false &&
    typeof resp.errorCode === 'string' &&
    AUTH_ERROR_CODES.includes(resp.errorCode.toUpperCase())
  ) {
    return true;
  }
  return false;
}

export function extractData(resp) {
  return resp ? resp.data : undefined;
}
