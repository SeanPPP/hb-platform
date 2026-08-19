// 401/业务鉴权失败时 single-flight refresh：并发请求共享同一次刷新
export function createAuthExecutor({ isAuthFailure, refresh }) {
  let refreshPromise = null;

  async function withRefresh(request) {
    const first = await request();
    if (!isAuthFailure(first)) return first;
    if (!refreshPromise) {
      refreshPromise = Promise.resolve()
        .then(refresh)
        .then(
          () => {
            refreshPromise = null;
          },
          (err) => {
            refreshPromise = null;
            throw err;
          },
        );
    }
    await refreshPromise;
    return request();
  }

  return {
    withRefresh,
    _isRefreshing: () => !!refreshPromise,
    _reset: () => {
      refreshPromise = null;
    },
  };
}
