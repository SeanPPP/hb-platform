<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **hb-platform-main** (117305 symbols, 416627 relationships, 300 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> Index stale? Run `node .gitnexus/run.cjs analyze` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? `npx gitnexus analyze` (npm 11 crash → `npm i -g gitnexus`; #1939).

## Always Do

- 修改业务逻辑、公共接口或跨模块调用前，必须评估影响范围；GitNexus 可用且索引可信时，运行 `impact({target: "symbolName", direction: "upstream"})`，说明直接调用方、受影响流程及风险等级。纯文档、配置或样式改动按实际风险直接核验。
- 提交前核对实际 diff 和变更范围；业务代码变更在 GitNexus 可用且索引可信时运行 `detect_changes()`。回归审查按已确认的基准分支比较，例如 `detect_changes({scope: "compare", base_ref: "main"})`。工具不可用时使用精确源码、调用点和相关测试替代，并说明限制。
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `query({query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `context({name: "symbolName"})`.

## Never Do

- 禁止在相关调用路径和影响范围尚未理解时修改业务逻辑；图工具不可用或索引不可信时，先用源码、调用点及相关测试完成替代分析。
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- 禁止未经实际 diff 和影响范围核对就提交；检查方式遵循上面的可用工具与替代验证规则。

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/hb-platform-main/context` | Codebase overview, check index freshness |
| `gitnexus://repo/hb-platform-main/clusters` | All functional areas |
| `gitnexus://repo/hb-platform-main/processes` | All execution flows |
| `gitnexus://repo/hb-platform-main/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->

## 工具与验证边界

- 上方 GitNexus 自动生成说明中的强制步骤以本段为边界，即使重新索引改写了该说明也适用：业务逻辑、公共接口或跨模块改动必须评估影响；图工具可用且索引可信时使用图分析，否则用精确源码、调用点和相关测试替代并说明限制。纯文档、配置或样式改动按实际风险直接核验。
- 提交前必须核对实际 diff 与影响范围；图工具不可用不免除核验责任。测试与变更风险相称，必要检查通过后仅因新改动、失败或未解决疑点扩大验证。

## 个性化代理策略

- 模型和推理强度以当前会话及实际角色配置为准，按任务难度选择角色，避免对简单任务重复编排。
- 对适合委派的纯文本任务，仅当对应 DeepSeek 角色已配置且当前可调用时，优先使用 `DeepSeek-Flash` 处理常规任务、`DeepSeek-Pro` 处理复杂任务；不可用时直接使用当前原生角色，不尝试未经授权的 provider、凭据或安装变更。
- 代码审查使用 `code-reviewer`；`DeepSeek-Pro` 已配置且当前可调用时，并发进行不继承完整上下文的独立第二路审查，提供自包含的目标、范围及 diff、commit 或 PR 证据。主代理汇总、去重并依据代码逐条复核；第二路不可用或未返回有效结果时说明该限制，继续完成可执行的审查与验证，不冒充已完成第二路审查。
- 图片、视频、截图及其他视觉输入仍由主代理先识别并整理为文字事实，再按需交给 `DeepSeek-Pro` 或 `DeepSeek-Flash`。

## 发布与 PR 规范

- **需要重新构建移动端原生包才能生效的 PR，标题必须写明**，例如「…（需重建 iOS 与 Android 包）」「…（需重建 APK）」「…（需重建 iOS）」，写在 GitHub squash 自动追加的 `(#N)` 之前。纯 JS、可走 OTA 的 PR 不加该标注，避免标题噪音。
- 判定「需要重新出原生包」（OTA 只能送 JS）：改动落在 `apps/mobile/android/**`、`apps/mobile/plugins/**`（配置插件与 `*.swift`/`*.kt`），或改了 `app.json` / `eas.json` 的原生字段（`version`、`runtimeVersion`、权限、`plugins`、图标、scheme），或新增、升级带原生代码的依赖与 Expo SDK。
- 标题只标注「是否需要原生包」；发布顺序（后端、OTA、原生包）、runtime 影响与构建基线写在描述里。新原生包要从「与同批 OTA 相同的 JS 基线 + 升版本提交」构建，不要直接从 main 构建。
- 之所以要求写进标题：标题是合并列表与发布清单里唯一始终可见的信息。原生包要走 EAS 构建与上架审核，比 OTA 慢得多，只写在描述里，批量合并后容易被当成能 OTA 的改动而漏发。
