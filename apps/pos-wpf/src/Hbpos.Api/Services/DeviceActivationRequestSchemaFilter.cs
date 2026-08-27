using System.ComponentModel.DataAnnotations;
using Hbpos.Contracts.Devices;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Hbpos.Api.Services;

/// <summary>
/// ASP.NET Core 要求 positional record 的验证注解放在构造参数上；
/// Swashbuckle 6 不会自动把这些注解投影到属性 schema，因此在这里做窄范围映射。
/// </summary>
public sealed class DeviceActivationRequestSchemaFilter : ISchemaFilter
{
    private static readonly HashSet<Type> RequestTypes =
    [
        typeof(DeviceActivationCodePreviewRequest),
        typeof(DeviceActivationCodeRedeemRequest),
        typeof(DeviceActivationCodeRebindRequest),
    ];

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!RequestTypes.Contains(context.Type))
        {
            return;
        }

        var constructor = context.Type.GetConstructors().Single();
        foreach (var parameter in constructor.GetParameters())
        {
            var property = schema.Properties.FirstOrDefault(item =>
                string.Equals(item.Key, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (property.Value == null)
            {
                continue;
            }

            if (parameter.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Length > 0)
            {
                schema.Required.Add(property.Key);
                property.Value.Nullable = false;
            }

            var length = parameter
                .GetCustomAttributes(typeof(StringLengthAttribute), inherit: true)
                .OfType<StringLengthAttribute>()
                .SingleOrDefault();
            if (length != null)
            {
                property.Value.MaxLength = length.MaximumLength;
                property.Value.MinLength = length.MinimumLength;
            }
        }
    }
}
