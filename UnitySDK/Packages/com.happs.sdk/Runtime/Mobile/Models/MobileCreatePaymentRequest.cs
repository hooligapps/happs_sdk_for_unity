using System;

namespace HAppsSDK
{
	public sealed class MobileCreatePaymentRequest
	{
		public string ProductId;
		public decimal Price;
		public string Currency;
		public string Description;
		public string RequestId;
        internal MobileCreatePaymentRequest ValidatedCopy()
        {
            ValidateText(ProductId, 128, nameof(ProductId));
            ValidateText(RequestId, 128, nameof(RequestId));
            ValidateText(Currency, 10, nameof(Currency));
            ValidateText(Description, 500, nameof(Description));
            if (Price < 0.01m || Price > 99999999.99m)
                throw new ArgumentOutOfRangeException(nameof(Price));
            return (MobileCreatePaymentRequest)MemberwiseClone();
        }

        private static void ValidateText(string value, int maxLength, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
                throw new ArgumentException($"{name} is required and must not exceed {maxLength} characters.", name);
        }
	}
}
