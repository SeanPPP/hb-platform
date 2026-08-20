import { useEffect } from 'react'
import {
  preserveMobilePrivacyDocumentMetadata,
  updateMobilePrivacyDocumentMetadata,
} from '../MobilePrivacy/documentMetadata'
import '../MobilePrivacy/mobilePrivacy.css'
import { BROWSER_EXTENSION_PRIVACY_POLICY as policy } from './browserExtensionPrivacyPolicy'

export default function BrowserExtensionPrivacyPage() {
  useEffect(() => {
    const restoreMetadata = preserveMobilePrivacyDocumentMetadata(document)
    updateMobilePrivacyDocumentMetadata(document, 'en', policy.title)
    return restoreMetadata
  }, [])

  return (
    <main className="mobile-privacy-page">
      <header className="mobile-privacy-header">
        <a
          className="mobile-privacy-brand"
          href="/privacy/browser-extension"
          aria-label="Hot Bargain browser extension privacy policy"
        >
          <span className="mobile-privacy-mark" aria-hidden="true">HB</span>
          <span>Hot Bargain</span>
        </a>
      </header>

      <article className="mobile-privacy-document">
        <div className="mobile-privacy-intro">
          <p className="mobile-privacy-eyebrow">{policy.subtitle}</p>
          <h1>{policy.title}</h1>
          <p className="mobile-privacy-effective">Effective date: {policy.effectiveDate}</p>
          <p className="mobile-privacy-summary">{policy.summary}</p>
        </div>

        <dl className="mobile-privacy-organization">
          <div>
            <dt>Responsible entity</dt>
            <dd>{policy.responsibleEntity}</dd>
          </div>
          <div>
            <dt>Privacy contact</dt>
            <dd><a href={`mailto:${policy.privacyEmail}`}>{policy.privacyEmail}</a></dd>
          </div>
        </dl>

        <div className="mobile-privacy-sections">
          {policy.sections.map((section) => (
            <section key={section.id} id={section.id}>
              <h2>{section.title}</h2>
              {section.paragraphs.map((paragraph) => <p key={paragraph}>{paragraph}</p>)}
              {section.items.length > 0 ? (
                <ul>
                  {section.items.map((item) => <li key={item}>{item}</li>)}
                </ul>
              ) : null}
            </section>
          ))}
        </div>

        <footer className="mobile-privacy-footer">
          <span>© {new Date().getFullYear()} {policy.responsibleEntity}</span>
          <a href={`mailto:${policy.privacyEmail}`}>Email privacy enquiries</a>
        </footer>
      </article>
    </main>
  )
}
