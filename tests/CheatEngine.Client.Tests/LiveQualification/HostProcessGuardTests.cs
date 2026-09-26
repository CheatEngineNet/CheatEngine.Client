using System.Runtime.Versioning;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     What the host guard decides without starting a process: which process names block a session, what the sandboxed
///     Cheat Engine's environment keeps, and which target modules betray an injection (Q45).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HostProcessGuardTests
{
	[Theory]
	[InlineData("cheatengine-x86_64", true)]
	[InlineData("CheatEngine-i386", true)]
	[InlineData("cheatengine-x86_64-SSE4-AVX2", true)]
	[InlineData("Cheat Engine", true)]
	[InlineData("gtutorial-x86_64", true)]
	[InlineData("gtutorial-i386", true)]
	[InlineData("CheatEngine.Client.Tests", false)]
	[InlineData("dotnet", false)]
	[InlineData("Cheat Engine Helper", false)]
	public void HostAndTargetProcessNamesAreRecognized(string name, bool blocks)
	{
		Assert.Equal(blocks, HostProcessGuard.IsHostOrTargetName(name));
	}

	[Fact]
	public void TheSandboxEnvironmentDropsHostBuildTestAndInheritedLiveProbeVariables()
	{
		Dictionary<string, string?> environment = Inherited();

		CheatEngineEnvironment.Apply(environment, new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT"] = "phrase",
			["CECLIENT_QUALIFICATION_SESSION"] = "S0"
		}, false);

		Assert.Equal(
		[
			"CECLIENT_QUALIFICATION_SESSION=S0",
			"CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT=phrase",
			"CHEATENGINE_SDK_IDENTIFY_ON_ENABLE=1",
			"Path=C:\\Windows",
			"TEMP=C:\\Temp"
		], environment.Select(static pair => $"{pair.Key}={pair.Value}").Order(StringComparer.Ordinal));
	}

	[Fact]
	public void DotNetRootIsKeptOnlyWhenTheSpikeRequiresIt()
	{
		Dictionary<string, string?> kept = Inherited();
		CheatEngineEnvironment.Apply(kept, new Dictionary<string, string>(StringComparer.Ordinal), true);

		Assert.Equal("C:\\dotnet", kept["DOTNET_ROOT"]);
		Assert.Equal("C:\\dotnet", kept["DOTNET_ROOT_X64"]);
		Assert.False(kept.ContainsKey("DOTNET_gcServer"));
	}

	[Theory]
	[InlineData("CHEATENGINE_CLIENT_LIVE_QUALIFICATION")]
	[InlineData("DOTNET_ROOT")]
	[InlineData("ce_sdk_live_probe_acknowledgement")]
	public void OnlyLiveProbeAndQualificationInputsMayBeAdded(string name)
	{
		Assert.Throws<ArgumentException>(() => CheatEngineEnvironment.Apply(Inherited(),
			new Dictionary<string, string>(StringComparer.Ordinal) { [name] = "value" }, false));
	}

	[Fact]
	public void InjectedHelperModulesAreFoundInATarget()
	{
		string[] modules = ["ntdll.dll", "SpeedHack-x86_64.dll", "kernel32.dll", "luaclient-x86_64.dll", "dbk64.sys", "allochook-x86_64.dll",
			"vehdebug-x86_64.dll", "gtutorial-x86_64.exe"];

		Assert.Equal(["allochook-x86_64.dll", "dbk64.sys", "luaclient-x86_64.dll", "speedhack-x86_64.dll", "vehdebug-x86_64.dll"],
			TargetLauncher.FindForbiddenModules(modules));
		Assert.Empty(TargetLauncher.FindForbiddenModules(["ntdll.dll", "gtutorial-x86_64.exe", "user32.dll"]));
	}

	private static Dictionary<string, string?> Inherited()
	{
		return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
		{
			["Path"] = "C:\\Windows",
			["TEMP"] = "C:\\Temp",
			["DOTNET_ROOT"] = "C:\\dotnet",
			["DOTNET_ROOT_X64"] = "C:\\dotnet",
			["DOTNET_gcServer"] = "1",
			["MSBuildExtensionsPath"] = "C:\\msbuild",
			["MSBUILDNOINPROCNODE"] = "1",
			["TESTINGPLATFORM_DIAGNOSTIC"] = "1",
			["VSTEST_HOST_DEBUG"] = "1",
			["CHEATENGINE_CLIENT_LIVE_QUALIFICATION"] = "I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET",
			["CHEATENGINE_CLIENT_PACKAGE_SOURCE"] = "C:\\packages",
			["CHEATENGINE_SDK_IDENTIFY_ON_ENABLE"] = "0",
			["CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE"] = "C:\\stale.json",
			["CECLIENT_QUALIFICATION_ENABLE_AA"] = "1"
		};
	}
}
