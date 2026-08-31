// 这些导出同时被 App 使用；作为独立静态入口使 Rollup 提取共享首屏模块。
export { App as AntdApp, ConfigProvider, Result, Spin, theme } from 'antd'
export { default as enUS } from 'antd/locale/en_US'
export { default as zhCN } from 'antd/locale/zh_CN'
import 'dayjs/locale/zh-cn'
