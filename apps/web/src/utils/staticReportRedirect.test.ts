import { resolveStaticReportRedirect } from './staticReportRedirect'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

assertEqual(resolveStaticReportRedirect('/reports/kfc-uncle-bills/'), '/reports/kfc-uncle-bills/', '标准报告路径原样返回')
assertEqual(resolveStaticReportRedirect('/reports/kfc-uncle-bills'), '/reports/kfc-uncle-bills/', '缺少末尾斜杠时补齐，避免 nginx 目录重定向丢掉登录后的跳回')

// 以下都不是静态报告路径，必须交回原有的 SPA 跳转逻辑处理
assertEqual(resolveStaticReportRedirect(null), undefined, '空值')
assertEqual(resolveStaticReportRedirect(undefined), undefined, '未定义')
assertEqual(resolveStaticReportRedirect(''), undefined, '空字符串')
assertEqual(resolveStaticReportRedirect('/reports/'), undefined, '没有报告名称')
assertEqual(resolveStaticReportRedirect('/shop'), undefined, 'SPA 路由不走整页跳转')
assertEqual(resolveStaticReportRedirect('/reports/kfc/../api/x'), undefined, '路径穿越')
assertEqual(resolveStaticReportRedirect('/reports/kfc/extra'), undefined, '多级路径')
assertEqual(resolveStaticReportRedirect('/reports/KFC/'), undefined, '大写字母不在允许范围内')
assertEqual(resolveStaticReportRedirect('/reports/kfc/?x=1'), undefined, '带查询参数')

// 开放重定向防护：外站地址、协议相对地址一律拒绝
assertEqual(resolveStaticReportRedirect('https://evil.example/reports/kfc/'), undefined, '绝对外站地址')
assertEqual(resolveStaticReportRedirect('//evil.example/reports/kfc/'), undefined, '协议相对地址')
assertEqual(resolveStaticReportRedirect('/\\evil.example/reports/kfc/'), undefined, '反斜杠变体')
assertEqual(resolveStaticReportRedirect(' /reports/kfc/'), undefined, '前导空格')

console.log('staticReportRedirect tests passed')
