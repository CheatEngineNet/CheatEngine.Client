using System.Runtime.Versioning;

using CheatEngine.Client.Tests.Infrastructure;

using Microsoft.Win32;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The user state guard around a session, on a test-owned scratch key and a temporary folder standing for
///     <c>%APPDATA%\Cheat Engine</c> (never the real ones): the backup and the crash marker come before any change, the
///     plugin list is neutralized for the session, the restore is verified before the marker goes, and a marker left by a
///     crashed run is restored first and fails the next run.
/// </summary>
[Collection(ScratchRegistrySerialGroup.Name)]
[SupportedOSPlatform("windows")]
public sealed class RegistryRecoveryTests : IDisposable
{
	private readonly ScratchRegistryKey _scratch = new();
	private readonly TemporaryDirectory _temporary = new("LiveQualificationRecovery");
	private readonly string _appData;
	private readonly string _runRoot;
	private readonly CheatEngineRegistryGuard _guard;

	public RegistryRecoveryTests()
	{
		_appData = Path.Combine(_temporary.Path, "Roaming", "Cheat Engine");
		_runRoot = _temporary.CreateDirectory("runs");
		_guard = new CheatEngineRegistryGuard(new CheatEngineUserStateLocations(_scratch.SubKey, _appData), ["Plugins64"], ["LastPlugin"]);
		using (RegistryKey key = _scratch.Create())
		{
			key.SetValue("Saved", "operator setting");
			key.SetValue("LastPlugin", "C:\\plugins\\mine.dll");
		}

		using (RegistryKey plugins = _scratch.Create(@"Plugins64\00"))
		{
			plugins.SetValue("Path", "mine.dll");
		}

		Directory.CreateDirectory(_appData);
		File.WriteAllText(Path.Combine(_appData, "settings.txt"), "operator file");
	}

	public void Dispose()
	{
		_temporary.Dispose();
		_scratch.Dispose();
	}

	[Fact]
	public void TheSessionSeesTheNeutralizedStateAndTheRestoreBringsBackEverything()
	{
		string registryBefore = RegistryText();
		SandboxLayout layout = NewRun();

		using (ICheatEngineUserStateScope scope = _guard.Begin(layout))
		{
			Assert.True(File.Exists(layout.RestoreMarkerPath));
			Assert.True(File.Exists(layout.RegistryBackupPath));
			Assert.True(File.Exists(Path.Combine(layout.AppDataBackupDirectory, "settings.txt")));
			Assert.DoesNotContain("Plugins64", RegistryText());
			Assert.DoesNotContain("LastPlugin", RegistryText());
			SimulateCheatEngine();

			scope.Restore();

			Assert.True(scope.Restored);
		}

		Assert.Equal(registryBefore, RegistryText());
		Assert.Equal("operator file", File.ReadAllText(Path.Combine(_appData, "settings.txt")));
		Assert.False(File.Exists(Path.Combine(_appData, "created.txt")));
		Assert.False(File.Exists(layout.RestoreMarkerPath));
	}

	[Fact]
	public void DisposingTheScopeRestoresWhenTheSessionThrows()
	{
		string registryBefore = RegistryText();
		SandboxLayout layout = NewRun();

		Assert.Throws<TimeoutException>(Session);

		Assert.Equal(registryBefore, RegistryText());
		Assert.False(File.Exists(layout.RestoreMarkerPath));

		void Session()
		{
			using ICheatEngineUserStateScope scope = _guard.Begin(layout);
			SimulateCheatEngine();
			throw new TimeoutException("the session timed out");
		}
	}

	[Fact]
	public void AMarkerLeftByACrashedRunIsRestoredFirstAndFailsTheNextRun()
	{
		string registryBefore = RegistryText();
		SandboxLayout crashed = NewRun();
		_ = _guard.Begin(crashed);
		SimulateCheatEngine();

		InvalidOperationException stopped = Assert.Throws<InvalidOperationException>(() => _guard.Begin(NewRun()));

		Assert.Contains($"Run {crashed.RunId} ended without restoring", stopped.Message, StringComparison.Ordinal);
		Assert.Contains("restored and verified", stopped.Message, StringComparison.Ordinal);
		Assert.Equal(registryBefore, RegistryText());
		Assert.Equal("operator file", File.ReadAllText(Path.Combine(_appData, "settings.txt")));
		Assert.False(File.Exists(crashed.RestoreMarkerPath));
		using ICheatEngineUserStateScope next = _guard.Begin(NewRun());
		next.Restore();
		Assert.True(next.Restored);
	}

	[Fact]
	public void TheMarkerStaysUntilTheRestoreIsVerified()
	{
		SandboxLayout crashed = NewRun();
		_ = _guard.Begin(crashed);
		SimulateCheatEngine();
		string backup = Path.Combine(crashed.AppDataBackupDirectory, "settings.txt");
		File.WriteAllText(backup, "corrupted backup");

		InvalidOperationException failed = Assert.Throws<InvalidOperationException>(() => _guard.Begin(NewRun()));

		Assert.Contains("restoring its backup failed now", failed.Message, StringComparison.Ordinal);
		Assert.True(File.Exists(crashed.RestoreMarkerPath));
		File.WriteAllText(backup, "operator file");
		Assert.Throws<InvalidOperationException>(() => _guard.Begin(NewRun()));
		Assert.False(File.Exists(crashed.RestoreMarkerPath));
		Assert.Equal("operator file", File.ReadAllText(Path.Combine(_appData, "settings.txt")));
	}

	[Fact]
	public void AnAppDataFolderTheSessionCreatedIsRemovedAgain()
	{
		Directory.Delete(_appData, true);
		SandboxLayout layout = NewRun();

		using (ICheatEngineUserStateScope scope = _guard.Begin(layout))
		{
			Directory.CreateDirectory(_appData);
			File.WriteAllText(Path.Combine(_appData, "created.txt"), "by Cheat Engine");
		}

		Assert.False(Directory.Exists(_appData));
		Assert.Contains("\"exists\": false", File.ReadAllText(layout.AppDataManifestPath), StringComparison.Ordinal);
	}

	private SandboxLayout NewRun()
	{
		return SandboxLayout.Create(_runRoot, DateTimeOffset.UtcNow);
	}

	private string RegistryText()
	{
		return RegistrySnapshot.Serialize(RegistrySnapshot.Capture(_scratch.SubKey));
	}

	/// <summary>What Cheat Engine might do to its user state during a session.</summary>
	private void SimulateCheatEngine()
	{
		using (RegistryKey key = _scratch.Create())
		{
			key.SetValue("Saved", "changed by Cheat Engine");
			key.SetValue("NewSetting", 1, RegistryValueKind.DWord);
		}

		_scratch.Create(@"Plugins64\01").Dispose();
		File.WriteAllText(Path.Combine(_appData, "settings.txt"), "changed by Cheat Engine");
		File.WriteAllText(Path.Combine(_appData, "created.txt"), "by Cheat Engine");
	}
}
