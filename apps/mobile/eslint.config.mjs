import { defineConfig } from 'eslint/config'
import expoConfig from 'eslint-config-expo/flat.js'

export default defineConfig([
  ...expoConfig,
  {
    ignores: ['.expo/**', 'coverage/**', 'dist/**', 'ios/**', 'android/**'],
  },
  {
    files: ['plugins/**/*.{js,cjs,mjs}', 'scripts/**/*.{js,cjs,mjs}'],
    languageOptions: {
      globals: {
        __dirname: 'readonly',
      },
    },
    rules: {
      // 构建与校验脚本需要读取 CI 环境变量，不属于 Expo 客户端运行时代码。
      'expo/no-env-var-destructuring': 'off',
    },
  },
  {
    files: ['src/shared/network/index.ts'],
    rules: {
      // 该 barrel 有意汇总 hook 的兼容再导出；TypeScript typecheck 负责验证导出契约。
      'import/export': 'off',
    },
  },
])
