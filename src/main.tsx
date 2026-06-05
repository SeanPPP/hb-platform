import ReactDOM from 'react-dom/client'
import { registerSW } from 'virtual:pwa-register'
import 'antd/dist/reset.css'
import './i18n'
import './styles/global.css'
import App from './App'

if (import.meta.env.PROD) {
  const serviceWorkerUpdateIntervalMs = 60 * 1000

  const updateSW = registerSW({
    immediate: true,
    onNeedRefresh() {
      void updateSW(true)
    },
    onRegisteredSW(_swUrl, registration) {
      if (!registration) return

      // 定期检查 sw.js，让已打开的后台页面在发布后尽快发现新版本。
      window.setInterval(() => {
        if (!navigator.onLine) return
        void registration.update()
      }, serviceWorkerUpdateIntervalMs)
    },
  })
}

ReactDOM.createRoot(document.getElementById('root')!).render(<App />)
