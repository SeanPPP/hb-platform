export type BrowserExtensionPrivacySection = {
  id: string
  title: string
  paragraphs: string[]
  items: string[]
}

export type BrowserExtensionPrivacyPolicy = {
  policyVersion: string
  language: 'en'
  title: string
  subtitle: string
  effectiveDate: string
  summary: string
  responsibleEntity: string
  privacyEmail: string
  publicUrl: string
  sections: BrowserExtensionPrivacySection[]
}

export const BROWSER_EXTENSION_PRIVACY_POLICY = {
  policyVersion: '2026-08-20',
  language: 'en',
  title: 'HB Supplier Order Browser Extension Privacy Policy',
  subtitle: 'Hot Bargain internal supplier-ordering browser extension',
  effectiveDate: '20 August 2026',
  summary:
    'This policy explains how HOT BARGAIN INTERNATIONAL PTY LTD (we, us or our) handles information when authorised employees use the HB Supplier Order browser extension. It applies only to this extension and should be read before installation or use.',
  responsibleEntity: 'HOT BARGAIN INTERNATIONAL PTY LTD',
  privacyEmail: 'inquiries@hotbargain.com.au',
  publicUrl: 'https://hotbargain.vip/privacy/browser-extension',
  sections: [
    {
      id: 'purpose-and-scope',
      title: '1. Purpose and scope',
      paragraphs: [
        'HB Supplier Order is an internal business tool for authorised Hot Bargain employees. It helps an employee review internal purchase history, sales history and supplier-level sales insights while ordering products on supported supplier product-list pages.',
        'The extension does not offer public registration, place supplier orders automatically, alter supplier website accounts, or make purchasing decisions on behalf of a user.',
      ],
      items: [],
    },
    {
      id: 'information-we-handle',
      title: '2. Information we handle',
      paragraphs: [
        'The extension handles only the information needed to authenticate authorised employees, identify supplier products and retrieve the corresponding internal business records.',
      ],
      items: [
        'Account and authentication information: the username and password entered for sign-in, together with access and refresh tokens returned by the authorised Hot Bargain API. The password is transmitted for authentication and is not stored by the extension.',
        'Store and preference information: selected store code, display language, trusted Hot Bargain API origin and supplier-origin permission state.',
        'Supplier product information: the supplier domain, supplier code and item number detected from configured product-list pages. Product names or images may be read locally when needed to present the extension interface.',
        'Internal business information: purchase dates, purchase quantities, order references, sales quantities, average sale prices and supplier top-seller results returned for the authorised employee and selected store or permitted company scope.',
        'Technical configuration: declarative supplier profiles containing approved domains, selectors and allow-listed field transformations.',
      ],
    },
    {
      id: 'how-information-is-used',
      title: '3. How we use information',
      paragraphs: [
        'We use information only to provide and secure the extension\'s single purpose.',
      ],
      items: [
        'Authenticate the employee and enforce role and store access granted by Hot Bargain systems.',
        'Match a product on a supported supplier list page with Hot Bargain internal purchase and sales records.',
        'Display item history and supplier sales insights in the browser side panel and beside the relevant supplier item.',
        'Remember the employee\'s selected store, language, trusted API origin and granted supplier sites.',
        'Maintain security, diagnose faults and comply with legal or audit obligations.',
      ],
    },
    {
      id: 'browser-permissions',
      title: '4. Browser permissions',
      paragraphs: [
        'The extension requests only the browser permissions required for its stated purpose. Supplier-site access is optional and is requested one supplier origin at a time after a user action.',
      ],
      items: [
        'Storage: saves authentication tokens, selected settings, permission state and cached declarative supplier profiles. It does not store supplier website passwords or purchase and sales history as a local archive.',
        'Side panel: provides the employee sign-in, store selection, supplier permission, item-history and top-seller interface.',
        'Scripting and supplier host access: registers packaged content scripts on user-authorised supplier origins to detect configured item numbers and add history controls to product-list pages.',
        'Hot Bargain host access: connects to the trusted Hot Bargain website and API for installation-status checks, authentication, supplier configuration and authorised internal data queries.',
        'Localhost access: is available only when an employee explicitly selects the internal development option for local testing.',
      ],
    },
    {
      id: 'storage-and-retention',
      title: '5. Storage and retention',
      paragraphs: [
        'The access token is held in browser session storage. The refresh token and settings required to keep the employee signed in are held in extension local storage until logout, expiry, removal of the extension or clearing of extension data. Changing the trusted API origin clears authentication tokens and cached supplier configuration.',
        'Purchase and sales responses are used to render the requested view and are not maintained by the extension as a separate local history database. Hot Bargain server records and security logs are retained under applicable business, audit, security and legal requirements.',
      ],
      items: [],
    },
    {
      id: 'sharing-and-sale',
      title: '6. Sharing, sale and advertising',
      paragraphs: [
        'Information is sent only to authorised Hot Bargain systems and to service providers that process information on our instructions where necessary to host, secure or support those systems. Access remains subject to organisational roles and store scope.',
        'We do not sell or rent personal information. We do not use extension data for advertising, creditworthiness, lending, or purposes unrelated to the extension\'s supplier-ordering function. We do not disclose it to third parties except where required to operate the service, protect people or systems, or comply with law.',
      ],
      items: [],
    },
    {
      id: 'remote-code-and-tracking',
      title: '7. Remote code and tracking',
      paragraphs: [
        'All JavaScript and modules executed by the extension are included in the installed extension package. Supplier profiles contain declarative domains, selectors and allow-listed field transformations only. The extension does not download, evaluate or execute remote JavaScript or WebAssembly.',
        'The extension does not track browsing across unrelated websites, record keystrokes or mouse activity, or create a general browsing-history record. It reads product information only on supported supplier origins that the employee has authorised.',
      ],
      items: [],
    },
    {
      id: 'security',
      title: '8. Security',
      paragraphs: [
        'We use access controls, encrypted HTTPS connections to the production Hot Bargain API, browser extension isolation and other reasonable safeguards appropriate to the information handled. No storage or transmission method can guarantee absolute security.',
        'Employees must protect their Hot Bargain credentials, use the extension only on authorised devices and supplier sites, and report suspected unauthorised access promptly.',
      ],
      items: [],
    },
    {
      id: 'choices-and-requests',
      title: '9. Employee choices and requests',
      paragraphs: [
        'An employee can revoke a supplier-site permission in the browser, log out to remove active authentication state, clear extension data, or uninstall the extension. Revoking a permission may prevent the related supplier integration from working.',
        'For access, correction, deletion or other privacy requests concerning an employee account or internal business records, contact an authorised Hot Bargain administrator or the privacy contact below. We may need to verify identity and authority, and some records must be retained where required by law or legitimate business obligations.',
      ],
      items: [],
    },
    {
      id: 'changes-and-contact',
      title: '10. Changes, complaints and contact',
      paragraphs: [
        'We may update this policy when the extension, processing activities or legal requirements change. The latest version will be published at this public URL with a revised effective date.',
        'For questions or privacy complaints, email inquiries@hotbargain.com.au. We will investigate and respond in accordance with applicable privacy law. If you are not satisfied with our response, you may contact the Office of the Australian Information Commissioner (OAIC).',
      ],
      items: [],
    },
  ],
} satisfies BrowserExtensionPrivacyPolicy
