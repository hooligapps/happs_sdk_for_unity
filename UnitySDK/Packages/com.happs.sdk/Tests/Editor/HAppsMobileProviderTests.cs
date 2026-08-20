using System;
using System.Linq;
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

		[Test]
		public async Task SessionGate_SerializesStateMutations()
		{
			var provider = new HAppsMobileProvider();
			var method = typeof(HAppsMobileProvider)
				.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
				.Single(candidate => candidate.Name == "RunSessionExclusiveAsync" && candidate.IsGenericMethod)
				.MakeGenericMethod(typeof(int));
			var firstEntered = NewCompletionSource<bool>();
			var secondEntered = NewCompletionSource<bool>();
			var releaseFirst = NewCompletionSource<bool>();

			var first = (Task<int>)method.Invoke(provider, new object[]
			{
				0,
				new Func<Task<int>>(async () =>
				{
					firstEntered.TrySetResult(true);
					await releaseFirst.Task;
					return 1;
				})
			});
			await firstEntered.Task;

			var second = (Task<int>)method.Invoke(provider, new object[]
			{
				0,
				new Func<Task<int>>(() =>
				{
					secondEntered.TrySetResult(true);
					return Task.FromResult(2);
				})
			});

			await Task.Delay(25);
			Assert.That(secondEntered.Task.IsCompleted, Is.False);
			releaseFirst.TrySetResult(true);

			Assert.That(await first, Is.EqualTo(1));
			Assert.That(await second, Is.EqualTo(2));
			Assert.That(secondEntered.Task.IsCompleted, Is.True);
		}

		[Test]
		public async Task Dispose_InvalidatesRunningStateMutation()
		{
			var provider = new HAppsMobileProvider();
			var method = typeof(HAppsMobileProvider)
				.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
				.Single(candidate => candidate.Name == "RunSessionExclusiveAsync" && candidate.IsGenericMethod)
				.MakeGenericMethod(typeof(int));
			var entered = NewCompletionSource<bool>();
			var release = NewCompletionSource<bool>();
			var task = (Task<int>)method.Invoke(provider, new object[]
			{
				0,
				new Func<Task<int>>(async () =>
				{
					entered.TrySetResult(true);
					await release.Task;
					return 1;
				})
			});

			await entered.Task;
			provider.Dispose();
			release.TrySetResult(true);

			try
			{
				await task;
				Assert.Fail("Expected ObjectDisposedException.");
			}
			catch (ObjectDisposedException)
			{
			}
			Assert.Throws<ObjectDisposedException>(() => provider.Configure(new HAppsMobileAuthOptions()));
		}

		[Test]
		public void LoginReservation_RejectsSecondLoginBeforeNetworkStarts()
		{
			var provider = new HAppsMobileProvider();
			var begin = typeof(HAppsMobileProvider).GetMethod("BeginLogin", BindingFlags.Instance | BindingFlags.NonPublic);
			var end = typeof(HAppsMobileProvider).GetMethod("EndLogin", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(begin, Is.Not.Null);
			Assert.That(end, Is.Not.Null);

			begin.Invoke(provider, null);
			var exception = Assert.Throws<TargetInvocationException>(() => begin.Invoke(provider, null));
			Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
			end.Invoke(provider, new object[] { null });
		}

		[Test]
		public async Task ConcurrentSessionRefreshes_ShareOneOperation()
		{
			var store = new BlockingTokenStore();
			var provider = new HAppsMobileProvider();
			provider.Configure(new HAppsMobileAuthOptions
			{
				ClientId = "client",
				DeviceRegisterUrl = "https://portal.example/device/register",
				InitSessionUrl = "https://portal.example/session/init"
			}, store);

			var first = provider.InitSessionAsync();
			var second = provider.RefreshSessionAsync();
			Assert.That(second, Is.SameAs(first));

			store.CompleteLoad(null);
			try
			{
				await first;
				Assert.Fail("Expected Android-only device signing to reject the Editor runtime.");
			}
			catch (NotSupportedException)
			{
			}
		}

		private static TaskCompletionSource<T> NewCompletionSource<T>()
		{
			return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		}

		private sealed class BlockingTokenStore : IMobileTokenStore
		{
			private readonly TaskCompletionSource<MobileTokenSet> _load = NewCompletionSource<MobileTokenSet>();

			public Task<MobileTokenSet> LoadAsync() => _load.Task;
			public Task SaveAsync(MobileTokenSet tokenSet) => Task.CompletedTask;
			public Task ClearAsync() => Task.CompletedTask;
			public void CompleteLoad(MobileTokenSet tokenSet) => _load.TrySetResult(tokenSet);
		}
	}
}
