// 服务器上 /reports/<名称>/ 是 nginx 直接提供的静态报告页，不在 SPA 路由里：
// nginx 用 auth_request 调后端鉴权，未登录时带 ?redirect= 跳到登录页。
// 登录成功后必须整页跳转回去（SPA 内 navigate 只会落到前端 404）。
// 只接受严格格式的站内相对路径，避免被利用成开放重定向。
const STATIC_REPORT_PATH = /^\/reports\/[a-z0-9][a-z0-9-]{0,63}\/?$/

export function resolveStaticReportRedirect(target: string | null | undefined): string | undefined {
  if (!target || !STATIC_REPORT_PATH.test(target)) {
    return undefined
  }
  return target.endsWith('/') ? target : `${target}/`
}
