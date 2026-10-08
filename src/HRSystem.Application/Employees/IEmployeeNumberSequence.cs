namespace HRSystem.Application.Employees;

public interface IEmployeeNumberSequence
{
    Task<int> GetNextValueAsync(CancellationToken cancellationToken = default);
}
