using FluentAssertions;
using NexusMods.DataModel.Games;
using NexusMods.Paths;

namespace NexusMods.StandardGameLocators.Tests;

public class BasicTests
{
    private readonly IGame _game;

    public BasicTests(IGame game)
    {
        _game = game;
    }

    [Fact]
    public void Test_Locators_Linux()
    {
        if (!OperatingSystem.IsLinux()) return;

        _game.Installations.Should().SatisfyRespectively(
            steamInstallation =>
            {
                steamInstallation.Locations
                    .Should().ContainSingle()
                    .Which.Value
                    .ToString().Should().Contain("steam_game");
            });
    }

    [Fact]
    public void Test_Locators_Windows()
    {
        if (!OperatingSystem.IsWindows()) return;

        _game.Installations.Should().SatisfyRespectively(
            eaInstallation =>
            {
                eaInstallation.Locations
                    .Should().ContainSingle()
                    .Which.Value
                    .ToString().Should().Contain("ea_game");
            },
            epicInstallation =>
            {
                epicInstallation.Locations
                    .Should().ContainSingle()
                    .Which.Value
                    .ToString().Should().Contain("epic_game");
            },
            originInstallation =>
            {
                originInstallation.Locations
                    .Should().ContainSingle()
                    .Which.Value
                    .ToString().Should().Contain("origin_game");
            },
            gogInstallation =>
            {
                gogInstallation.Locations
                    .Should().ContainSingle()
                    .Which.Value
                    .ToString().Should().Contain("gog_game");
            },
            steamInstallation =>
            {
                steamInstallation.Locations
                    .Should().ContainSingle()
                    .Which.Value
                    .ToString().Should().Contain("steam_game");
            },
            xboxInstallation =>
            {
                xboxInstallation.Locations
                    .Should().ContainSingle()
                    .Which.Value
                    .ToString().Should().Contain("xbox_game");
            });
    }

    [Fact]
    public void CanFindGames()
    {
        _game.Installations.Should().NotBeEmpty();
    }
}
