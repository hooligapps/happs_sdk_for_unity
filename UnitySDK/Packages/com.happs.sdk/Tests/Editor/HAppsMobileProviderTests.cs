using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace HAppsSDK.Tests
{
	public sealed class HAppsMobileProviderTests
	{
		[Test]
		public void Configure_UsesNonPersistentStoreOutsideAndroidRuntime()
		{
			var provider = new HAppsMobileProvider();
			provider.Configure(new HAppsMobileAuthOptions
			{
				ClientId = "client",
				PlayerPrefsStorageKey = "test.mobile.tokens"
			});

			var field = typeof(HAppsMobileProvider).GetField("_tokenStore", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null);
			Assert.That(field.GetValue(provider), Is.TypeOf<InMemoryMobileTokenStore>());
		}

		[TestCase("com.example.game://auth/callback?code=abc&state=123", true)]
		[TestCase("COM.EXAMPLE.GAME://AUTH/callback?code=abc", true)]
		[TestCase("com.example.game://auth/callback.evil?code=abc", false)]
		[TestCase("com.example.game://attacker/callback?code=abc", false)]
		[TestCase("not a uri", false)]
		public void MatchesRedirectUri_RequiresExactCallbackEndpoint(string actual, bool expected)
		{
			var method = typeof(HAppsMobileProvider).GetMethod(
				"MatchesRedirectUri",
				BindingFlags.Static | BindingFlags.NonPublic);

			Assert.That(method, Is.Not.Null);
			var result = (bool)method.Invoke(null, new object[]
			{
				actual,
				"com.example.game://auth/callback"
			});
			Assert.That(result, Is.EqualTo(expected));
		}

		[Test]
		public async Task RemoteLogoutFailure_StillClearsLocalCredentials()
		{
			var store = new InMemoryMobileTokenStore();
			await store.SaveAsync(new MobileTokenSet { AccessToken = "secret" });

			var provider = new HAppsMobileProvider();
			provider.Configure(new HAppsMobileAuthOptions { ClientId = "client" }, store);
			var method = typeof(HAppsMobileProvider).GetMethod(
				"ExecuteRemoteLogoutAndClearLocalStateAsync",
				BindingFlags.Instance | BindingFlags.NonPublic);

			Assert.That(method, Is.Not.Null);
			var task = (Task)method.Invoke(provider, new object[]
			{
				new System.Func<Task>(() => Task.FromException(new System.InvalidOperationException("remote failure")))
			});

			Assert.ThrowsAsync<System.InvalidOperationException>(async () => await task);
			Assert.That(await store.LoadAsync(), Is.Null);
		}
	}
}
