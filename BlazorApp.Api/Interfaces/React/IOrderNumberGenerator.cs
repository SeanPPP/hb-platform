using System.Threading.Tasks;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IOrderNumberGenerator
    {
        Task<string> GetNextOrderNoAsync();
    }
}
