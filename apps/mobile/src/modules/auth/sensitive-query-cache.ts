type ClearableQueryClient = { clear: () => void };

export function clearSensitiveQueryCache(queryClient: ClearableQueryClient) {
  // 会话结束时清除全部请求缓存，敏感资料和普通账号数据都不能跨会话保留。
  queryClient.clear();
}
