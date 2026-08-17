<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **hb-platform-main** (107464 symbols, 382406 relationships, 300 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> Index stale? Run `node .gitnexus/run.cjs analyze` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? `npx gitnexus analyze` (npm 11 crash → `npm i -g gitnexus`; #1939).

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows. For regression review, compare against the default branch: `detect_changes({scope: "compare", base_ref: "main"})`.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `query({query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `context({name: "symbolName"})`.

## Never Do

- NEVER edit a function, class, or method without first running `impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit changes without running `detect_changes()` to check affected scope.

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

## Notes

- 涉及 UI 层、界面、视觉或交互体验的任务，自动使用全局 `taste-skill` 技能。

## 个性化代理策略

- 对适合委派的常规纯文本任务，尽量优先使用已配置的 `DeepSeek-Flash` 原生子代理，不再以 Codex 周额度作为启用条件；复杂编码、架构分析和高难度 Agent 任务使用 `DeepSeek-Pro`。
- 代码审查任务在派发 `code-reviewer` 的同时，并发派发一个不继承当前任务上下文的独立 `DeepSeek-Pro` 原生子代理，并向其提供自包含的审查目标、范围及 diff、commit 或 PR 证据来执行第二路审查；主代理须汇总、去重并依据代码证据逐条复核两路发现，`DeepSeek-Pro` 结论不构成最终批准。若该路未返回有效结果，必须明确标注“未完成 DeepSeek-Pro 独立复核”，不得冒充已完成。
- 仅在对应 DeepSeek 角色已配置且当前可用时启用；角色不可用或当前工具无法识别时，继续使用现有原生代理策略，不绕过配置流程。
- 图片、视频、截图及其他视觉输入仍由主代理先识别并整理为文字事实，再按需交给 `DeepSeek-Pro` 或 `DeepSeek-Flash`。
