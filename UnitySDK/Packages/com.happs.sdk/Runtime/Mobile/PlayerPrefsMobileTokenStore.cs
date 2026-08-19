using System.Threading.Tasks;
using UnityEngine;

namespace HAppsSDK
{
	[System.Obsolete("PlayerPrefsMobileTokenStore stores tokens as plaintext. Use AndroidKeystoreMobileTokenStore or a secure custom IMobileTokenStore.")]
	public sealed class PlayerPrefsMobileTokenStore : IMobileTokenStore
	{
		private readonly string _storageKey;

		public PlayerPrefsMobileTokenStore(string storageKey = "happs.mobile.state.v1")
		{
			_storageKey = string.IsNullOrWhiteSpace(storageKey) ? "happs.mobile.state.v1" : storageKey;
		}

		public Task<MobileTokenSet> LoadAsync()
		{
			if (!PlayerPrefs.HasKey(_storageKey))
				return Task.FromResult<MobileTokenSet>(null);

			var json = PlayerPrefs.GetString(_storageKey, string.Empty);
			if (string.IsNullOrWhiteSpace(json))
				return Task.FromResult<MobileTokenSet>(null);

			return Task.FromResult(JsonUtility.FromJson<MobileTokenSet>(json));
		}

		public Task SaveAsync(MobileTokenSet tokenSet)
		{
			if (tokenSet == null)
			{
				PlayerPrefs.DeleteKey(_storageKey);
			}
			else
			{
				PlayerPrefs.SetString(_storageKey, JsonUtility.ToJson(tokenSet));
			}

			PlayerPrefs.Save();
			return Task.CompletedTask;
		}

		public Task ClearAsync()
		{
			PlayerPrefs.DeleteKey(_storageKey);
			PlayerPrefs.Save();
			return Task.CompletedTask;
		}
	}
}
