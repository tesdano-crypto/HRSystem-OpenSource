namespace HRSystem.Application.Common.Exceptions;

public sealed class ConcurrencyConflictException(string message = "資料已被其他使用者更新，請重新載入後再試。") : Exception(message);
