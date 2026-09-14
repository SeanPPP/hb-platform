import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import zh from './locales/zh.json'
import en from './locales/en.json'
import batchProductSalesZh from '../pages/ExecutiveSalesIntelligence/BatchProductSalesAnalysis/messages.zh.json'
import batchProductSalesEn from '../pages/ExecutiveSalesIntelligence/BatchProductSalesAnalysis/messages.en.json'

const STORAGE_KEY = 'lang'

function detectLanguage(): string {
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored === 'zh' || stored === 'en') return stored
  const browser = navigator.language.toLowerCase()
  return browser.startsWith('zh') ? 'zh' : 'en'
}

i18n.use(initReactI18next).init({
  resources: {
    zh: { translation: { ...zh, ...batchProductSalesZh } },
    en: { translation: { ...en, ...batchProductSalesEn } },
  },
  lng: detectLanguage(),
  fallbackLng: 'zh',
  interpolation: { escapeValue: false },
})

export default i18n
