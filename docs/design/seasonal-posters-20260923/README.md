# 圣诞与万圣节 Poster

2026-09-23 · 初版由 PR #305 合入；当前重设计沿用原有业务和打印接口。

## 实现范围

- App、Web、后端 PDF 新增 `christmas`、`halloween`，覆盖 SPECIAL / NEW ARRIVAL / CLEARANCE / MULTI-BUY。
- App 风格选择器按两列换行，最后一个选项保持半行宽度；编辑、队列缩略图、保存恢复和打印请求使用同一风格类型。
- Web 按整批选择经典、现代、圣诞、万圣节；保留现有 PDF 预览、下载、打印和 Logo 开关。
- 两套 PDF 绘制器共用节日版式，复用现有价格/多买价格、页脚、字体、纸张和拼版工具。装饰是矢量路径；正文可检索，字体嵌入 PDF。
- Logo 与装饰独立；隐藏 Logo 后保留其布局空间，全部正文和页脚坐标不变。默认开启、恢复旧批次、部分打印保留、清空/完成/跨店替换重置等规则沿用现有队列。
- 默认仍为经典；业务资格、手填校验、价格计算、数据库和服务接口地址保持现有行为。

## 设计与样张

以下设计图直接由本次后端 PDF 渲染生成，商品与价格为验证样例。

重设计将圣诞老人、火车、圣诞树和南瓜、蜘蛛、骷髅、蝙蝠完整收在标题下方的装饰带；圣诞内容区增加浅绿色雪花底纹，万圣节增加浅棕色蛛网底纹。底纹先于品名、价格绘制，仍是 PDF 矢量路径；App 预览用 SVG 绘制同样位置和色彩。Logo 开关不改变图形或正文坐标。

| 文件 | 内容 |
| --- | --- |
| `christmas-four-designs.png` | 重设计圣诞四款，A6、Logo 开 |
| `halloween-four-designs.png` | 重设计万圣节四款，A6、Logo 开 |
| `logo-on-off-comparison.png` | 两主题 A7 Logo 开关对照 |
| `christmas-16-combinations.png` / `christmas-logo-off-16-combinations.png` | 圣诞四类型 × 四尺寸 × Logo 开关 |
| `halloween-16-combinations.png` / `halloween-logo-off-16-combinations.png` | 万圣节四类型 × 四尺寸 × Logo 开关 |
| `a7-extreme-eight-designs.png` | A7 长品名、长货号、大金额、99 件多买 |

最终 PDF 位于工作树的 `output/pdf/seasonal-posters/`：

| PDF | 页数与用途 |
| --- | --- |
| `posters-Christmas.pdf` | 16 页，圣诞，Logo 开 |
| `posters-Christmas-logo-off.pdf` | 16 页，圣诞，Logo 关 |
| `posters-Halloween.pdf` | 16 页，万圣节，Logo 开 |
| `posters-Halloween-logo-off.pdf` | 16 页，万圣节，Logo 关 |
| `sheet-A6-Christmas.pdf` | 1 页 A4，圣诞四张 A6 拼版及裁切线 |
| `sheet-A6-Halloween.pdf` | 1 页 A4，万圣节四张 A6 拼版及裁切线 |
| `seasonal-A7-extremes-and-mixed-imposition.pdf` | 前 8 页 A7 极值；后 15 页混合尺寸拼版 |

16 页样张依次为特价、多买、新品、清仓，每类按 A4、A5、A6、A7 排列。原尺寸为 210×297、148×210、105×148、74×105 mm；拼版复用 1/2/4/8 张排版。打印样张选择 100% / 实际大小。A7 采用现有短版页脚：有效期显示结束日，货号缩为 `#`，装饰减少。

## 验证证据

- 后端 `PromoPosterTests`：51 项通过，包含 64 基础组合的纸张、文字、Logo XObject 检查；32 对 Logo 开关的全部字形内容和坐标完全一致。
- 对基础及 A7 极值 PDF 检查字形在纸内、词块不相交；极值覆盖长品名、20 位货号、五位整数金额、99 件、角分、多买 SAVE、混搭。交付图册已人工查看全部 64 组合及 8 张极值。
- 回归原有经典/现代/省彩墨、字体嵌入、业务校验、默认值、A4–A7 混合拼版等现有测试。
- App、Web 相关逻辑测试、TypeScript 检查、改动文件 ESLint 通过；`git diff --check` 通过。
- 队列测试覆盖节日风格和 Logo 的读取恢复、真实保存快照、部分打印、切换风格、清空、整批完成与跨店替换。
- `ios-*.png`、`android-*.png`、`web-*.png` 为 PR #305 首版界面验收截图；重设计以本目录更新后的 PDF 样张为准，客户端运行时验证另行记录。
- 独立代码审查指出 A4/A5 装饰越出标题下方窄条、App 与 PDF 主图几何不同；已将缩放按装饰带高度限制，并逐项同步两端的圣诞老人、火车、树、南瓜、蜘蛛、骷髅和蝙蝠。

代码路径通过 codebase-memory 与源码调用点核对；GitNexus 当前不可用，采用精确源码、实际 diff 和相关测试完成影响核验。

## 复现

从工作树根目录运行后端验证并重新生成真实 PDF：

```sh
PROMO_POSTER_SAMPLE_DIR=/tmp/hb-seasonal-pdf dotnet test \
  services/backend/BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj \
  --filter FullyQualifiedName~PromoPosterTests
```

App：

```sh
cd apps/mobile
npx tsx src/modules/promo-posters/logic.test.ts
npx tsx src/modules/promo-posters/queue-store.test.ts
npx tsc --noEmit
npx eslint src/components/promo-posters/PosterSegmented.tsx \
  src/components/promo-posters/PromoPosterPreview.tsx \
  src/modules/promo-posters/editor-screen.tsx \
  src/modules/promo-posters/types.ts \
  src/modules/promo-posters/logic.test.ts \
  src/modules/promo-posters/queue-store.test.ts
```

## 交付边界

本次完成本地实现、设计与样张及模拟器/浏览器验证；尚未部署、发布 OTA 或实物打印。上线顺序为后端支持新 style 后，再发布 Web/App。实际设备下载、重载和打印机出纸需在发布后分别确认。

App 预览继续使用现有系统粗体/窄体和文字 Logo 近似，最终 PDF 使用嵌入字体与真实品牌资源；打印以 PDF 为准。长品名沿用最多两行省略规则。A7 极长货号会缩小页脚文字，样张未发现截断或重叠。
