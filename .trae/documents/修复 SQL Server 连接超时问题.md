修改 `appsettings.json` 中的 `DefaultConnection` 连接字符串，增加连接超时参数并优化连接配置：

1. 在连接字符串中添加 `Connect Timeout=60`（增加到 60 秒）
2. 可选：将 `Server=localhost` 改为 `Server=127.0.0.1,1433`（使用 IP 和显式端口）
3. 可选：添加 `Command Timeout=30`（命令执行超时）

这样可以给 SQL Server 更多时间响应连接请求，避免在初始化阶段超时。

**注意**：如果 SQL Server 服务确实未运行，需要先启动 SQL Server 服务。