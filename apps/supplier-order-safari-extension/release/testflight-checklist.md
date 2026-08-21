# Internal TestFlight 验收清单

构建：1.2.0 (1)，内部测试组，不创建外部测试组。

必须分别在实体 iPhone 与实体 iPad 完成：

- [ ] 宿主 App 正常安装和启动。
- [ ] “设置 → Apps → Safari → 扩展”中可启用 HB Supplier Order。
- [ ] 默认远端地址显示 https://hotbargain.vip。
- [ ] 使用现有内部测试账号登录并选择授权门店。
- [ ] 打开 https://www.meteorparty.com.au/Party-Favors/Party-Favors-Allfavors，无需供应商登录。
- [ ] 授予该供应商站点权限，点击 Safari 工具栏 HB 图标可打开完整助手。
- [ ] 验证网页内商品入口、采购记录、销售记录和供应商排行。
- [ ] 验证退出、重新启动、权限拒绝和网络失败的安全状态。
- [ ] TestFlight 没有新增阻断崩溃。

若失败，修复后只将 CURRENT_PROJECT_VERSION 递增为 2、3 等；MARKETING_VERSION 与扩展语义版本仍保持 1.2.0。

两台设备全部通过后，按授权直接进入正式 App Review 与 Unlisted 申请，不再追加发布确认。
