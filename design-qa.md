# 收银主页设计 QA

- 目标画布：1366 × 768 px
- 设计基准：`.artifacts/designs/pos-cashier-home-spec-1366x768-v2.png`
- 真实 WPF 截图：`.artifacts/design-qa/pos-cashier-runtime-final.png`
- 并排对照图：`.artifacts/design-qa/pos-cashier-comparison-final.png`
- 运行方式：Release、安全 Preview 数据、96 DPI；最终预览请求只指向 `127.0.0.1:0`

## 核验结果

- [x] 画布严格为 1366 × 768；顶部 54 px、内容区 672 px、底部 42 px。
- [x] 主内容列为 58% / 26% / 16%，运行时约 792 / 355 / 219 px。
- [x] 购物车是主视觉区域，搜索、商品表格、汇总、付款按钮无裁切或重叠。
- [x] 商品数量与 SKU 按最新要求合并在同一行，并将 30 px 纵向空间归还商品列表。
- [x] “无码商品”按最新要求使用品牌蓝高亮；无有效金额时仍保持原命令禁用规则和可辨识的禁用反馈。
- [x] 中部输入缓冲、5 × 3 数字键盘、四个编辑操作、折扣快捷键均保留原命令绑定。
- [x] 右侧 2 × 5 功能按钮、状态卡、考勤二维码入口全部保留，触控高度不低于 62 px。
- [x] 标题、门店、收银员、语言、扫码器、客户显示、窗口控制及底部同步状态绑定均保留。
- [x] 商品图片继续使用真实 `ProductImage` 绑定；安全 Preview 数据没有图片时显示既有图标回退，不改变生产数据行为。
- [x] 已将设计基准与真实运行截图放在同一张对照图中检查；边界、间距、圆角、文字裁切和主要层级通过。

## 证据哈希

- 设计基准：`F4C293E24127F73D2A9198685E32581631B709CC71401FE86B18D2CD07286079`
- 真实截图：`0FE20143066EADEA602961B96F95F0E86A9EC97452672E32550A3F5A6139BED4`
- 并排对照：`6619D6CCAD8F3698C4761BE4BA90ED2A50FB7ED2820AFFAEDB5AFFCB3D262C32`

final result: passed

---

# 设置中心四页设计 QA

- 目标与实现画布：1672 × 941 px，96 DPI，Release、安全 Preview 数据。
- 设计基准：
  - 数据维护：`C:\Users\panga\.codex\generated_images\01a03ce6-b9f3-7671-87e7-96d99f241f34\exec-dddaf858-9a76-4aa4-9506-c903c0c14c24.png`
  - 支付终端：`C:\Users\panga\.codex\visualizations\2026\08\26\01a03ce6-b9f3-7671-87e7-96d99f241f34\hb-pos-settings-payment-terminal-redesign.png`
  - 小票打印机：`C:\Users\panga\.codex\visualizations\2026\08\26\01a03ce6-b9f3-7671-87e7-96d99f241f34\hb-pos-settings-receipt-printer-redesign.png`
  - 门店注册：`C:\Users\panga\.codex\visualizations\2026\08\26\01a03ce6-b9f3-7671-87e7-96d99f241f34\hb-pos-settings-store-registration-redesign.png`
- 真实 WPF 截图：`.artifacts/settings-redesign-design-qa-final2/runtime-*.png`
- 同画布并排对照：`.artifacts/settings-redesign-design-qa-final2/comparison-*.png`
- Preview 请求仅指向 `127.0.0.1:0`；未触发下载、重置、连接测试、保存或重新注册。

## 对比历史

1. 首轮发现支付页仍为纵向堆叠，连接模式、优先级和 Local IP 配置无法在首屏共同扫读，判定为 P2。
2. 第二轮改为左侧连接模式与优先级、右侧网关配置；补齐超时与无支付副作用提示，并压缩优先级行高。
3. 最终轮为当前模式增加蓝色选中边框，四页按相同像素画布重新截图并并排复核。
4. 独立审查后移除硬编码 Linkly 默认选中，把 Linkly 专属说明入口收回 Linkly 标签，并将导航声明顺序调整为先左后右；QA 截图只切换可视标签以复核 Linkly 设计态，不执行业务命令。

## 最终核验

- [x] 四个分类均保留左侧导航、面包屑、上下文标签和原有命令入口。
- [x] 支付页 Square / ANZ Linkly 标签可辨识；连接模式、优先级、Local IP、端口、超时与操作区无裁切或重叠。
- [x] 小票页保留真实双向输入绑定，并用同一字段即时驱动右侧预览。
- [x] 门店页保留当前注册、共享 API Server 面板、安全清单、主管提示和重新注册命令。
- [x] 数据页 Release 状态不伪造下载进度；仅 Debug 可见的测试数据重置继续由既有可见性规则控制。
- [x] 颜色、圆角、边框、图标、文字层级、触控尺寸及垂直滚动符合主页设计语言。
- [x] 最终对照未发现 P0、P1 或 P2 视觉问题；动态业务状态造成的内容差异不作为视觉缺陷。

## 证据哈希

- 数据维护设计 / 运行 / 对照：`31120D34C1A9959C337940D345FCD5E5A72B8093AC605617DD8AE80418B2D789` / `C3EEB25E16C12336D200111FAACAB08854D75E688D6A07835A5CDA12D2DC075E` / `6350052B5DD38DE7B77CADD62F0CCAEAA3697AC993F24EEDEC81492F43F59116`
- 支付终端设计 / 运行 / 对照：`2756479CFDDA15972D429A939BEB7B2881B8B126565C778866359552374FF74C` / `8F511B28793EA99ACC8ED0946E959B0A409CB27AA9206062F91F3363AF23F1F2` / `A4090C0AF02E98BA223A38AB5B962F577E9AC77E8483B503F08BDDA492CA7B15`
- 小票打印机设计 / 运行 / 对照：`486D6B790E6A3B7B4AE825EA76F0AAB5BDEC54B5D29185FD021F9F9B93285124` / `8E3074129A14BCB2B9D689535705CC9E0EE69D301ABC4BD2CE52FECC1C7A8FBD` / `DF294C048EF2C50D238ACBA6827103146B46241175CC791287B96341F7F706A5`
- 门店注册设计 / 运行 / 对照：`9E5F1E3D069B27C0320673192B4FB14D88E1937A25149811955497FD6F888067` / `579DFC3F2DF56C29BE6ED062F3BEC91210C8F7EB14A1E2387CB9E7E48EF73CFC` / `C05F14AB00080BE4104E5110B82D4963C12F46BBA65BE811208FD77D3A752094`

final result: passed
