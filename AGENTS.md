<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **hb-platform** (51281 symbols, 147728 relationships, 300 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> Index stale? Run `node .gitnexus/run.cjs analyze` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? `npx gitnexus analyze` (npm 11 crash → `npm i -g gitnexus`; #1939).

## Always Do

- **MUST run impact analysis before editing any symbol.** Prefer GitNexus MCP `impact({target: "symbolName", direction: "upstream"})` when exposed; otherwise run `node .gitnexus/run.cjs impact "symbolName" --direction upstream --repo hb-platform`. Report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run change detection before committing.** Prefer MCP `detect_changes()` when exposed; otherwise run `node .gitnexus/run.cjs detect-changes --scope staged --repo hb-platform`. For regression review, use `--scope compare --base-ref main`.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, prefer MCP `query({query: "concept"})`; otherwise run `node .gitnexus/run.cjs query "concept" --repo hb-platform`.
- For full symbol context, prefer MCP `context({name: "symbolName"})`; otherwise run `node .gitnexus/run.cjs context "symbolName" --repo hb-platform`.

## Never Do

- NEVER edit a function, class, or method without first running `impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with blind find-and-replace. If a semantic rename tool is unavailable, use LSP rename or a complete caller/reference list plus focused edits and verification; GitNexus CLI has no rename command.
- NEVER commit changes without running MCP `detect_changes()` or the CLI fallback above to check affected scope.

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/hb-platform/context` | Codebase overview, check index freshness |
| `gitnexus://repo/hb-platform/clusters` | All functional areas |
| `gitnexus://repo/hb-platform/processes` | All execution flows |
| `gitnexus://repo/hb-platform/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `gitnexus-exploring` skill |
| Blast radius / "What breaks if I change X?" | `gitnexus-impact-analysis` skill |
| Trace bugs / "Why is X failing?" | `gitnexus-debugging` skill |
| Rename / extract / split / refactor | `gitnexus-refactoring` skill |
| Tools, resources, schema reference | `gitnexus-guide` skill |
| Index, status, clean, wiki CLI commands | `gitnexus-cli` skill |

<!-- gitnexus:end -->

## Notes

- 提交添加 reasonix
- 子代理默认使用 GPT-5.6 Sol；复杂任务、验证和代码审查使用 GPT-5.6 Sol xhigh。
