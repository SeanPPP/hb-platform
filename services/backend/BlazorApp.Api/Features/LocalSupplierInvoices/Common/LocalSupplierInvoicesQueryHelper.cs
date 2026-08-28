namespace BlazorApp.Api.Features.LocalSupplierInvoices
{
    /// <summary>共享的顺序分块查询端口，保持原有块大小、顺序及异常传播语义。</summary>
    internal static class LocalSupplierInvoicesQueryHelper
    {
        public static async Task<List<T>> QueryInChunksAsync<T, TKey>(
            IReadOnlyList<TKey> keys,
            int chunkSize,
            Func<List<TKey>, Task<List<T>>> fetch
        )
        {
            var result = new List<T>();
            if (keys == null || keys.Count == 0)
                return result;

            var total = keys.Count;
            for (var i = 0; i < total; i += chunkSize)
            {
                var chunk = new List<TKey>(Math.Min(chunkSize, total - i));
                for (var j = i; j < Math.Min(i + chunkSize, total); j++)
                {
                    chunk.Add(keys[j]);
                }

                var part = await fetch(chunk);
                if (part != null && part.Count > 0)
                {
                    result.AddRange(part);
                }
            }

            return result;
        }
    }
}
