using WinLet.Core;
using Xunit;

namespace WinLet.Core.Tests;

/// <summary>
/// SD-018: valid TOML must load exactly as written. The legacy Windows-path preprocessor used to run on EVERY document
/// and doubled each backslash in any double-quoted string starting with a drive path, corrupting escaped values.
/// </summary>
public class ConfigLoaderBackslashTests
{
    private static ServiceConfig Load(string processLines, string environmentLines = "") =>
        ConfigLoader.LoadFromString(
            "[service]\nname = \"sd018-test\"\ndisplay_name = \"sd018-test\"\n\n" +
            "[process]\n" + processLines + "\n\n" +
            "[process.environment]\n" + environmentLines + "\n");

    [Fact]
    public void EscapedDrivePath_LoadsWithSingleBackslashes()
    {
        // The form cictl's _toml_escape writes.
        var config = Load("executable = \"C:\\\\Program Files\\\\nodejs\\\\node.exe\"");
        Assert.Equal(@"C:\Program Files\nodejs\node.exe", config.Process.Executable);
    }

    [Fact]
    public void DrivePathFollowedByNonPathToken_KeepsTheTokenIntact()
    {
        // VS-403 check j: this argument reached node as "--server medarms03\\SQLEXPRESS".
        var config = Load(
            "executable = \"node\"\n" +
            "arguments = \"C:\\\\DEV\\\\vs403\\\\probe.mjs --server medarms03\\\\SQLEXPRESS\"");
        Assert.Equal(@"C:\DEV\vs403\probe.mjs --server medarms03\SQLEXPRESS", config.Process.Arguments);
    }

    [Fact]
    public void MixedDriveAndUncList_KeepsTheUncRoot()
    {
        // API: the UNC half used to arrive as \\\\host\\share and resolve to a local C:\host\share.
        var config = Load("executable = \"node\"", "DOC_ROOTS = \"D:\\\\SXServer;\\\\\\\\fixturehost\\\\share\"");
        Assert.Equal(@"D:\SXServer;\\fixturehost\share", config.Process.Environment["DOC_ROOTS"]);
    }

    [Fact]
    public void LiteralString_IsUntouched()
    {
        var config = Load(
            "executable = 'C:\\Program Files\\nodejs\\node.exe'\n" +
            "arguments = 'C:\\DEV\\probe.mjs --server medarms03\\SQLEXPRESS'",
            "DOC_ROOTS = 'D:\\SXServer'");
        Assert.Equal(@"C:\Program Files\nodejs\node.exe", config.Process.Executable);
        Assert.Equal(@"C:\DEV\probe.mjs --server medarms03\SQLEXPRESS", config.Process.Arguments);
        Assert.Equal(@"D:\SXServer", config.Process.Environment["DOC_ROOTS"]);
    }

    [Fact]
    public void LegacyRawPath_ThatIsNotValidToml_StillLoadsThroughTheFallback()
    {
        // "\P" is not a TOML escape, so the document fails to parse as written; the legacy preprocessor rescues it.
        var config = Load("executable = \"C:\\Program Files\\nodejs\\node.exe\"");
        Assert.Equal(@"C:\Program Files\nodejs\node.exe", config.Process.Executable);
    }

    [Fact]
    public void UnescapedPathThatHappensToBeValidToml_IsRefusedNotMangled()
    {
        // "C:\temp" parses (\t is a TAB). Refuse loudly instead of running with a mangled value.
        var ex = Assert.Throws<ConfigurationException>(() =>
            Load("executable = \"node\"\narguments = \"C:\\temp\\run.js\""));
        Assert.Contains("process.arguments", ex.Message);
        Assert.Contains("control character", ex.Message);
    }

    [Fact]
    public void InvalidToml_ThatThePreprocessorCannotChange_ReportsTheParseError()
    {
        var ex = Assert.Throws<ConfigurationException>(() => Load("executable = \"node"));
        Assert.Contains("Failed to parse TOML configuration", ex.Message);
    }
}
