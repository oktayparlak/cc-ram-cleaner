using System.Xml.Linq;
using RamCleaner.Core.Startup;

namespace RamCleaner.Core.Tests;

// I4: schtasks defaults stop the task on battery and after 72 h — the logon task must override them.
public class ScheduledTaskXmlTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly XDocument _xml = XDocument.Parse(ScheduledTaskXml.Build(@"C:\Tools & Apps\RamCleaner.exe", @"DESKTOP\oktay"));

    private string Value(string name) => _xml.Descendants(Ns + name).Single().Value;

    [Fact]
    public void KeepsRunningOnBattery_AndHasNoTimeLimit()
    {
        Assert.Equal("false", Value("DisallowStartIfOnBatteries"));
        Assert.Equal("false", Value("StopIfGoingOnBatteries"));
        Assert.Equal("PT0S", Value("ExecutionTimeLimit"));
    }

    [Fact]
    public void RunsElevated_AtThisUsersLogon_Minimized()
    {
        Assert.Equal("HighestAvailable", Value("RunLevel"));
        Assert.Equal(@"DESKTOP\oktay", _xml.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId")!.Value);
        Assert.Equal(@"C:\Tools & Apps\RamCleaner.exe", Value("Command"));
        Assert.Equal("--minimized", Value("Arguments"));
    }
}
