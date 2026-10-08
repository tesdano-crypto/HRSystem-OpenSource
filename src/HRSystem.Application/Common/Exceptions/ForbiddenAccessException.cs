namespace HRSystem.Application.Common.Exceptions;

public sealed class ForbiddenAccessException(string message = "您沒有執行此操作的權限。") : Exception(message);
