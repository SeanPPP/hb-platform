export async function resolveGrantedProfileOrigins(profiles, hasPermission) {
  const allowedOrigins = [
    ...new Set(
      (Array.isArray(profiles) ? profiles : [])
        .filter((profile) => profile && profile.enabled !== false)
        .flatMap((profile) => (Array.isArray(profile.origins) ? profile.origins : [])),
    ),
  ];
  const grantedOrigins = [];

  for (const origin of allowedOrigins) {
    try {
      // 浏览器权限是最终事实来源，可恢复因配置切换而丢失的本地授权缓存。
      if (await hasPermission(origin)) grantedOrigins.push(origin);
    } catch {
      // 单个域名检查失败不应阻断其他已授权供应商的内容脚本注册。
    }
  }

  return grantedOrigins;
}
