using System.Runtime.Versioning;

using Microsoft.Win32;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The serial collection of the tests that write the registry. They only ever touch their own scratch key, but they
///     share its parent, which each of them removes once it is empty.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScratchRegistrySerialGroup
{
	/// <summary>The collection name.</summary>
	public const string Name = "Registry scratch key";
}

/// <summary>
///     A test-owned key <c>HKCU\Software\CheatEngine.Client.Tests\&lt;guid&gt;</c>, the only registry location a CI test
///     writes. Disposing it deletes the key tree, then deletes the parent <c>HKCU\Software\CheatEngine.Client.Tests</c> when
///     no other test process holds a key under it. <c>HKCU\Software\Cheat Engine</c> is never opened.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ScratchRegistryKey : IDisposable
{
	internal ScratchRegistryKey()
	{
		SubKey = CheatEngineUserStateLocations.ScratchRegistryParent + "\\" + Guid.NewGuid().ToString("N");
		CheatEngineUserStateLocations.RequireGuardedSubKey(SubKey);
	}

	/// <summary>The scratch key, below <c>HKEY_CURRENT_USER</c>.</summary>
	internal string SubKey
	{
		get;
	}

	/// <summary>Whether the parent key currently exists.</summary>
	internal static bool ParentExists()
	{
		using RegistryKey? parent = Registry.CurrentUser.OpenSubKey(CheatEngineUserStateLocations.ScratchRegistryParent, false);
		return parent is not null;
	}

	/// <summary>Creates (or opens) the scratch key, or one of its subkeys, for writing.</summary>
	internal RegistryKey Create(string? child = null)
	{
		return Registry.CurrentUser.CreateSubKey(child is null ? SubKey : SubKey + "\\" + child, true);
	}

	/// <summary>Whether the scratch key exists.</summary>
	internal bool Exists()
	{
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SubKey, false);
		return key is not null;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		Registry.CurrentUser.DeleteSubKeyTree(SubKey, false);
		bool empty;
		using (RegistryKey? parent = Registry.CurrentUser.OpenSubKey(CheatEngineUserStateLocations.ScratchRegistryParent, false))
		{
			empty = parent is not null && parent.SubKeyCount == 0 && parent.ValueCount == 0;
		}

		if (!empty)
		{
			return;
		}

		try
		{
			Registry.CurrentUser.DeleteSubKey(CheatEngineUserStateLocations.ScratchRegistryParent, false);
		}
		catch (InvalidOperationException)
		{
			// Another test process created its own scratch key in the meantime; it removes the parent itself.
		}
	}
}
