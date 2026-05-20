using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BlazorApp.Api.Interfaces
{
    public class CurrentUserManageableStoreScope
    {
        public bool IsAllowed { get; set; }
        public bool IsAuthenticated { get; set; }
        public bool IsAdmin { get; set; }
        public string ActorLabel { get; set; } = "system";
        public string Message { get; set; } = string.Empty;
        public string UserGuid { get; set; } = string.Empty;
        public IReadOnlyList<string> StoreGuids { get; set; } = new List<string>();
        public IReadOnlyList<string> StoreCodes { get; set; } = new List<string>();

        public bool CanAccessStoreGuid(string storeGuid) =>
            IsAdmin
            || StoreGuids.Any(item => item.Equals(storeGuid, System.StringComparison.OrdinalIgnoreCase));

        public bool CanAccessStoreCode(string storeCode) =>
            IsAdmin
            || StoreCodes.Any(item => item.Equals(storeCode, System.StringComparison.OrdinalIgnoreCase));
    }

    public interface ICurrentUserManageableStoreScopeService
    {
        Task<CurrentUserManageableStoreScope> GetScopeAsync();
        Task<bool> CanManageStoreAsync(string storeGuid);
        Task<bool> CanManageUserAsync(string userGuid);
    }
}
