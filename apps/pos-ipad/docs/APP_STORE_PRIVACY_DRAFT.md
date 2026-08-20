# HB POS App Privacy 技术草稿

本文是基于当前代码的数据流盘点，用于协助业务/法律负责人填写 App Store Connect
的 App Privacy 问卷。它不是法律结论，也不得在未确认实际运营、第三方处理者、保留
周期和地区范围前直接提交。

## 当前代码可观察的数据类型

| Apple 数据类别 | 当前字段/行为示例 | 与身份关联 | 主要用途草稿 |
| --- | --- | --- | --- |
| Identifiers | 安装/硬件标识、设备号、分店号、收银员 ID | 是 | App Functionality、Security |
| Name | 收银员姓名；分期客户姓名 | 是 | App Functionality |
| Phone Number | 分期客户电话 | 是 | App Functionality |
| Purchases | 商品、数量、价格、折扣、订单/退款、付款方式与时间 | 是 | App Functionality、Analytics |
| Payment Information | 卡类型、BIN、掩码卡号、授权码、交易引用、STAN、商户号、退款引用 | 是 | App Functionality、Fraud Prevention |
| Other User Content | 分期备注及操作备注 | 是 | App Functionality |
| Diagnostics | App 版本、错误类别、trace ID、错误信息/堆栈、分店/设备/用户投影 | 视日志配置而定 | App Functionality、Analytics |

代码审计未发现广告标识、跨 App 跟踪、定位、健康、照片、视频或麦克风数据用于该 POS
业务数据流。相机只作为商品条码扫描备用入口；本结论仍需结合原生 SDK 和最终 IPA
重新核验。

## Tracking

- 当前代码未发现 IDFA、广告 SDK 或把数据用于跨公司 App/网站跟踪的实现。
- App Store Connect 的 Tracking 建议值只有在第三方 SDK、支付服务、日志服务和实际
  运营方全部确认后才能选择 `No`。
- `NSPrivacyTracking=false` 不能替代对第三方处理行为的业务确认。

## 必须补齐的隐私政策内容

现有移动端隐私页尚未明确覆盖以下 POS 数据，不能直接视为完整：

- 分期客户姓名、电话和备注。
- 掩码卡号、BIN、授权码、交易引用、STAN、商户号及退款引用。
- 门店本地网络访问、物理支付终端和打印机用途。
- Face ID 仅用于设备本地安全凭据解锁（如最终功能启用）。
- POS 数据的保留周期、删除/更正请求渠道、第三方支付与日志处理者、跨境处理地区。

## 提交前业务确认

- [ ] 分期客户资料的收集依据、用途、保留周期和删除/更正流程。
- [ ] Square、Linkly、Expo/EAS、日志中心、媒体主机和云存储各自承担的数据处理角色。
- [ ] 支付凭证字段是否上传、保存多久、谁可访问，以及是否用于欺诈预防。
- [ ] Diagnostics 是否在 production 开启、目标服务、采样范围和保留周期。
- [ ] App 是否在法国或其他有额外加密/隐私要求的地区提供。
- [ ] 最终 POS 专用隐私政策 URL 已发布并与 App Store Connect 问卷一致。
- [ ] 最终 IPA 的 `PrivacyInfo.xcprivacy`、权限文案和第三方 SDK manifest 已重新审计。
