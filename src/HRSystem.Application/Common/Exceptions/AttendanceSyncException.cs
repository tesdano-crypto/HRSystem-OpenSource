namespace HRSystem.Application.Common.Exceptions;

public sealed class AttendanceSyncException : Exception
{
    public AttendanceSyncException(string message)
        : base(message)
    {
    }
}
