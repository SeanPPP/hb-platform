import { Segmented, Tooltip } from 'antd'
import type { SegmentedProps } from 'antd'
import { useTranslation } from 'react-i18next'

const LANG_STORAGE_KEY = 'lang'

interface LanguageSwitchProps {
  className?: string
  size?: SegmentedProps['size']
  compact?: boolean
}

export default function LanguageSwitch({ className, size = 'middle', compact = false }: LanguageSwitchProps) {
  const { t, i18n } = useTranslation()
  const currentLanguage = i18n.language === 'en' ? 'en' : 'zh'

  const handleChange = (value: string | number) => {
    const nextLanguage = value === 'en' ? 'en' : 'zh'
    void i18n.changeLanguage(nextLanguage)
    localStorage.setItem(LANG_STORAGE_KEY, nextLanguage)
  }

  const switcher = (
    <Segmented
      className={className}
      size={size}
      value={currentLanguage}
      onChange={handleChange}
      options={[
        { label: t('layout.zh', '中文'), value: 'zh' },
        { label: 'EN', value: 'en' },
      ]}
    />
  )

  if (!compact) {
    return switcher
  }

  return <Tooltip title={t('layout.language', '语言')}>{switcher}</Tooltip>
}
