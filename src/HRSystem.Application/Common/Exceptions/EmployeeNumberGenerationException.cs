namespace HRSystem.Application.Common.Exceptions;

public sealed class EmployeeNumberGenerationException : Exception
{
    public EmployeeNumberGenerationException(
        string message = "員工編號產生服務尚未完成部署，請聯絡系統管理員。",
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
