using System.Runtime.Versioning;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     Protects the Cheat Engine state of the workstation user (<c>HKCU\Software\Cheat Engine</c> and the Cheat Engine
///     files of <c>%APPDATA%</c>) around a session: <see cref="Begin" /> backs it up before Cheat Engine starts, and the
///     returned scope restores it, verified, afterwards.
/// </summary>
internal interface ICheatEngineUserStateGuard
{
	/// <summary>Backs up the user state of <paramref name="layout" />'s run and returns the scope that restores it.</summary>
	public ICheatEngineUserStateScope Begin(SandboxLayout layout);
}

/// <summary>A backed-up user state; disposing it restores and verifies the state if <see cref="Restore" /> did not.</summary>
internal interface ICheatEngineUserStateScope : IDisposable
{
	/// <summary>Whether the state was restored and verified equal to the backup.</summary>
	public bool Restored
	{
		get;
	}

	/// <summary>Restores the backup and verifies it; throws when the restored state differs.</summary>
	public void Restore();
}

/// <summary>
///     The guard of a runner that cannot protect the user state yet: it refuses every session before Cheat Engine starts,
///     so no live run can change <c>HKCU\Software\Cheat Engine</c> without a verified backup.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UnavailableUserStateGuard : ICheatEngineUserStateGuard
{
	/// <summary>The shared instance.</summary>
	internal static UnavailableUserStateGuard Instance
	{
		get;
	} = new();

	/// <inheritdoc />
	public ICheatEngineUserStateScope Begin(SandboxLayout layout)
	{
		throw new NotSupportedException(
			"The Cheat Engine user state guard (HKCU\\Software\\Cheat Engine and %APPDATA%\\Cheat Engine) is not " +
			"implemented yet: no live session may start Cheat Engine without a verified backup and restore.");
	}
}
