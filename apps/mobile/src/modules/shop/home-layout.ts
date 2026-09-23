// 与仓库页 PDA 布局同一阈值：Zebra TC26 等 PDA 逻辑宽度约 360dp，三列卡片过挤。
const NARROW_SCREEN_MAX_WIDTH = 390;

export function resolveHomeProductColumns(windowWidth: number) {
  return windowWidth <= NARROW_SCREEN_MAX_WIDTH ? 2 : 3;
}
