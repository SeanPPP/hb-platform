export type HbSupplierOrderSupportLanguage = 'en' | 'zh'

type SupportFaq = {
  question: string
  answer: string
}

type SupportLocale = {
  title: string
  subtitle: string
  summary: string
  platformLabel: string
  platformValue: string
  accessLabel: string
  accessValue: string
  stepsTitle: string
  steps: string[]
  testTitle: string
  testDescription: string
  faqTitle: string
  faqs: SupportFaq[]
  helpTitle: string
  helpDescription: string
  privacyLabel: string
}

export const HB_SUPPLIER_ORDER_SUPPORT = {
  publicUrl: 'https://hotbargain.vip/support/hb-supplier-order',
  privacyUrl: 'https://hotbargain.vip/privacy/browser-extension',
  contactEmail: 'inquiries@hotbargain.com.au',
  testSupplierUrl: 'https://www.meteorparty.com.au/Party-Favors/Party-Favors-Allfavors',
  locales: {
    en: {
      title: 'HB Supplier Order Support',
      subtitle: 'iPhone and iPad Safari extension',
      summary:
        'Install and enable HB Supplier Order, sign in with an authorised Hot Bargain account, then review purchase and sales insights while browsing a supported supplier page.',
      platformLabel: 'Supported devices',
      platformValue: 'iPhone and iPad using Safari',
      accessLabel: 'Account access',
      accessValue: 'Existing authorised Hot Bargain account required',
      stepsTitle: 'Install and enable the extension',
      steps: [
        'Install HB Supplier Order from its App Store link and open the host app once.',
        'Open Settings → Apps → Safari → Extensions → HB Supplier Order, then turn on Allow Extension.',
        'Return to Safari, open hotbargain.vip and allow the extension to access the Hot Bargain website when prompted.',
        'Sign in to Hot Bargain, select your authorised store and open the Supplier Order Assistant.',
        'Open a supported supplier product page, allow that website when prompted, then tap the HB icon in the Safari toolbar.',
      ],
      testTitle: 'Public supplier test page',
      testDescription:
        'Meteor Party provides a supported product-list page that does not require a supplier login. Sign in to Hot Bargain first, then use this page to verify the in-page product entry and full assistant.',
      faqTitle: 'Frequently asked questions',
      faqs: [
        {
          question: 'Which browsers are supported on iPhone and iPad?',
          answer: 'Use Safari. Other iOS browsers are not supported by this Safari extension.',
        },
        {
          question: 'Why is the HB icon or product entry missing?',
          answer:
            'Check that the extension is enabled, Safari has permission for the current website, and you are signed in to Hot Bargain with an authorised account.',
        },
        {
          question: 'Does the extension place supplier orders automatically?',
          answer:
            'No. It displays authorised internal ordering insights and leaves every purchasing decision and supplier action to the user.',
        },
        {
          question: 'What should I do after a permission or network error?',
          answer:
            'Reload the page, confirm website permission and network access, then sign out and back in if the session has expired.',
        },
      ],
      helpTitle: 'Contact support',
      helpDescription:
        'Include your device model, iOS or iPadOS version, Safari page URL and the step that failed. Never email a password or access token.',
      privacyLabel: 'Browser extension privacy policy',
    },
    zh: {
      title: 'HB Supplier Order 支持',
      subtitle: 'iPhone 与 iPad Safari 扩展',
      summary:
        '安装并启用 HB Supplier Order，使用已授权的 Hot Bargain 账号登录，即可在受支持的供应商页面查看采购与销售洞察。',
      platformLabel: '支持设备',
      platformValue: '使用 Safari 的 iPhone 与 iPad',
      accessLabel: '账号要求',
      accessValue: '需要现有且已授权的 Hot Bargain 账号',
      stepsTitle: '安装并启用扩展',
      steps: [
        '通过专属 App Store 链接安装 HB Supplier Order，并至少打开一次宿主 App。',
        '打开“设置 → Apps → Safari → 扩展 → HB Supplier Order”，开启“允许扩展”。',
        '返回 Safari，打开 hotbargain.vip，并在提示时允许扩展访问 Hot Bargain 网站。',
        '登录 Hot Bargain，选择有权限的门店并打开供应商订货助手。',
        '打开受支持的供应商商品页，在提示时允许该网站，然后点击 Safari 工具栏中的 HB 图标。',
      ],
      testTitle: '公开供应商测试页面',
      testDescription:
        'Meteor Party 提供无需供应商登录的受支持商品列表页。请先登录 Hot Bargain，再使用该页面验证网页内商品入口与完整助手。',
      faqTitle: '常见问题',
      faqs: [
        {
          question: 'iPhone 和 iPad 支持哪些浏览器？',
          answer: '请使用 Safari。其他 iOS 浏览器不支持此 Safari 扩展。',
        },
        {
          question: '为什么看不到 HB 图标或商品入口？',
          answer: '请确认扩展已启用、Safari 已允许访问当前网站，并且使用有权限的 Hot Bargain 账号登录。',
        },
        {
          question: '扩展会自动向供应商下单吗？',
          answer: '不会。扩展只显示经授权的内部订货洞察，采购决定和供应商操作始终由用户完成。',
        },
        {
          question: '遇到权限或网络错误怎么办？',
          answer: '刷新页面，检查网站访问权限和网络；如果登录状态已过期，请退出后重新登录。',
        },
      ],
      helpTitle: '联系支持',
      helpDescription:
        '请提供设备型号、iOS 或 iPadOS 版本、Safari 页面地址和失败步骤。请勿通过邮件发送密码或访问令牌。',
      privacyLabel: '浏览器扩展隐私政策',
    },
  } satisfies Record<HbSupplierOrderSupportLanguage, SupportLocale>,
}

export function resolveHbSupplierOrderSupportLanguage(
  language?: string | null,
): HbSupplierOrderSupportLanguage {
  return language?.toLowerCase().startsWith('zh') ? 'zh' : 'en'
}
