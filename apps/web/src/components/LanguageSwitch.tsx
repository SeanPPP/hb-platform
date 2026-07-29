import { Button, Segmented, Tooltip } from 'antd'
import type { SegmentedProps } from 'antd'
import { useTranslation } from 'react-i18next'

const LANG_STORAGE_KEY = 'lang'

type LanguageSwitchVariant = 'segmented' | 'target-icon'

interface LanguageSwitchProps {
  className?: string
  size?: SegmentedProps['size']
  compact?: boolean
  variant?: LanguageSwitchVariant
}

export default function LanguageSwitch({
  className,
  size = 'middle',
  compact = false,
  variant = 'segmented',
}: LanguageSwitchProps) {
  const { t, i18n } = useTranslation()
  const currentLanguage = i18n.language === 'en' ? 'en' : 'zh'

  const handleChange = (value: string | number) => {
    const nextLanguage = value === 'en' ? 'en' : 'zh'
    void i18n.changeLanguage(nextLanguage)
    localStorage.setItem(LANG_STORAGE_KEY, nextLanguage)
  }

  if (variant === 'target-icon') {
    const targetLanguage = currentLanguage === 'zh' ? 'en' : 'zh'
    const targetLanguageLabel = targetLanguage === 'en'
      ? t('layout.en', 'EN')
      : t('layout.zh', '中文')
    const targetActionLabel = `${t('layout.switchLang', '切换语言')}: ${targetLanguageLabel}`

    return (
      <Tooltip title={targetActionLabel}>
        <Button
          aria-label={targetActionLabel}
          className={className}
          icon={(
            <span aria-hidden="true" className="language-target-glyph">
              {targetLanguage === 'en' ? 'EN' : '中'}
            </span>
          )}
          onClick={() => handleChange(targetLanguage)}
          size={size}
          type="text"
        />
      </Tooltip>
    )
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
