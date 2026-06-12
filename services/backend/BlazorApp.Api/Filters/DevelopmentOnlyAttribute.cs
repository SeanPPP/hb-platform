using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlazorApp.Api.Filters
{
    /// <summary>
    /// Restricts diagnostic or cleanup candidate controllers to Development.
    /// </summary>
    public sealed class DevelopmentOnlyAttribute : TypeFilterAttribute
    {
        public DevelopmentOnlyAttribute()
            : base(typeof(DevelopmentOnlyFilter)) { }
    }

    public sealed class DevelopmentOnlyFilter : IActionFilter
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<DevelopmentOnlyFilter> _logger;

        public DevelopmentOnlyFilter(
            IWebHostEnvironment environment,
            ILogger<DevelopmentOnlyFilter> logger
        )
        {
            _environment = environment;
            _logger = logger;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (_environment.IsDevelopment())
            {
                return;
            }

            var action = context.ActionDescriptor.DisplayName ?? "unknown action";
            _logger.LogWarning("Blocked development-only endpoint: {Action}", action);
            context.Result = new NotFoundResult();
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}
