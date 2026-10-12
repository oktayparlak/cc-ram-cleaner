using System.Globalization;
using RamCleaner.Core.Rules;

namespace RamCleaner.Core.Tests;

public class NamePatternTests
{
    [Theory]
    [InlineData("Spotify", "spotify", true)]
    [InlineData("Spotify", "Spotify.exe", true)]
    [InlineData("Spotify.exe", "Spotify", true)]
    [InlineData("com.docker.*", "com.docker.backend", true)]
    [InlineData("com.docker.*", "comXdocker.backend", false)]
    [InlineData("vmmem*", "vmmemWSL", true)]
    [InlineData("qemu-system-*", "qemu-system-x86_64", true)]
    [InlineData("node", "nodemon", false)]
    [InlineData("Docker Desktop", "docker desktop", true)]
    public void Matches(string pattern, string name, bool expected) =>
        Assert.Equal(expected, NamePattern.Matches(pattern, name));

    [Fact]
    public void Matches_IsCultureInvariant_UnderTurkishCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            Assert.True(NamePattern.Matches("IDEA64", "idea64"));
            Assert.True(NamePattern.Matches("I*", "idea64"));
            Assert.True(NamePattern.Matches("MSBuild", "msbuild"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void MatchesAny_TrueWhenOnePatternMatches() =>
        Assert.True(NamePattern.MatchesAny(["chrome", "node"], "node.exe"));

    [Theory]
    [InlineData("", false)]
    [InlineData("*", false)]
    [InlineData("**", false)]
    [InlineData("a*", false)]
    [InlineData("ab*", false)]
    [InlineData("abc*", true)]
    [InlineData("Spotify", true)]
    public void IsSafeBlockPattern(string pattern, bool expected) =>
        Assert.Equal(expected, NamePattern.IsSafeBlockPattern(pattern));
}
