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
    public void MixedDocument_RawPathIsRescued_EscapedValuesAreNotReDoubled()
    {
        // REV: one raw path makes the whole document fail to parse. The fallback must fix ONLY that string, not
        // re-double the correctly escaped ones beside it (the original bug, back through the fallback).
        var config = Load(
            "executable = \"C:\\Program Files\\nodejs\\node.exe\"\n" +
            "arguments = \"C:\\\\DEV\\\\probe.mjs --server medarms03\\\\SQLEXPRESS\"",
            "DOC_ROOTS = \"D:\\\\SXServer;\\\\\\\\fixturehost\\\\share\"");
        Assert.Equal(@"C:\Program Files\nodejs\node.exe", config.Process.Executable);
        Assert.Equal(@"C:\DEV\probe.mjs --server medarms03\SQLEXPRESS", config.Process.Arguments);
        Assert.Equal(@"D:\SXServer;\\fixturehost\share", config.Process.Environment["DOC_ROOTS"]);
    }

    [Fact]
    public void MixedWithinOneString_IsRefusedNotGuessed()
    {
        // "C:\\Users\x": a valid escaped pair plus a raw backslash. Doubling all would corrupt the escaped half.
        var ex = Assert.Throws<ConfigurationException>(() =>
            Load("executable = \"node\"\narguments = \"C:\\\\Users\\x\\run.js\""));
        Assert.Contains("'process.arguments'", ex.Message);
        Assert.Contains("mixes escaped", ex.Message);
    }

    [Fact]
    public void RawUncPath_IsStillRescued()
    {
        var config = Load("executable = \"node\"", "SHARE = \"\\\\fixturehost\\share\\x\"");
        Assert.Equal(@"\\fixturehost\share\x", config.Process.Environment["SHARE"]);
    }

    [Fact]
    public void Fallback_NeverReachesIntoALiteralString()
    {
        // The document fails to parse (raw executable), so the fallback runs. The old finder matched the quoted
        // "C:\x" INSIDE the literal string and doubled it.
        var config = Load(
            "executable = \"C:\\Program Files\\nodejs\\node.exe\"\n" +
            "arguments = 'run \"C:\\x\" now'");
        Assert.Equal(@"C:\Program Files\nodejs\node.exe", config.Process.Executable);
        Assert.Equal("run \"C:\\x\" now", config.Process.Arguments);
    }

    [Fact]
    public void Fallback_LeavesATrailingCommentAlone()
    {
        var config = Load("executable = \"C:\\Program Files\\nodejs\\node.exe\"   # was \"C:\\old\\node.exe\"");
        Assert.Equal(@"C:\Program Files\nodejs\node.exe", config.Process.Executable);
    }

    [Fact]
    public void CrlfDocument_RawPathIsStillRescued()
    {
        // REV: our generator writes CRLF; the LF-anchored rescue used to miss every line.
        var config = ConfigLoader.LoadFromString(
            "[service]\r\nname = \"sd018-test\"\r\ndisplay_name = \"sd018-test\"\r\n\r\n" +
            "[process]\r\nexecutable = \"C:\\Program Files\\nodejs\\node.exe\"\r\n" +
            "arguments = \"C:\\\\DEV\\\\probe.mjs --server medarms03\\\\SQLEXPRESS\"\r\n");
        Assert.Equal(@"C:\Program Files\nodejs\node.exe", config.Process.Executable);
        Assert.Equal(@"C:\DEV\probe.mjs --server medarms03\SQLEXPRESS", config.Process.Arguments);
    }

    [Fact]
    public void RawDriveAndUncRootList_IsRescuedNotCalledMixed()
    {
        // The DOC_ROOTS shape written raw: the "\\" opening the second segment is a UNC prefix.
        var config = Load("executable = \"C:\\Program Files\\nodejs\\node.exe\"", "DOC_ROOTS = \"D:\\SX;\\\\fixturehost\\share\"");
        Assert.Equal(@"D:\SX;\\fixturehost\share", config.Process.Environment["DOC_ROOTS"]);
    }

    [Fact]
    public void RawPathInsideAnArray_IsNotRescued_AndReportsTheParseError()
    {
        // The rescue is deliberately narrow (key = "..." lines only); an array of raw paths fails closed.
        var ex = Assert.Throws<ConfigurationException>(() =>
            ConfigLoader.LoadFromString(
                "[service]\nname = \"sd018-test\"\ndisplay_name = \"sd018-test\"\n\n" +
                "[process]\nexecutable = \"node\"\npaths = [\"C:\\Program Files\\x\"]\n"));
        Assert.Contains("Failed to parse TOML configuration", ex.Message);
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
    public void MultiLineEnvironmentValue_IsAllowed()
    {
        // A newline is legitimate in a non-path value; only path-shaped values refuse CR/LF.
        var config = Load("executable = \"node\"", "BANNER = \"line one\\nline two\"");
        Assert.Equal("line one\nline two", config.Process.Environment["BANNER"]);
    }

    [Fact]
    public void PathShapedValueWithANewline_IsRefused()
    {
        // "C:\new" is legal TOML whose \n is a newline: the classic unescaped path. (A path with ANY invalid escape,
        // e.g. "C:\new\out", fails to parse as written and is rescued correctly instead.)
        var ex = Assert.Throws<ConfigurationException>(() => Load("executable = \"node\"", "OUT = \"C:\\new\""));
        Assert.Contains("process.environment.OUT", ex.Message);
    }

    [Fact]
    public void InvalidToml_ThatThePreprocessorCannotChange_ReportsTheParseError()
    {
        var ex = Assert.Throws<ConfigurationException>(() => Load("executable = \"node"));
        Assert.Contains("Failed to parse TOML configuration", ex.Message);
    }
}
