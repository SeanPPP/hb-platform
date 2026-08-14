import { defineConfig } from "eslint/config";
import expoConfig from "eslint-config-expo/flat.js";

export default defineConfig([
  ...expoConfig,
  {
    ignores: [
      ".expo/**",
      "coverage/**",
      "dist/**",
      "ios/**",
      "android/**",
      "src/generated/**",
    ],
    rules: {
      "import/order": [
        "warn",
        {
          "newlines-between": "always",
          alphabetize: {
            order: "asc",
            caseInsensitive: true
          }
        }
      ],
    },
  },
  {
    files: ["app/**/*.tsx", "src/**/*.tsx"],
    ignores: [
      "**/*.test.tsx",
      "**/*.spec.tsx",
      "**/*.rntl.test.tsx",
      "src/ui/controls/**",
    ],
    rules: {
      "no-restricted-imports": [
        "error",
        {
          paths: [
            {
              name: "react-native",
              importNames: [
                "Pressable",
                "TouchableOpacity",
                "TouchableHighlight",
                "TouchableWithoutFeedback",
                "Button",
                "Switch",
                "TextInput",
              ],
              allowTypeImports: true,
              message: "业务触控请使用 @/ui/controls 下对应 POS 控件。",
            },
          ],
        },
      ],
    },
  },
  {
    files: ["src/core/peripherals/scanner/hid-scanner-capture.tsx"],
    rules: {
      "no-restricted-imports": "off",
    },
  },
]);
