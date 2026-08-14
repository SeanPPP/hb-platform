import { posColors } from "@/ui/theme";

/** 手机收银全部页面共享的 8px 布局节奏。 */
export const handheldLayout = Object.freeze({
  grid: 8,
  screenPadding: 16,
  compactGap: 8,
  sectionGap: 16,
  largeGap: 24,
  fixedActionPadding: 16,
  contentMaxWidth: 520,
});

/** 所有关键操作必须满足小屏触控下限，圆角保持克制。 */
export const handheldControl = Object.freeze({
  minimumHeight: 48,
  compactHeight: 40,
  radius: 6,
  borderWidth: 1,
});

export const handheldType = Object.freeze({
  title: 24,
  sectionTitle: 16,
  body: 14,
  metadata: 12,
  amount: 32,
});

export const handheldTone = Object.freeze({
  danger: Object.freeze({
    foreground: posColors.red,
    background: posColors.redSoft,
  }),
  info: Object.freeze({
    foreground: posColors.blue,
    background: posColors.blueSoft,
  }),
  success: Object.freeze({
    foreground: posColors.green,
    background: posColors.greenSoft,
  }),
  warning: Object.freeze({
    foreground: posColors.yellow,
    background: posColors.yellowSoft,
  }),
});
