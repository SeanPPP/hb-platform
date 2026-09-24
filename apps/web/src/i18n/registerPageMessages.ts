import i18n from './index'

/**
 * 按页懒注册文案：页面级文案随页面代码块一起加载，不进入首屏 i18n 包。
 * 在页面模块顶层调用一次即可；重复注册幂等，已有键不会被覆盖。
 */
export function registerPageMessages(bundles: Record<string, Record<string, unknown>>) {
  for (const [language, resource] of Object.entries(bundles)) {
    i18n.addResourceBundle(language, 'translation', resource, true, false)
  }
}
