import ReactDOM from 'react-dom/client'
import 'antd/dist/reset.css'
import './i18n'
import './styles/global.css'
import App from './App'
import { registerChunkPreloadRecovery } from './router/chunkRecovery'
import { reportRuntimeError } from './utils/centerLogClient'

function registerGlobalErrorHandlers() {
  // 浏览器运行时异常统一汇总到中心日志，避免只留在控制台里。
  window.addEventListener('error', (event) => {
    reportRuntimeError('window-error', event.error ?? event.message, {
      filename: event.filename,
      lineno: event.lineno,
      colno: event.colno,
    })
  })

  window.addEventListener('unhandledrejection', (event) => {
    reportRuntimeError('unhandledrejection', event.reason, {
      reasonType: typeof event.reason,
    })
  })
}

// 必须在应用启动前注册，才能接住首个懒加载 chunk 的旧 hash 失败。
registerChunkPreloadRecovery()
registerGlobalErrorHandlers()

ReactDOM.createRoot(document.getElementById('root')!).render(<App />)
