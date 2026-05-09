import ReactDOM from 'react-dom/client'
import { registerSW } from 'virtual:pwa-register'
import 'antd/dist/reset.css'
import './styles/global.css'
import App from './App'

if (import.meta.env.PROD) {
  registerSW({ immediate: true })
}

ReactDOM.createRoot(document.getElementById('root')!).render(<App />)
