分析

* 现有后端在 ProductWarehouseReactService.cs:468-513 的 isactive 分支仅支持 true/false 字符串过滤。
* 需要前端在表格上方添加 isactive 选择器（0/1/all），后端对应接受 0/1/all 并实现过滤；当选择 all 时不过滤。

后端改动（仅 isactive 分支，其他字段保持既定方案）

位置：d:/Development/cline/blazor/BlazorApp.Api/Services/React/ProductWarehouseReactService.cs:468-513

将 isactive 分支改为支持 0/1/all：

case "isactive":
    var flags = values.Select(v => v.Trim().ToLower()).ToList();
    if (!flags.Contains("all"))
    {
        query = query.Where(w =>
            (w.IsActive && (flags.Contains("1") || flags.Contains("true"))) ||
            (!w.IsActive && (flags.Contains("0") || flags.Contains("false")))
        );
    }
    break;

说明：保留 true/false 兼容；传 all 则不应用过滤。

前端改动

* 在表格上方添加选择器：选项为 全部(all)、上架(1)、下架(0)。
* 当选择 all：不传 isactive 过滤或传值 all；当选择 1 或 0：以字符串值传递到后端 filters 中的 isactive 键。

React 示例（伪代码）：

const options = [
  { label: "全部", value: "all" },
  { label: "上架", value: "1" },
  { label: "下架", value: "0" }
];

const [isActiveFilter, setIsActiveFilter] = useState("all");

<Select options={options} value={isActiveFilter} onChange={v => setIsActiveFilter(v)} />

const filters = { ...otherFilters };
if (isActiveFilter === "all") delete filters.isactive; else filters.isactive = [isActiveFilter];

Blazor 示例（伪代码）：

<select @bind="IsActiveFilter">
  <option value="all">全部</option>
  <option value="1">上架</option>
  <option value="0">下架</option>
</select>

var filters = new Dictionary<string, List<string>>(otherFilters);
if (IsActiveFilter == "all") filters.Remove("isactive"); else filters["isactive"] = new List<string> { IsActiveFilter };

验证

* 选择器切换 0/1/all 时请求参数正确变化；后端按预期返回。
* 构建与类型检查通过；联调验证分页和组合过滤。

如确认，我将按上述方案实施后端 isactive 过滤更新，并协助前端集成选择器。