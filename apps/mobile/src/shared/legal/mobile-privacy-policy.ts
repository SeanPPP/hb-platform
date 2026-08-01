import policyEn from "./mobile-privacy-policy.en.json";
import policyZh from "./mobile-privacy-policy.zh.json";

export type MobilePrivacyLanguage = "en" | "zh";

type MobilePrivacySection = {
  id: string;
  title: string;
  paragraphs: string[];
  items: string[];
};

export type MobilePrivacyPolicy = {
  policyVersion: string;
  language: string;
  title: string;
  subtitle: string;
  effectiveDateLabel: string;
  effectiveDate: string;
  summary: string;
  organization: {
    label: string;
    name: string;
    contactLabel: string;
    email: string;
  };
  sections: MobilePrivacySection[];
  footer: {
    backLabel: string;
    publicCopy: string;
    publicUrl: string;
    emailLabel: string;
    openFailedTitle: string;
    openFailedMessage: string;
  };
};

// 以显式契约约束两种语言的 JSON，避免类型断言掩盖字段缺失。
const MOBILE_PRIVACY_POLICIES = {
  en: policyEn,
  zh: policyZh,
} satisfies Record<MobilePrivacyLanguage, MobilePrivacyPolicy>;

export const MOBILE_PRIVACY_PUBLIC_URL = MOBILE_PRIVACY_POLICIES.en.footer.publicUrl;

export function getMobilePrivacyPolicy(language: string): MobilePrivacyPolicy {
  return MOBILE_PRIVACY_POLICIES[
    language.toLowerCase().startsWith("zh") ? "zh" : "en"
  ];
}
