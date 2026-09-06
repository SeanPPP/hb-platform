# RustDesk 公司账号与设备通讯录

官方 RustDesk iOS/macOS 在“ID/中继服务器”中配置：

- ID 服务器：`hotbargain.vip:21116`
- 中继服务器（客户端显示此项时）：`hotbargain.vip:21117`
- API 服务器：`https://hotbargain.vip/api/rustdesk`
- Key：沿用服务器当前公钥，不更换私钥。

保存后通过“登录”输入现有 HBWeb 系统管理员账号。默认个人通讯录返回公司受管设备：已安装并回传 RustDesk ID 的有效 Windows POS，以及管理员显式登记的 Mac。通讯录只读；远程访问密码由连接时另行输入，不随通讯录同步。

## 状态与登记范围

在线状态由官方客户端向 `hbbs TCP 21115` 发送 `OnlineRequest` 查询，设备通过 `UDP 21116` 注册/保活。API 的心跳接口不会新增设备，不把陌生 hbbs 注册者自动视为公司设备。

POS 每次读取通讯录均核对 POSM 中该硬件的最新登记，要求仍启用且类型为 POS、系统为 Windows。旧登记或已停用设备不返回。Mac 采用 `HBweb_RustDeskManagedDevice` 显式登记，停用标记为 `IsDisabled`。本次部署仅纳入用户确认的现有 Mac，不自动扫描或导入其他电脑。

## 会话与部署

RustDesk 会话使用 32 字节随机 opaque bearer，只持久化 SHA-256 哈希，与 HBWeb JWT 隔离。会话最长 30 天；退出、密码变化、账号停用/删除或管理员角色撤销后不再授权。

仅显式执行 `--schema=rustdesk-client` 创建两张专用表，随后执行 `--schema=rustdesk-client-check`。普通 HTTP 启动不建表。部署保留当前主 API 镜像、源码和所有配置/密钥；失败时恢复主 API 镜像和对应任务源码，新表保留为兼容性回滚数据，不删除。

官方协议参照 RustDesk 1.4.9 的 `user_model.dart`、`ab_model.dart`、`peer_model.dart` 以及 `src/client.rs::peer_online`。API 地址包含路径前缀，客户端会追加 `/api/login` 等路径。
