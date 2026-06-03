using System.Threading.Tasks;

namespace HAppsSDK
{
	public interface IMobileTokenStore
	{
		Task<MobileTokenSet> LoadAsync();
		Task SaveAsync(MobileTokenSet tokenSet);
		Task ClearAsync();
	}
}
