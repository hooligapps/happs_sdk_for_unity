using System.Threading.Tasks;

namespace HAppsSDK
{
	public sealed class InMemoryMobileTokenStore : IMobileTokenStore
	{
		private MobileTokenSet _tokenSet;

		public Task<MobileTokenSet> LoadAsync()
		{
			return Task.FromResult(_tokenSet);
		}

		public Task SaveAsync(MobileTokenSet tokenSet)
		{
			_tokenSet = tokenSet;
			return Task.CompletedTask;
		}

		public Task ClearAsync()
		{
			_tokenSet = null;
			return Task.CompletedTask;
		}
	}
}
