import { useEffect, useMemo, useState } from 'react'
import {
  preserveMobilePrivacyDocumentMetadata,
  updateMobilePrivacyDocumentMetadata,
} from '../MobilePrivacy/documentMetadata'
import '../MobilePrivacy/mobilePrivacy.css'
import {
  HB_SUPPLIER_ORDER_SUPPORT as support,
  resolveHbSupplierOrderSupportLanguage,
  type HbSupplierOrderSupportLanguage,
} from './supportContent'

export default function HbSupplierOrderSupportPage() {
  const [language, setLanguage] = useState<HbSupplierOrderSupportLanguage>(() =>
    resolveHbSupplierOrderSupportLanguage(window.navigator.language),
  )
  const content = useMemo(() => support.locales[language], [language])

  useEffect(() => {
    const restoreMetadata = preserveMobilePrivacyDocumentMetadata(document)
    updateMobilePrivacyDocumentMetadata(document, language, content.title)
    return restoreMetadata
  }, [content.title, language])

  return (
    <main className="mobile-privacy-page">
      <header className="mobile-privacy-header">
        <a className="mobile-privacy-brand" href="/support/hb-supplier-order" aria-label="HB Supplier Order Support">
          <span className="mobile-privacy-mark" aria-hidden="true">HB</span>
          <span>HB Supplier Order</span>
        </a>
        <div className="mobile-privacy-language" role="group" aria-label="Language / 语言">
          <button
            type="button"
            className={language === 'en' ? 'is-active' : ''}
            aria-pressed={language === 'en'}
            onClick={() => setLanguage('en')}
          >
            English
          </button>
          <button
            type="button"
            className={language === 'zh' ? 'is-active' : ''}
            aria-pressed={language === 'zh'}
            onClick={() => setLanguage('zh')}
          >
            简体中文
          </button>
        </div>
      </header>

      <article className="mobile-privacy-document">
        <div className="mobile-privacy-intro">
          <p className="mobile-privacy-eyebrow">{content.subtitle}</p>
          <h1>{content.title}</h1>
          <p className="mobile-privacy-summary">{content.summary}</p>
        </div>

        <dl className="mobile-privacy-organization">
          <div>
            <dt>{content.platformLabel}</dt>
            <dd>{content.platformValue}</dd>
          </div>
          <div>
            <dt>{content.accessLabel}</dt>
            <dd>{content.accessValue}</dd>
          </div>
        </dl>

        <div className="mobile-privacy-sections">
          <section>
            <h2>{content.stepsTitle}</h2>
            <ol>
              {content.steps.map((step) => <li key={step}>{step}</li>)}
            </ol>
          </section>

          <section>
            <h2>{content.testTitle}</h2>
            <p>{content.testDescription}</p>
            <p>
              <a href={support.testSupplierUrl} target="_blank" rel="noopener noreferrer">
                meteorparty.com.au/Party-Favors/Party-Favors-Allfavors
              </a>
            </p>
          </section>

          <section>
            <h2>{content.faqTitle}</h2>
            {content.faqs.map((faq) => (
              <div key={faq.question}>
                <p><strong>{faq.question}</strong></p>
                <p>{faq.answer}</p>
              </div>
            ))}
          </section>

          <section>
            <h2>{content.helpTitle}</h2>
            <p>{content.helpDescription}</p>
            <p><a href={`mailto:${support.contactEmail}`}>{support.contactEmail}</a></p>
          </section>
        </div>

        <footer className="mobile-privacy-footer">
          <span>© {new Date().getFullYear()} HOT BARGAIN INTERNATIONAL PTY LTD</span>
          <a href={support.privacyUrl}>{content.privacyLabel}</a>
        </footer>
      </article>
    </main>
  )
}
