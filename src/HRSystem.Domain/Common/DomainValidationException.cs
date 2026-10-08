namespace HRSystem.Domain.Common;

public sealed class DomainValidationException(string message) : Exception(message);
