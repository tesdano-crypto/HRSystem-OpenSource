namespace HRSystem.Application.Common.Exceptions;

public sealed class EmployeeNumberCapacityExceededException : Exception
{
    public EmployeeNumberCapacityExceededException(Exception? innerException = null)
        : base("員工編號已達 EMP9999，無法建立新員工，請聯絡系統管理員。", innerException)
    {
    }
}
