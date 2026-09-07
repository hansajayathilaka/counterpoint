using System;
using Counterpoint.Domain.Security;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Security;

/// <summary>
/// The two roles and the permission split between them (SRS §3.3, FR-1.2).
/// </summary>
public sealed class RolesTests
{
    [Fact]
    public void FR_1_2_AnOwnerCanDoEverythingACashierCan()
    {
        Roles.Satisfies(Role.Owner, Role.Owner).Should().BeTrue();
        Roles.Satisfies(Role.Owner, Role.Cashier).Should().BeTrue(
            "SRS §3.3 gives the owner everything the cashier has, plus the rest");
    }

    [Fact]
    public void FR_1_2_ACashierCannotDoWhatOnlyTheOwnerCan()
    {
        Roles.Satisfies(Role.Cashier, Role.Cashier).Should().BeTrue();
        Roles.Satisfies(Role.Cashier, Role.Owner).Should().BeFalse();
    }

    [Fact]
    public void TheDefaultRoleIsTheLowerPrivilege()
    {
        default(Role).Should().Be(
            Role.Cashier,
            "a role field that was never set must not grant anything");
    }

    [Theory]
    [InlineData(Role.Cashier, "CASHIER")]
    [InlineData(Role.Owner, "OWNER")]
    public void TheTokensAreTheOnesTheSchemaConstrainsAppUserRoleTo(Role role, string token)
    {
        Roles.ToToken(role).Should().Be(token);
        Roles.Parse(token).Should().Be(role);
    }

    [Theory]
    [InlineData("cashier")]
    [InlineData("ADMIN")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownTokenIsRefusedRatherThanGuessed(string? token)
    {
        Roles.TryParse(token, out _).Should().BeFalse();

        if (token is not null)
        {
            var parse = () => Roles.Parse(token);

            parse.Should().Throw<ArgumentOutOfRangeException>(
                "a role nobody recognises must never be treated as either privilege level");
        }
    }
}
