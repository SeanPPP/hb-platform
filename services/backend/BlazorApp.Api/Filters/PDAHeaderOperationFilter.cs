using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BlazorApp.Api.Filters
{
    /// <summary>
    /// PDA设备认证请求头过滤器
    /// 为PDA相关API添加X-Device-Id和X-Auth-Code请求头
    /// </summary>
    public class PDAHeaderOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var apiPath = context.ApiDescription.RelativePath;
            if (string.IsNullOrEmpty(apiPath))
            {
                return;
            }

            if (apiPath.Contains("/pda/", StringComparison.OrdinalIgnoreCase))
            {
                operation.Parameters ??= new List<OpenApiParameter>();

                operation.Parameters.Add(
                    new OpenApiParameter
                    {
                        Name = "X-Device-Id",
                        In = ParameterLocation.Header,
                        Required = false,
                        Schema = new OpenApiSchema { Type = "string" },
                        Description = "设备硬件ID",
                    }
                );

                operation.Parameters.Add(
                    new OpenApiParameter
                    {
                        Name = "X-Auth-Code",
                        In = ParameterLocation.Header,
                        Required = false,
                        Schema = new OpenApiSchema { Type = "string" },
                        Description = "设备授权码",
                    }
                );
            }
        }
    }
}
