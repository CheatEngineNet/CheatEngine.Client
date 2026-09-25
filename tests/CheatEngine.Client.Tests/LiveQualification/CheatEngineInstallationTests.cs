using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

using CheatEngine.Client.Tests.Infrastructure;

using LivePlugin.Qualification.Harness;

using CpuArchitecture = System.Runtime.InteropServices.Architecture;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     The source installation checks, on fake files in a temporary folder: the host hash, machine and version, the
///     target hashes, the sandbox copy and the before/after fingerprint that proves the source untouched.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class CheatEngineInstallationTests : IDisposable
{
	private readonly TemporaryDirectory _temporary = new("LiveQualificationInstallation");
	private readonly string _source;
	private readonly CheatEngineProfile _profile;

	public CheatEngineInstallationTests()
	{
		_source = _temporary.CreateDirectory("Cheat Engine");
		File.WriteAllText(Path.Combine(_source, "cheatengine-x86_64.exe"), "host");
		File.WriteAllText(Path.Combine(_source, CheatEngineProfile.Target64), "target 64");
		File.WriteAllText(Path.Combine(_source, CheatEngineProfile.Target32), "target 32");
		Directory.CreateDirectory(Path.Combine(_source, "autorun", "forms"));
		File.WriteAllText(Path.Combine(_source, "autorun", "celib.lua"), "-- lib");
		File.WriteAllText(Path.Combine(_source, "autorun", "forms", "form.frm"), "form");
		File.WriteAllText(Path.Combine(_source, "ce.runtimeconfig.json"), "{}");
		_profile = CheatEngineProfile.CheatEngine77 with
		{
			HostSha256 = CheatEngineInstallation.Sha256(Path.Combine(_source, "cheatengine-x86_64.exe")),
			TargetSha256 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				[CheatEngineProfile.Target64] = CheatEngineInstallation.Sha256(Path.Combine(_source, CheatEngineProfile.Target64)),
				[CheatEngineProfile.Target32] = CheatEngineInstallation.Sha256(Path.Combine(_source, CheatEngineProfile.Target32))
			}
		};
	}

	public void Dispose()
	{
		_temporary.Dispose();
	}

	[Fact]
	public void TheQualifiedProfileIsTheHostTheHarnessGatePins()
	{
		CheatEngineProfile profile = CheatEngineProfile.CheatEngine77;

		Assert.Equal(QualificationAuthorization.ExactCheatEngineSha256, profile.HostSha256);
		Assert.Equal(QualificationAuthorization.ExactCheatEngineFileVersion, profile.HostFileVersion);
		Assert.Equal("Amd64", profile.HostMachine);
		Assert.Equal("ce-7.7.0.10621-x64-managed-hostfxr", profile.Profile);
		Assert.StartsWith("2DABEFFD", profile.TargetSha256[CheatEngineProfile.Target64], StringComparison.Ordinal);
		Assert.All(profile.TargetSha256.Values.Append(profile.HostSha256), static hash => Assert.Matches("^[0-9A-F]{64}$", hash));
	}

	[Fact]
	public void AMatchingInstallationVerifies()
	{
		Assert.Empty(CheatEngineInstallation.Verify(_source, _profile, new FakeInspector("Amd64", "7.7.0.10621")));
	}

	[Fact]
	public void EveryDifferenceFromTheProfileIsReported()
	{
		File.WriteAllText(Path.Combine(_source, "cheatengine-x86_64.exe"), "patched host");
		File.Delete(Path.Combine(_source, CheatEngineProfile.Target32));

		IReadOnlyList<string> problems = CheatEngineInstallation.Verify(_source, _profile, new FakeInspector("I386", "7.6.0.9999"));

		Assert.Collection(problems,
			static problem => Assert.StartsWith("cheatengine-x86_64.exe has SHA-256", problem, StringComparison.Ordinal),
			static problem => Assert.Equal("cheatengine-x86_64.exe is built for I386, expected Amd64.", problem),
			static problem => Assert.Equal("cheatengine-x86_64.exe has file version 7.6.0.9999, expected 7.7.0.10621.", problem),
			static problem => Assert.Equal("gtutorial-i386.exe is missing.", problem));
	}

	[Fact]
	public void AMissingHostOrANonPortableExecutableIsReported()
	{
		IReadOnlyList<string> notPe = CheatEngineInstallation.Verify(_source, _profile, PortableExecutableInspector.Instance);
		File.Delete(Path.Combine(_source, "cheatengine-x86_64.exe"));
		IReadOnlyList<string> missing = CheatEngineInstallation.Verify(_source, _profile, PortableExecutableInspector.Instance);

		Assert.Contains("cheatengine-x86_64.exe is built for NotAPortableExecutable, expected Amd64.", notPe);
		Assert.Equal(["cheatengine-x86_64.exe is missing."], missing);
	}

	[Fact]
	public void ThePortableExecutableInspectorReadsTheMachineOfARealImage()
	{
		ExecutableFacts facts = PortableExecutableInspector.Instance.Describe(Environment.ProcessPath!);

		string expected = RuntimeInformation.ProcessArchitecture switch
		{
			CpuArchitecture.X64 => "Amd64",
			CpuArchitecture.Arm64 => "Arm64",
			CpuArchitecture.X86 => "I386",
			_ => RuntimeInformation.ProcessArchitecture.ToString()
		};
		Assert.Equal(expected, facts.Machine);
	}

	[Fact]
	public void TheSandboxCopyIsVerifiedAndTheSourceIsLeftUnchanged()
	{
		InstallationFingerprint before = CheatEngineInstallation.Fingerprint(_source, _profile);
		string sandbox = Path.Combine(_temporary.Path, "run", "ce");
		File.SetAttributes(Path.Combine(_source, "autorun", "celib.lua"), FileAttributes.ReadOnly);

		CheatEngineInstallation.CopyTo(_source, sandbox, _profile, new FakeInspector("Amd64", "7.7.0.10621"));

		Assert.Empty(CheatEngineInstallation.Compare(before, CheatEngineInstallation.Fingerprint(_source, _profile)));
		Assert.Empty(CheatEngineInstallation.Compare(before, CheatEngineInstallation.Fingerprint(sandbox, _profile)));
		Assert.Equal("{}", File.ReadAllText(Path.Combine(sandbox, "ce.runtimeconfig.json")));
		Assert.False(File.GetAttributes(Path.Combine(sandbox, "autorun", "celib.lua")).HasFlag(FileAttributes.ReadOnly));
		Assert.Throws<IOException>(() => CheatEngineInstallation.CopyTo(_source, sandbox, _profile, new FakeInspector("Amd64", "7.7.0.10621")));
		File.SetAttributes(Path.Combine(_source, "autorun", "celib.lua"), FileAttributes.Normal);
	}

	[Fact]
	public void TheSandboxCopyIsRefusedWhenItDoesNotMatchTheProfile()
	{
		string sandbox = Path.Combine(_temporary.Path, "run", "ce");

		InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
			CheatEngineInstallation.CopyTo(_source, sandbox, _profile, new FakeInspector("Amd64", "7.6.0.9999")));

		Assert.Contains("file version 7.6.0.9999", refused.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void TheFingerprintDetectsAChangedHostOrAutorunFolder()
	{
		InstallationFingerprint before = CheatEngineInstallation.Fingerprint(_source, _profile);
		File.WriteAllText(Path.Combine(_source, "autorun", "zz_cheatengine_client_qualification.lua"), "-- driver");
		File.WriteAllText(Path.Combine(_source, "autorun", "celib.lua"), "-- changed");
		File.WriteAllText(Path.Combine(_source, "cheatengine-x86_64.exe"), "patched host");

		IReadOnlyList<string> changes = CheatEngineInstallation.Compare(before, CheatEngineInstallation.Fingerprint(_source, _profile));

		Assert.Contains(changes, static change => change.StartsWith("host executable SHA-256", StringComparison.Ordinal));
		Assert.Contains(changes, static change => change.StartsWith("autorun lost or changed: celib.lua 6 ", StringComparison.Ordinal));
		Assert.Contains(changes, static change => change.StartsWith("autorun gained or changed: celib.lua 10 ", StringComparison.Ordinal));
		Assert.Contains(changes, static change => change.StartsWith("autorun gained or changed: zz_cheatengine_client_qualification.lua ", StringComparison.Ordinal));
		Assert.Contains("forms/form.frm 4 ", string.Join('\n', before.Autorun), StringComparison.Ordinal);
	}

	[Fact]
	public void EachRunGetsItsOwnTimestampedDirectory()
	{
		DateTimeOffset now = new(2026, 9, 24, 10, 15, 30, TimeSpan.Zero);

		SandboxLayout first = SandboxLayout.Create(Path.Combine(_temporary.Path, "runs"), now);
		SandboxLayout second = SandboxLayout.Create(Path.Combine(_temporary.Path, "runs"), now);

		Assert.Matches(ExpectedRunId(), first.RunId);
		Assert.NotEqual(first.RunDirectory, second.RunDirectory);
		Assert.True(Directory.Exists(first.PluginsDirectory));
		Assert.Equal(Path.Combine(first.RunDirectory, "ce", "autorun", SandboxLayout.DriverScriptName), first.DriverScriptPath);
		Assert.True(Directory.Exists(first.SessionDirectory("S0")));
		Assert.False(Directory.Exists(first.CheatEngineDirectory));
	}

	private sealed class FakeInspector(string machine, string? fileVersion) : IExecutableInspector
	{
		public ExecutableFacts Describe(string path)
		{
			return new ExecutableFacts(machine, fileVersion);
		}
	}

	[GeneratedRegex("^20260924T101530Z-[0-9a-f]{4}$")]
	private static partial Regex ExpectedRunId();
}
