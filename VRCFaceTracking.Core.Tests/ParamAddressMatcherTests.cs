using System.Text.RegularExpressions;
using VRCFaceTracking.Core.OSC.DataTypes;

namespace VRCFaceTracking.Core.Tests;

public class ParamAddressMatcherTests
{
    private static readonly string[] ParamNames =
    {
        "JawOpen",
        "v2/JawOpen",
        "v2/EyeLidLeft",
        "EyeLeftX",
        "Eye",
        "SmileSadLeft",
        "v3/TongueOut",
    };

    private static readonly string[] Addresses =
    {
        "/avatar/parameters/JawOpen",
        "/avatar/parameters/v2/JawOpen",
        "/avatar/parameters/v1/JawOpen",
        "/avatar/parameters/v10/JawOpen",
        "/avatar/parameters/xv2/JawOpen",
        "/avatar/parameters/v2/EyeLidLeft",
        "/avatar/parameters/EyeLeftX",
        "/avatar/parameters/NotJawOpen",
        "/avatar/parameters/JawOpen1",
        "/avatar/parameters/JawOpen12",
        "/avatar/parameters/v2/JawOpen3",
        "JawOpen",
        "v2/JawOpen",
        "JawOpen7",
        "/JawOpen",
        "/v2/JawOpen",
        "/avatar/parameters/Eye",
        "/avatar/parameters/prefix/Eye",
        "/avatar/parameters/SmileSadLeft",
        "/avatar/parameters/v3/TongueOut",
        "",
        "/",
    };

    private static Regex ExactRegex(string name) =>
        new(@"(?<!(v\d+))(/" + name + ")$|^(" + name + ")$");

    private static Regex IndexedRegex(string name) =>
        new(@"(?<!(v\d+))/" + name + @"\d+$|^" + name + @"\d+$");

    [Fact]
    public void Matches_IsEquivalentToTheOriginalExactRegex()
    {
        foreach (var name in ParamNames)
        {
            var regex = ExactRegex(name);
            foreach (var address in Addresses)
            {
                Assert.Equal(regex.IsMatch(address), ParamAddressMatcher.Matches(address, name));
            }
        }
    }

    [Fact]
    public void MatchesIndexed_IsEquivalentToTheOriginalIndexedRegex()
    {
        foreach (var name in ParamNames)
        {
            var regex = IndexedRegex(name);
            foreach (var address in Addresses)
            {
                Assert.Equal(regex.IsMatch(address), ParamAddressMatcher.MatchesIndexed(address, name));
            }
        }
    }

    [Theory]
    [InlineData("/avatar/parameters/JawOpen", "JawOpen", true)]
    [InlineData("/avatar/parameters/v2/JawOpen", "JawOpen", false)]
    [InlineData("/avatar/parameters/v2/JawOpen", "v2/JawOpen", true)]
    [InlineData("/avatar/parameters/NotJawOpen", "JawOpen", false)]
    [InlineData("JawOpen", "JawOpen", true)]
    public void Matches_KnownCases(string address, string name, bool expected) =>
        Assert.Equal(expected, ParamAddressMatcher.Matches(address, name));
}
