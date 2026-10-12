using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using CCRamCleaner.Core.State;
using CCRamCleaner.Core.Tests.TestSupport;

namespace CCRamCleaner.Core.Tests;

// I6: the elevated app must not trust a journal folder a normal user can create, own, or redirect.
[Trait("Category", "Windows")]
public class SecureDirectoryTests
{
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);

    [Fact]
    public void NewDirectory_GetsProtectedAdminAndSystemOnlyAcl()
    {
        var path = Path.GetDirectoryName(TempPaths.NewFile("x"))!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); // only the leaf gets the locked-down ACL
        try
        {
            SecureDirectory.Ensure(path, _ => true);

            var acl = new DirectoryInfo(path).GetAccessControl();
            Assert.True(acl.AreAccessRulesProtected);
            var identities = acl.GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Select(r => (SecurityIdentifier)r.IdentityReference)
                .ToHashSet();
            Assert.Equal(new HashSet<SecurityIdentifier> { Admins, System }, identities);
        }
        finally { Unlock(path); }
    }

    [Fact]
    public void DirectoryWithUntrustedOwner_IsRefused()
    {
        var path = Path.GetDirectoryName(TempPaths.NewFile("x"))!;
        Directory.CreateDirectory(path);
        Assert.Throws<UnsafeDirectoryException>(() => SecureDirectory.Ensure(path, _ => false));
    }

    [Fact]
    public void Junction_IsRefused()
    {
        var root = Path.GetDirectoryName(TempPaths.NewFile("x"))!;
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!)
            mklink.WaitForExit(5000);
        Assert.True(Directory.Exists(link));

        Assert.Throws<UnsafeDirectoryException>(() => SecureDirectory.Ensure(link, _ => true));
    }

    /// <summary>The test user (owner) gives itself access back so the temp folder can be cleaned up.</summary>
    private static void Unlock(string path)
    {
        var acl = new DirectoryInfo(path).GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(acl);
        Directory.Delete(path, recursive: true);
    }
}
