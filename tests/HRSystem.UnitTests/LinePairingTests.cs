using System.Security.Cryptography;
using System.Text;
using HRSystem.Application.Abstractions.Security;
using HRSystem.Application.Approvals;
using HRSystem.Application.Common.Auditing;
using HRSystem.Application.Common.Exceptions;
using HRSystem.Application.Security;
using HRSystem.Domain.Approvals;

namespace HRSystem.UnitTests;

public sealed class LinePairingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 30, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pairing_Request_Is_One_Time_Revocable_And_Purpose_Specific()
    {
        var request = Pairing("owner", "token", Now.AddMinutes(20), Now);
        Assert.True(request.IsValid(Now));
        Assert.Equal(LinePairingPurpose.PrivateUserBinding, request.Purpose);
        request.Consume(Now.AddMinutes(1));
        Assert.False(request.IsValid(Now.AddMinutes(2)));

        var revoked = Pairing("owner", "other", Now.AddMinutes(20), Now);
        revoked.Revoke(Now.AddMinutes(1));
        Assert.False(revoked.IsValid(Now.AddMinutes(2)));
    }

    [Fact]
    public void Binding_Is_Private_User_Only_And_Soft_Revoked()
    {
        var binding = new LineUserBinding(Guid.NewGuid(), "owner", "U-owner",
            Now, Now);
        binding.Deactivate(Now.AddMinutes(1));
        Assert.False(binding.IsActive);
        Assert.Equal(Now.AddMinutes(1), binding.RevokedAtUtc);
        Assert.Throws<HRSystem.Domain.Common.DomainValidationException>(() =>
            new LineUserBinding(Guid.NewGuid(), "owner", "group-id", Now, Now,
                (LineBindingTargetType)2));
    }

    [Fact]
    public async Task Private_User_Callback_Creates_Verified_Binding_And_Isolates_Approval()
    {
        await using var setup = Setup.Create();
        var pairing = await setup.Service.CreatePairingRequestAsync(new()
        { HrSystemUserId = "owner" });
        var token = pairing.PairingCommand["綁定 HRSystem ".Length..];

        var completed = await setup.Service.CompletePrivatePairingAsync(new(
            token, "U-private-owner", "user"));

        Assert.True(completed.Succeeded);
        var binding = Assert.Single(setup.Db.LineUserBindings);
        Assert.True(binding.IsActive);
        Assert.Equal(LineBindingTargetType.User, binding.TargetType);
        Assert.NotNull(Assert.Single(setup.Db.LinePairingRequests).ConsumedAtUtc);
        Assert.Empty(setup.Db.Approvals);
        Assert.Empty(setup.Db.ApprovalLineActionTokens);
        Assert.Contains(setup.Db.AuditLogs,
            x => x.Action == AuditActions.LinePairingVerified);
        Assert.DoesNotContain("U-private-owner",
            string.Join('|', setup.Db.AuditLogs.Select(x => x.NewValuesJson)),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("group")]
    [InlineData("room")]
    public async Task Non_Private_Source_Is_Rejected_Without_Consuming(string sourceType)
    {
        await using var setup = Setup.Create();
        var pairing = await setup.Service.CreatePairingRequestAsync(new()
        { HrSystemUserId = "owner" });
        var token = pairing.PairingCommand["綁定 HRSystem ".Length..];

        var result = await setup.Service.CompletePrivatePairingAsync(new(
            token, "source-id", sourceType));

        Assert.False(result.Succeeded);
        Assert.Null(Assert.Single(setup.Db.LinePairingRequests).ConsumedAtUtc);
        Assert.Empty(setup.Db.LineUserBindings);
    }

    [Fact]
    public async Task Expired_Replayed_Wrong_Purpose_And_Revoked_Tokens_Are_Rejected()
    {
        await using var setup = Setup.Create();
        setup.Db.LinePairingRequests.Add(Pairing("owner", "expired",
            Now.AddMinutes(-1), Now.AddMinutes(-2)));
        var wrongPurpose = Pairing("owner", "wrong-purpose", Now.AddMinutes(10), Now);
        typeof(LinePairingRequest).GetProperty(nameof(LinePairingRequest.Purpose))!
            .SetValue(wrongPurpose, (LinePairingPurpose)99);
        setup.Db.LinePairingRequests.Add(wrongPurpose);
        var revoked = Pairing("owner", "revoked", Now.AddMinutes(10), Now);
        revoked.Revoke(Now);
        setup.Db.LinePairingRequests.Add(revoked);
        await setup.Db.SaveChangesAsync();

        Assert.False((await setup.Service.CompletePrivatePairingAsync(new(
            "expired", "U1", "user"))).Succeeded);
        Assert.False((await setup.Service.CompletePrivatePairingAsync(new(
            "wrong-purpose", "U1", "user"))).Succeeded);
        Assert.False((await setup.Service.CompletePrivatePairingAsync(new(
            "revoked", "U1", "user"))).Succeeded);

        var valid = await setup.Service.CreatePairingRequestAsync(new()
        { HrSystemUserId = "owner" });
        var token = valid.PairingCommand["綁定 HRSystem ".Length..];
        Assert.True((await setup.Service.CompletePrivatePairingAsync(new(
            token, "U-owner", "user"))).Succeeded);
        Assert.False((await setup.Service.CompletePrivatePairingAsync(new(
            token, "U-owner", "user"))).Succeeded);
    }

    [Fact]
    public async Task Duplicate_Line_Id_Is_Rejected_And_Replacement_Must_Be_Explicit()
    {
        await using var duplicate = Setup.Create();
        duplicate.Db.LineUserBindings.Add(new LineUserBinding(Guid.NewGuid(),
            "other-owner", "U-used", Now, Now));
        await duplicate.Db.SaveChangesAsync();
        var pairing = await duplicate.Service.CreatePairingRequestAsync(new()
        { HrSystemUserId = "owner" });
        Assert.False((await duplicate.Service.CompletePrivatePairingAsync(new(
            pairing.PairingCommand["綁定 HRSystem ".Length..], "U-used", "user"))).Succeeded);

        await using var replacement = Setup.Create();
        var old = new LineUserBinding(Guid.NewGuid(), "owner", "U-old", Now, Now);
        replacement.Db.LineUserBindings.Add(old);
        await replacement.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ApplicationValidationException>(() =>
            replacement.Service.CreatePairingRequestAsync(new()
            { HrSystemUserId = "owner" }));
        var replace = await replacement.Service.CreatePairingRequestAsync(new()
        {
            HrSystemUserId = "owner",
            ReplaceExistingBinding = true,
            ReplacementReason = "Owner 主動更換私人帳號"
        });
        var result = await replacement.Service.CompletePrivatePairingAsync(new(
            replace.PairingCommand["綁定 HRSystem ".Length..], "U-new", "user"));
        Assert.True(result.Succeeded);
        Assert.False(old.IsActive);
        Assert.NotNull(old.RevokedAtUtc);
        Assert.Equal("U-new", Assert.Single(replacement.Db.LineUserBindings,
            x => x.IsActive).LineUserId);
    }

    [Fact]
    public async Task Resolver_Uses_Only_Active_Private_User_Binding()
    {
        await using var db = TestDb.Create();
        db.LineUserBindings.Add(new LineUserBinding(Guid.NewGuid(), "owner",
            "U-owner", Now, Now));
        var revoked = new LineUserBinding(Guid.NewGuid(), "revoked-owner",
            "U-revoked", Now, Now);
        revoked.Deactivate(Now.AddMinutes(1));
        db.LineUserBindings.Add(revoked);
        await db.SaveChangesAsync();
        var resolver = new LinePrivateTargetResolver(db);

        var found = await resolver.ResolveAsync("owner");
        var missing = await resolver.ResolveAsync("unknown-name-or-employee-number");
        var revokedResult = await resolver.ResolveAsync("revoked-owner");

        Assert.Equal(LinePrivateTargetStatus.Found, found.Status);
        Assert.Equal("U-owner", found.LineUserId);
        Assert.Equal(LinePrivateTargetStatus.MissingBinding, missing.Status);
        Assert.Equal(LinePrivateTargetStatus.MissingBinding, revokedResult.Status);
    }

    [Fact]
    public async Task Revoke_Is_Soft_And_Appends_Safe_Audit()
    {
        await using var setup = Setup.Create();
        var binding = new LineUserBinding(Guid.NewGuid(), "owner", "U-owner", Now, Now);
        setup.Db.LineUserBindings.Add(binding);
        await setup.Db.SaveChangesAsync();

        await setup.Service.RevokeAsync(new("owner", "Owner 要求解除"));

        Assert.False(binding.IsActive);
        Assert.NotNull(binding.RevokedAtUtc);
        Assert.Contains(setup.Db.AuditLogs,
            x => x.Action == AuditActions.LineUserBindingRevoked);
        Assert.DoesNotContain("U-owner",
            string.Join('|', setup.Db.AuditLogs.Select(x => x.NewValuesJson)),
            StringComparison.Ordinal);
    }

    private static LinePairingRequest Pairing(string userId, string token,
        DateTimeOffset expires, DateTimeOffset created) => new(Guid.NewGuid(),
            userId, SHA256.HashData(Encoding.UTF8.GetBytes(token)), expires,
            "admin", created);

    private sealed class Setup : IAsyncDisposable
    {
        private Setup(HRSystem.Infrastructure.Persistence.HRSystemDbContext db,
            LineUserBindingService service)
        { Db = db; Service = service; }
        public HRSystem.Infrastructure.Persistence.HRSystemDbContext Db { get; }
        public LineUserBindingService Service { get; }

        public static Setup Create()
        {
            var db = TestDb.Create();
            var service = new LineUserBindingService(db,
                new RoleCurrentUser(RoleNames.Admin, "admin"),
                new FakeDirectory(), new FakeSender(), new FixedTimeProvider(Now));
            return new(db, service);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeDirectory : ILinePairingUserDirectory
    {
        private static readonly LinePairingUserDto Owner = new("owner", "owner",
            "Owner", "EMP-TEST", "測試 Owner", [RoleNames.Owner]);
        public Task<IReadOnlyList<LinePairingUserDto>> GetOwnerUsersAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LinePairingUserDto>>([Owner]);
        public Task<LinePairingUserDto?> FindActiveOwnerAsync(string userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LinePairingUserDto?>(userId is "owner" or "other-owner"
                ? Owner with { UserId = userId } : null);
    }

    private sealed class FakeSender : ILinePrivateTestNotificationSender
    {
        public bool IsConfigured => false;
        public Task SendAsync(LinePrivateTestNotification notification,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RoleCurrentUser(string role, string userId) : ICurrentUser
    {
        public string? UserId => userId;
        public Guid? EmployeeId => null;
        public string? DisplayName => role;
        public string? IpAddress => "127.0.0.1";
        public bool IsAuthenticated => true;
        public bool IsInRole(string expectedRole) => role == expectedRole;
        public bool HasPermission(string policy) => RolePermissions.HasPermission([role], policy);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
