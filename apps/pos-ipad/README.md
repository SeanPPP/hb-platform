# HB POS iPad

`apps/pos-ipad` 是独立的 iPad 收银应用，不复用 `apps/mobile` 的 Bundle ID、EAS Project 或审核配置。

## 本地启动

```bash
npm install
npm run test
npm run prebuild:ios
npm run ios
```

原生打印、SQLCipher 和外接客显必须使用 Development Build，Expo Go 不在支持范围内。

API 地址通过 `EXPO_PUBLIC_HBPOS_API_URL` 注入；可切换的备用 origin 必须在签名构建时通过
`EXPO_PUBLIC_HBPOS_TRUSTED_API_ORIGINS` 明确列出。设备注册成功后仍由 Hbpos.Api 的设备审批和门店权限控制，
持久化设置不能把设备或收银员凭据发送边界扩张到构建白名单之外。

银行卡终端必须通过 `EXPO_PUBLIC_HBPOS_CARD_PROVIDER=square|linkly`
显式选择。Square 与 Linkly 的公开配置可以同时保留，用于恢复切换前已经耐久化的
支付 attempt；未选择 provider 时禁止发起新的银行卡交易。token、密钥和 provider
交易引用不得写入 Expo public extra。
