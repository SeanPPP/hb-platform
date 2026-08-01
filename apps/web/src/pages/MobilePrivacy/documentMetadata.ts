import type { MobilePrivacyLanguage } from './mobilePrivacyPolicy'

type MobilePrivacyDocument = {
  title: string
  documentElement: {
    lang: string
  }
}

export function updateMobilePrivacyDocumentMetadata(
  document: MobilePrivacyDocument,
  language: MobilePrivacyLanguage,
  policyTitle: string,
) {
  document.documentElement.lang = language === 'zh' ? 'zh-CN' : 'en'
  document.title = `${policyTitle} | Hot Bargain`
}

export function preserveMobilePrivacyDocumentMetadata(document: MobilePrivacyDocument) {
  const previousLanguage = document.documentElement.lang
  const previousTitle = document.title

  // 保存进入隐私页前的元数据，离开时不影响应用中原有的文档语义。
  return () => {
    document.documentElement.lang = previousLanguage
    document.title = previousTitle
  }
}
