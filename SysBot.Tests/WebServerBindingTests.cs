using FluentAssertions;
using SysBot.Pokemon.Helpers;
using Xunit;

namespace SysBot.Tests;

public class WebServerBindingTests
{
    [Fact]
    public void ExternalConnectionsDisabled_BindsOnlyToLoopback()
    {
        WebServerBinding.GetHttpPrefixes(8080, allowExternalConnections: false)
            .Should().Equal(
                "http://localhost:8080/",
                "http://127.0.0.1:8080/");
    }

    [Fact]
    public void ExternalConnectionsEnabled_BindsToAllInterfaces()
    {
        WebServerBinding.GetHttpPrefixes(8080, allowExternalConnections: true)
            .Should().Equal("http://+:8080/");
    }
}
