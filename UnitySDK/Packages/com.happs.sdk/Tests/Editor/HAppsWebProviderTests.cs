using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace HAppsSDK.Tests
{
	public sealed class HAppsWebProviderTests
	{
		[Test]
		public async Task MakePayment_CompletesOnImmediateFailure()
		{
			var provider = new HAppsWebProvider();
			try
			{
				var task = provider.MakePayment("order-1");
				GetBridge(provider).OnMessage(
					"{\"type\":\"payment\",\"paymentData\":{\"status\":\"fail\",\"error\":\"payment_in_progress\"}}");

				var result = await task;
				Assert.That(result.Status, Is.EqualTo(PaymentStatus.Fail));
				Assert.That(result.error, Is.EqualTo("payment_in_progress"));
			}
			finally
			{
				provider.Dispose();
			}
		}

		[Test]
		public async Task GetProfile_ThrowsTypedBridgeError()
		{
			var provider = new HAppsWebProvider();
			try
			{
				var task = provider.GetProfile();
				GetBridge(provider).OnMessage(
					"{\"type\":\"profile\",\"error\":{\"code\":\"NOT_AUTHENTICATED\",\"message\":\"User not authenticated\"}}");

				try
				{
					await task;
					Assert.Fail("Expected HAppsException.");
				}
				catch (HAppsException exception)
				{
					Assert.That(exception.Code, Is.EqualTo("NOT_AUTHENTICATED"));
				}
			}
			finally
			{
				provider.Dispose();
			}
		}

		[Test]
		public void DisposedProvider_RejectsNewOperations()
		{
			var provider = new HAppsWebProvider();
			provider.Dispose();

			Assert.Throws<ObjectDisposedException>(() => provider.Connect());
		}

		[Test]
		public async Task PortalAuth_AlreadyVerified_CompletesWithoutNewBridgeResponse()
		{
			var provider = new HAppsWebProvider();
			try
			{
				var connectTask = provider.Connect();
				GetBridge(provider).OnMessage(
					"{\"type\":\"connect\",\"initData\":{\"ready\":true},\"userData\":{\"userId\":\"user-1\",\"verified\":true}}");
				await connectTask;

				var result = await provider.OpenPortalAuthPopup();
				Assert.That(result, Is.True);
			}
			finally
			{
				provider.Dispose();
			}
		}

		private static HAppsJSBridge GetBridge(HAppsWebProvider provider)
		{
			var field = typeof(HAppsWebProvider).GetField("_bridge", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null);
			return (HAppsJSBridge)field.GetValue(provider);
		}
	}
}
