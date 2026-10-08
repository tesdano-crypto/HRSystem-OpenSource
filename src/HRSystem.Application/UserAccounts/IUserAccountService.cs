using HRSystem.Application.Common.Models;

namespace HRSystem.Application.UserAccounts;

public interface IUserAccountService
{
    Task<PagedResult<UserAccountDto>> GetListAsync(UserAccountQuery query, CancellationToken cancellationToken = default);
    Task<UserAccountDto> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<CurrentUserProfileDto> GetCurrentProfileAsync(CancellationToken cancellationToken = default);
    Task<UserAccountDto> CreateAsync(CreateUserAccountRequest request, CancellationToken cancellationToken = default);
    Task<UserAccountDto> UpdateAsync(UpdateUserAccountRequest request, CancellationToken cancellationToken = default);
    Task SetActiveAsync(string id, bool isActive, string rowVersion, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetUserPasswordRequest request, CancellationToken cancellationToken = default);
}
