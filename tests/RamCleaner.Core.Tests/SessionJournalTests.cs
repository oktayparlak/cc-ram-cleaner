using RamCleaner.Core.Services;
using RamCleaner.Core.State;
using RamCleaner.Core.Tests.TestSupport;

namespace RamCleaner.Core.Tests;

public class SessionJournalTests
{
    private readonly string _path = TempPaths.NewFile("session.json");

    [Fact]
    public void Record_PersistsAcrossInstances()
    {
        new SessionJournal(_path).Record(new("DiagTrack", ServiceStartType.Automatic, true, true));

        var journal = new SessionJournal(_path);

        Assert.True(journal.Exists);
        Assert.Equal(new[] { new ServiceJournalEntry("DiagTrack", ServiceStartType.Automatic, true, true) }, journal.Load());
    }

    [Fact]
    public void Record_KeepsTheFirstOriginalForAService()
    {
        var journal = new SessionJournal(_path);
        journal.Record(new("DiagTrack", ServiceStartType.Automatic, false, true));
        journal.Record(new("diagtrack", ServiceStartType.Disabled, false, false));

        var entry = Assert.Single(journal.Load());
        Assert.Equal(ServiceStartType.Automatic, entry.OriginalStartType);
    }

    [Fact]
    public void Replace_WithEmpty_DeletesFile()
    {
        var journal = new SessionJournal(_path);
        journal.Record(new("Fax", ServiceStartType.Manual, false, false));
        journal.Clear();
        Assert.False(journal.Exists);
        Assert.Empty(journal.Load());
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty_AndKeepsCopy()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "[ broken");

        Assert.Empty(new SessionJournal(_path).Load());
        Assert.True(File.Exists(_path + ".corrupt"));
    }

    [Fact]
    public void Record_WhenDirectoryCannotBeCreated_Throws()
    {
        var blocker = TempPaths.NewFile("blocker");
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        File.WriteAllText(blocker, "");
        var journal = new SessionJournal(Path.Combine(blocker, "session.json"));

        Assert.ThrowsAny<IOException>(() => journal.Record(new("Fax", ServiceStartType.Manual, false, false)));
    }
}
