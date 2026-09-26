using System.Runtime.Versioning;

using CheatEngine.Client.Tests.Infrastructure;

using Microsoft.Win32;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The registry snapshot, on a test-owned scratch key only (<c>HKCU\Software\CheatEngine.Client.Tests\&lt;guid&gt;</c>,
///     removed afterwards with its parent when empty): every value type round-trips through the JSON backup, a restore
///     puts back exactly the captured tree, and the guard refuses any key but the Cheat Engine key and scratch keys.
/// </summary>
[Collection(ScratchRegistrySerialGroup.Name)]
[SupportedOSPlatform("windows")]
public sealed class RegistrySnapshotTests : IDisposable
{
	private readonly ScratchRegistryKey _scratch = new();

	public void Dispose()
	{
		_scratch.Dispose();
	}

	[Fact]
	public void EveryValueTypeRoundTripsThroughTheBackupAndTheRestore()
	{
		using (RegistryKey key = _scratch.Create())
		{
			key.SetValue(string.Empty, "default value");
			key.SetValue("String", "text with \"quotes\" and é");
			key.SetValue("Expand", "%TEMP%\\cheat", RegistryValueKind.ExpandString);
			key.SetValue("Multi", new[] { "first", string.Empty, "third" }, RegistryValueKind.MultiString);
			key.SetValue("DWord", -2, RegistryValueKind.DWord);
			key.SetValue("QWord", long.MinValue, RegistryValueKind.QWord);
			key.SetValue("Binary", new byte[] { 0, 1, 254, 255 }, RegistryValueKind.Binary);
			key.SetValue("None", new byte[] { 7 }, RegistryValueKind.None);
		}

		using (RegistryKey plugins = _scratch.Create(@"Plugins64\00"))
		{
			plugins.SetValue("Path", "plugin.dll");
		}

		RegistryTreeSnapshot captured = RegistrySnapshot.Capture(_scratch.SubKey);
		string text = RegistrySnapshot.Serialize(captured);
		RegistryTreeSnapshot parsed = RegistrySnapshot.Parse(text);
		using (RegistryKey key = _scratch.Create())
		{
			key.SetValue("DWord", 5, RegistryValueKind.DWord);
			key.DeleteValue("Binary");
			key.CreateSubKey("Added", false).Dispose();
		}

		RegistrySnapshot.Restore(parsed);

		Assert.Equal(text, RegistrySnapshot.Serialize(parsed));
		Assert.Equal(text, RegistrySnapshot.Serialize(RegistrySnapshot.Capture(_scratch.SubKey)));
		using RegistryKey restored = Registry.CurrentUser.OpenSubKey(_scratch.SubKey, false)!;
		Assert.Equal("%TEMP%\\cheat", restored.GetValue("Expand", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
		Assert.Equal(RegistryValueKind.ExpandString, restored.GetValueKind("Expand"));
		Assert.Equal(-2, restored.GetValue("DWord"));
		Assert.Equal(new byte[] { 0, 1, 254, 255 }, restored.GetValue("Binary"));
		Assert.Equal(["00"], restored.OpenSubKey("Plugins64")!.GetSubKeyNames());
		Assert.DoesNotContain("Added", restored.GetSubKeyNames());
		Assert.Contains("\"kind\": \"QWord\"", text, StringComparison.Ordinal);
		Assert.Contains($"\"key\": \"HKEY_CURRENT_USER\\\\{_scratch.SubKey.Replace("\\", "\\\\", StringComparison.Ordinal)}\"", text,
			StringComparison.Ordinal);
	}

	[Fact]
	public void AnAbsentKeyIsCapturedAsAbsentAndRestoredAsAbsent()
	{
		RegistryTreeSnapshot absent = RegistrySnapshot.Capture(_scratch.SubKey);
		using (RegistryKey key = _scratch.Create("Created"))
		{
			key.SetValue("By", "the session");
		}

		RegistrySnapshot.Restore(RegistrySnapshot.Parse(RegistrySnapshot.Serialize(absent)));

		Assert.False(absent.Exists);
		Assert.False(_scratch.Exists());
	}

	[Theory]
	[InlineData(@"Software\Microsoft")]
	[InlineData(@"Software")]
	[InlineData(@"Software\CheatEngine.Client.Tests")]
	[InlineData(@"Software\CheatEngine.Client.Tests\not-a-guid")]
	[InlineData(@"Software\CheatEngine.Client.Tests\0123456789abcdef0123456789abcdef\child")]
	[InlineData(@"Software\Cheat Engine\Plugins64")]
	public void OnlyTheCheatEngineKeyAndScratchKeysAreGuarded(string subKey)
	{
		Assert.Throws<ArgumentException>(() => CheatEngineUserStateLocations.RequireGuardedSubKey(subKey));
	}

	[Fact]
	public void ARestoreOfAnUnguardedKeyIsRefusedBeforeItDeletesAnything()
	{
		// A missing key below the scratch parent: even a broken check could not delete anything that exists.
		RegistryTreeSnapshot unguarded = new(CheatEngineUserStateLocations.ScratchRegistryParent + @"\not-a-guid", null);

		Assert.Throws<ArgumentException>(() => RegistrySnapshot.Restore(unguarded));
	}

	[Fact]
	public void TheGuardLocationsPairTheCheatEngineKeyWithAppDataAndScratchKeysWithTemp()
	{
		string temporary = Path.Combine(Path.GetTempPath(), "CheatEngine.Client.Tests", "appdata");

		CheatEngineUserStateLocations.Workstation.Validate();
		new CheatEngineUserStateLocations(_scratch.SubKey, temporary).Validate();
		Assert.Throws<ArgumentException>(() => new CheatEngineUserStateLocations(_scratch.SubKey,
			CheatEngineUserStateLocations.Workstation.AppDataDirectory).Validate());
		Assert.Throws<ArgumentException>(() => new CheatEngineUserStateLocations(_scratch.SubKey, Path.GetTempPath()).Validate());
		Assert.Throws<ArgumentException>(() => new CheatEngineUserStateLocations(CheatEngineUserStateLocations.CheatEngineRegistrySubKey,
			temporary).Validate());
		Assert.Equal(@"Software\Cheat Engine", CheatEngineUserStateLocations.Workstation.RegistrySubKey);
	}

	[Fact]
	public void TheScratchKeyAndItsEmptyParentAreRemoved()
	{
		ScratchRegistryKey scratch = new();
		scratch.Create("child").Dispose();

		scratch.Dispose();

		Assert.False(scratch.Exists());
		using RegistryKey? parent = Registry.CurrentUser.OpenSubKey(CheatEngineUserStateLocations.ScratchRegistryParent, false);
		// Only this test's own key sits under the parent here (_scratch was never created), unless another test process
		// holds one right now.
		Assert.True(parent is null || parent.SubKeyCount > 0,
			"The scratch parent HKCU\\Software\\CheatEngine.Client.Tests was left behind empty.");
	}

	[Fact]
	public void TheFolderBackupRestoresFilesAndFoldersByteForByte()
	{
		using TemporaryDirectory temporary = new("LiveQualificationAppData");
		string folder = Path.Combine(temporary.Path, "Cheat Engine");
		Directory.CreateDirectory(Path.Combine(folder, "tables", "empty"));
		File.WriteAllText(Path.Combine(folder, "settings.txt"), "original");
		File.WriteAllBytes(Path.Combine(folder, "tables", "a.ct"), [1, 2, 3]);

		FileTreeSnapshot before = FileTreeBackup.Backup(folder, Path.Combine(temporary.Path, "backup"));
		File.WriteAllText(Path.Combine(folder, "settings.txt"), "changed by the session");
		File.Delete(Path.Combine(folder, "tables", "a.ct"));
		File.WriteAllText(Path.Combine(folder, "new.txt"), "added");
		FileTreeBackup.Restore(folder, Path.Combine(temporary.Path, "backup"), FileTreeBackup.Parse(FileTreeBackup.Serialize(folder, before)));

		Assert.Equal(before.Entries, FileTreeBackup.Capture(folder).Entries);
		Assert.Contains("tables/empty/", before.Entries);
		Assert.Equal("original", File.ReadAllText(Path.Combine(folder, "settings.txt")));
		Assert.False(File.Exists(Path.Combine(folder, "new.txt")));
	}
}
