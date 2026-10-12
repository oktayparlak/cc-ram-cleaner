using System.Security.AccessControl;
using System.Security.Principal;

namespace CCRamCleaner.Core.State;

public sealed class UnsafeDirectoryException(string message) : Exception(message);

/// <summary>
/// The session journal is read by an elevated process and its entries are applied to services, so its folder
/// must not be creatable, ownable or redirectable by a normal user (ProgramData lets Users create subfolders).
/// </summary>
public static class SecureDirectory
{
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);

    public static void Ensure(string path) => Ensure(path, owner => owner == Admins || owner == LocalSystem);

    /// <summary>Creates the folder with an Administrators/SYSTEM-only ACL, or validates and re-locks an existing one.</summary>
    /// <exception cref="UnsafeDirectoryException">The folder is a reparse point or has an untrusted owner.</exception>
    public static void Ensure(string path, Func<SecurityIdentifier, bool> isTrustedOwner)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists)
        {
            dir.Create(LockedDown());
            dir.Refresh();
        }
        if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new UnsafeDirectoryException($"{path} is a junction or symbolic link.");

        var owner = dir.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null || !isTrustedOwner(owner))
            throw new UnsafeDirectoryException($"{path} is owned by {owner?.Value ?? "nobody"}, not by Administrators or SYSTEM.");

        dir.SetAccessControl(LockedDown());
    }

    private static DirectorySecurity LockedDown()
    {
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { Admins, LocalSystem })
        {
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }
        return acl;
    }
}
