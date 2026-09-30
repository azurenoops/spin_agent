using System.Reflection;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class SystemEnvironmentRlsTests
{
    [Fact]
    public void SharedRegistrationRead_KeepsOwnerOnlyUpdateDeleteAndInsert()
    {
        // Arrange
        var method = typeof(RlsPolicyInstaller).GetMethod("BuildPolicySql", BindingFlags.NonPublic | BindingFlags.Static)!;
        var targets = new[] { ("dbo", "AzureSubscriptionRegistrations", "TenantId") };
        // Act
        var sql = (string)method.Invoke(null, [targets])!;
        // Assert
        sql.Should().Contain("ADD FILTER PREDICATE dbo.fn_EnvironmentRegistrationReadPredicate([TenantId], [Id])");
        sql.Should().Contain("ADD BLOCK PREDICATE dbo.fn_TenantPredicate([TenantId]) ON [dbo].[AzureSubscriptionRegistrations] BEFORE UPDATE");
        sql.Should().Contain("ADD BLOCK PREDICATE dbo.fn_TenantPredicate([TenantId]) ON [dbo].[AzureSubscriptionRegistrations] BEFORE DELETE");
        sql.Should().Contain("ADD BLOCK PREDICATE dbo.fn_TenantPredicate([TenantId]) ON [dbo].[AzureSubscriptionRegistrations] AFTER INSERT");
    }
}
