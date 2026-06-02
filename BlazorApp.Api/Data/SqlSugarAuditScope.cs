namespace BlazorApp.Api.Data
{
    /// <summary>
    /// SqlSugar 审计字段写入作用域控制。
    /// </summary>
    public static class SqlSugarAuditScope
    {
        private static readonly AsyncLocal<int> PreserveExplicitAuditFieldsDepth = new();

        public static bool ShouldPreserveExplicitAuditFields =>
            PreserveExplicitAuditFieldsDepth.Value > 0;

        public static IDisposable PreserveExplicitAuditFields()
        {
            PreserveExplicitAuditFieldsDepth.Value++;
            return new PreserveExplicitAuditFieldsScope();
        }

        private sealed class PreserveExplicitAuditFieldsScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                PreserveExplicitAuditFieldsDepth.Value = Math.Max(
                    0,
                    PreserveExplicitAuditFieldsDepth.Value - 1
                );
                _disposed = true;
            }
        }
    }
}
