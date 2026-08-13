using System.Reflection;
using System.Text.Json.Serialization;
using Hbpos.Contracts.HeldOrders;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Hbpos.Api.Services;

/// <summary>
/// 定向 OpenAPI schema filter：publish.cart / prepare.payload / recovery.payload
/// 使用 object + 运行时 converter 保持 direct wire，但 Swagger/OpenAPI 必须
/// 表达为 SharedSaleCartV1 | SharedSaleCartV2 的 oneOf，避免 codegen 退化为 unknown。
/// </summary>
public sealed class SharedSaleCartPayloadSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.MemberInfo is not PropertyInfo property ||
            !property.GetCustomAttributes(typeof(JsonConverterAttribute), inherit: true)
                .OfType<JsonConverterAttribute>()
                .Any(attribute =>
                    attribute.ConverterType == typeof(SharedSaleCartPayloadJsonConverter)))
        {
            return;
        }

        var v1 = context.SchemaGenerator.GenerateSchema(
            typeof(SharedSaleCartV1),
            context.SchemaRepository,
            memberInfo: null!,
            parameterInfo: null!,
            routeInfo: null!);
        var v2 = context.SchemaGenerator.GenerateSchema(
            typeof(SharedSaleCartV2),
            context.SchemaRepository,
            memberInfo: null!,
            parameterInfo: null!,
            routeInfo: null!);

        schema.Type = null;
        schema.Format = null;
        schema.Nullable = false;
        schema.Properties = null;
        schema.OneOf = [v1, v2];
    }
}
