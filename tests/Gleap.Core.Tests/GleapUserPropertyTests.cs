using System.Collections.Generic;
using GleapSDK;
using GleapSDK.Models;
using Xunit;

namespace Gleap.Core.Tests;

public class GleapUserPropertyTests
{
    [Fact]
    public void UserProperty_HoldsAllParityFields()
    {
        var p = new GleapUserProperty
        {
            UserId = "u1",
            Name = "Ada",
            Email = "ada@example.com",
            Phone = "123",
            Plan = "pro",
            CompanyName = "Acme",
            CompanyId = "c1",
            Avatar = "https://a",
            Lang = "en",
            Value = 42.0,
            Sla = 3.0,
            CustomData = new Dictionary<string, object> { ["k"] = "v" }
        };

        Assert.Equal("u1", p.UserId);
        Assert.Equal(42.0, p.Value);
        Assert.Equal("v", p.CustomData!["k"]);
    }

    [Fact]
    public void Enums_HaveParityMembers()
    {
        Assert.Equal(3, System.Enum.GetValues(typeof(Severity)).Length);
        Assert.Equal(3, System.Enum.GetValues(typeof(LogLevel)).Length);
        Assert.Equal(2, System.Enum.GetValues(typeof(ActivationMethod)).Length);
        Assert.Equal(2, System.Enum.GetValues(typeof(SurveyFormat)).Length);
    }
}
