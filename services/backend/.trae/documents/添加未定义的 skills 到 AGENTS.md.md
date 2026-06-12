## 执行计划

将 `.trae/skills` 目录中存在的但未在 AGENTS.md 中定义的 16 个 skills 添加到 AGENTS.md 的 `<available_skills>` 部分：

### 需要添加的 skills（按字母顺序）：

1. **algorithmic-art** (location: project)
   - Description: Creating algorithmic art using p5.js with seeded randomness and interactive parameter exploration. Use this when users request creating art using code, generative art, algorithmic art, flow fields, or particle systems.

2. **brand-guidelines** (location: project)
   - Description: Applies Anthropic's official brand colors and typography to any sort of artifact that may benefit from having Anthropic's look-and-feel. Use it when brand colors or style guidelines, visual formatting, or company design standards apply.

3. **canvas-design** (location: project)
   - Description: Create beautiful visual art in .png and .pdf documents using design philosophy. You should use this skill when user asks to create a poster, piece of art, design, or other static piece.

4. **doc-coauthoring** (location: project)
   - Description: Guide users through a structured workflow for co-authoring documentation. Use when user wants to write documentation, proposals, technical specs, decision docs, or similar structured content.

5. **docx** (location: project)
   - Description: Comprehensive document creation, editing, and analysis with support for tracked changes, comments, formatting preservation, and text extraction. When Claude needs to work with professional documents (.docx files).

6. **dotnet-backend-patterns** (location: project)
   - Description: Master C#/.NET backend development patterns for building robust APIs, MCP servers, and enterprise applications. Covers async/await, dependency injection, Entity Framework Core, Dapper, configuration, caching, and testing with xUnit.

7. **internal-comms** (location: project)
   - Description: A set of resources to help write all kinds of internal communications, using formats that company likes to use. Use whenever asked to write internal communications (status reports, leadership updates, 3P updates, company newsletters, FAQs, incident reports, project updates, etc.).

8. **mcp-builder** (location: project)
   - Description: Guide for creating high-quality MCP (Model Context Protocol) servers that enable LLMs to interact with external services through well-designed tools. Use when building MCP servers to integrate external APIs or services.

9. **pdf** (location: project)
   - Description: Comprehensive PDF manipulation toolkit for extracting text and tables, creating new PDFs, merging/splitting documents, and handling forms. When Claude needs to fill in a PDF form or programmatically process, generate, or analyze PDF documents at scale.

10. **pptx** (location: project)
    - Description: Presentation creation, editing, and analysis. When Claude needs to work with presentations (.pptx files) for: (1) Creating new presentations, (2) Modifying or editing content, (3) Working with layouts, (4) Adding comments or speaker notes.

11. **skill-creator** (location: project)
    - Description: Guide for creating effective skills. This skill should be used when users want to create a new skill (or update an existing skill) that extends Claude's capabilities.

12. **slack-gif-creator** (location: project)
    - Description: Knowledge and utilities for creating animated GIFs optimized for Slack. Provides constraints, validation tools, and animation concepts. Use when users request animated GIFs for Slack like "make me a GIF of X doing Y for Slack."

13. **theme-factory** (location: project)
    - Description: Toolkit for styling artifacts with a theme. These artifacts can be slides, docs, reportings, HTML landing pages, etc. There are 10 pre-set themes with colors/fonts.

14. **web-artifacts-builder** (location: project)
    - Description: Suite of tools for creating elaborate, multi-component claude.ai HTML artifacts using modern frontend web technologies (React, Tailwind CSS, shadcn/ui). Use for complex artifacts requiring state management, routing, or shadcn/ui components.

15. **webapp-testing** (location: project)
    - Description: Toolkit for interacting with and testing local web applications using Playwright. Supports verifying frontend functionality, debugging UI behavior, capturing browser screenshots, and viewing browser logs.

16. **xlsx** (location: project)
    - Description: Comprehensive spreadsheet creation, editing, and analysis with support for formulas, formatting, data analysis, and visualization. When Claude needs to work with spreadsheets (.xlsx, .xlsm, .csv, .tsv, etc).

### 修改步骤：
1. 在 `<available_skills>` 部分，在现有的 `ui-ux-pro-max` 之后插入上述 16 个新的 skill 定义
2. 保持现有的格式和缩进
3. 所有 location 设置为 "project"（因为这些 skills 位于 `.trae/skills` 项目目录中）