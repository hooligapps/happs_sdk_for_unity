using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace HAppsSDK
{
	public sealed class AndroidKeystoreMobileTokenStore : IMobileTokenStore
	{
		private const string DefaultStorageKey = "happs.mobile.state.v1";
		private const string AndroidKeyStore = "AndroidKeyStore";
		private const string CipherTransformation = "AES/GCM/NoPadding";

		private readonly string _storageKey;
		private readonly string _keyAlias;

		public AndroidKeystoreMobileTokenStore(string storageKey = DefaultStorageKey)
		{
			_storageKey = string.IsNullOrWhiteSpace(storageKey) ? DefaultStorageKey : storageKey;
			_keyAlias = BuildKeyAlias(_storageKey);
		}

		public Task<MobileTokenSet> LoadAsync()
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			if (!PlayerPrefs.HasKey(_storageKey))
				return Task.FromResult<MobileTokenSet>(null);

			try
			{
				var envelopeJson = PlayerPrefs.GetString(_storageKey, string.Empty);
				var envelope = JsonUtility.FromJson<EncryptedEnvelope>(envelopeJson);
				if (envelope == null || envelope.version != 1 || string.IsNullOrWhiteSpace(envelope.iv) || string.IsNullOrWhiteSpace(envelope.ciphertext))
					throw new InvalidOperationException("Encrypted mobile token payload is invalid.");

				using var secretKey = LoadSecretKey();
				if (secretKey == null)
					throw new InvalidOperationException("Android Keystore token key is unavailable.");

				var plaintext = Decrypt(secretKey, Convert.FromBase64String(envelope.iv), Convert.FromBase64String(envelope.ciphertext));
				var tokenSet = JsonUtility.FromJson<MobileTokenSet>(Encoding.UTF8.GetString(plaintext));
				return Task.FromResult(tokenSet);
			}
			catch (Exception exception)
			{
				HAppsLog.Warn($"Unable to load encrypted mobile state; clearing it. errorType={exception.GetType().Name}");
				PlayerPrefs.DeleteKey(_storageKey);
				PlayerPrefs.Save();
				return Task.FromResult<MobileTokenSet>(null);
			}
#else
			throw new PlatformNotSupportedException("Android Keystore storage is only available on Android runtime.");
#endif
		}

		public Task SaveAsync(MobileTokenSet tokenSet)
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			if (tokenSet == null)
				return ClearAsync();

			EnsureSecretKey();
			using var secretKey = LoadSecretKey();
			if (secretKey == null)
				throw new InvalidOperationException("Android Keystore token key is unavailable.");

			var plaintext = Encoding.UTF8.GetBytes(JsonUtility.ToJson(tokenSet));
			var encrypted = Encrypt(secretKey, plaintext);
			var envelope = new EncryptedEnvelope
			{
				version = 1,
				iv = Convert.ToBase64String(encrypted.Iv),
				ciphertext = Convert.ToBase64String(encrypted.Ciphertext)
			};

			PlayerPrefs.SetString(_storageKey, JsonUtility.ToJson(envelope));
			PlayerPrefs.Save();
			return Task.CompletedTask;
#else
			throw new PlatformNotSupportedException("Android Keystore storage is only available on Android runtime.");
#endif
		}

		public Task ClearAsync()
		{
#if UNITY_ANDROID && !UNITY_EDITOR
			PlayerPrefs.DeleteKey(_storageKey);
			PlayerPrefs.Save();
			DeleteSecretKey();
			return Task.CompletedTask;
#else
			throw new PlatformNotSupportedException("Android Keystore storage is only available on Android runtime.");
#endif
		}

		private static string BuildKeyAlias(string storageKey)
		{
			using var sha256 = SHA256.Create();
			var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(storageKey));
			var suffix = new StringBuilder(24);
			for (var i = 0; i < 12; i++)
				suffix.Append(hash[i].ToString("x2"));
			return $"happs_mobile_tokens_{suffix}";
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		private void EnsureSecretKey()
		{
			using var keyStore = LoadKeyStore();
			if (keyStore.Call<bool>("containsAlias", _keyAlias))
				return;

			using var keyGeneratorClass = new AndroidJavaClass("javax.crypto.KeyGenerator");
			using var keyGenerator = keyGeneratorClass.CallStatic<AndroidJavaObject>("getInstance", "AES", AndroidKeyStore);
			using var keyProperties = new AndroidJavaClass("android.security.keystore.KeyProperties");
			var purposeEncrypt = keyProperties.GetStatic<int>("PURPOSE_ENCRYPT");
			var purposeDecrypt = keyProperties.GetStatic<int>("PURPOSE_DECRYPT");
			var blockModeGcm = keyProperties.GetStatic<string>("BLOCK_MODE_GCM");
			var paddingNone = keyProperties.GetStatic<string>("ENCRYPTION_PADDING_NONE");

			using var builder = new AndroidJavaObject(
				"android.security.keystore.KeyGenParameterSpec$Builder",
				_keyAlias,
				purposeEncrypt | purposeDecrypt);
			builder.Call<AndroidJavaObject>("setBlockModes", new object[] { new[] { blockModeGcm } });
			builder.Call<AndroidJavaObject>("setEncryptionPaddings", new object[] { new[] { paddingNone } });
			using var spec = builder.Call<AndroidJavaObject>("build");
			keyGenerator.Call("init", spec);
			keyGenerator.Call<AndroidJavaObject>("generateKey");
		}

		private AndroidJavaObject LoadSecretKey()
		{
			using var keyStore = LoadKeyStore();
			return keyStore.Call<AndroidJavaObject>("getKey", new object[] { _keyAlias, null });
		}

		private static AndroidJavaObject LoadKeyStore()
		{
			using var keyStoreClass = new AndroidJavaClass("java.security.KeyStore");
			var keyStore = keyStoreClass.CallStatic<AndroidJavaObject>("getInstance", AndroidKeyStore);
			keyStore.Call("load", new object[] { null, null });
			return keyStore;
		}

		private void DeleteSecretKey()
		{
			using var keyStore = LoadKeyStore();
			if (keyStore.Call<bool>("containsAlias", _keyAlias))
				keyStore.Call("deleteEntry", _keyAlias);
		}

		private static EncryptedBytes Encrypt(AndroidJavaObject secretKey, byte[] plaintext)
		{
			using var cipherClass = new AndroidJavaClass("javax.crypto.Cipher");
			using var cipher = cipherClass.CallStatic<AndroidJavaObject>("getInstance", CipherTransformation);
			cipher.Call("init", 1, secretKey);
			var ciphertext = cipher.Call<byte[]>("doFinal", plaintext);
			var iv = cipher.Call<byte[]>("getIV");
			return new EncryptedBytes(iv, ciphertext);
		}

		private static byte[] Decrypt(AndroidJavaObject secretKey, byte[] iv, byte[] ciphertext)
		{
			using var cipherClass = new AndroidJavaClass("javax.crypto.Cipher");
			using var cipher = cipherClass.CallStatic<AndroidJavaObject>("getInstance", CipherTransformation);
			using var parameters = new AndroidJavaObject("javax.crypto.spec.GCMParameterSpec", 128, iv);
			cipher.Call("init", 2, secretKey, parameters);
			return cipher.Call<byte[]>("doFinal", ciphertext);
		}
#endif

		[Serializable]
		private sealed class EncryptedEnvelope
		{
			public int version;
			public string iv;
			public string ciphertext;
		}

		private readonly struct EncryptedBytes
		{
			public byte[] Iv { get; }
			public byte[] Ciphertext { get; }

			public EncryptedBytes(byte[] iv, byte[] ciphertext)
			{
				Iv = iv;
				Ciphertext = ciphertext;
			}
		}
	}
}
