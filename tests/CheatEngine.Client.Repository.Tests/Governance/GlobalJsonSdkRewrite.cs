using System.Text.RegularExpressions;

namespace CheatEngine.Client.Repository.Tests.Governance;

/// <summary>
/// Executable specification of the global.json rewrite of <c>eng/ci/Select-NewestDotNetSdk.ps1</c>, the first step of
/// the scheduled SDK canary. Same .NET regular-expression engine and the same patterns, which
/// <see cref="ScheduledHealthWorkflowTests"/> finds verbatim in the script: sdk.version becomes the selected SDK, every
/// whole occurrence of the pinned version inside the sdk.errorMessage string becomes the selected SDK too, and every
/// other byte stays as committed.
/// </summary>
internal static class GlobalJsonSdkRewrite
{
	/// <summary>Repository-relative path of the script this class mirrors.</summary>
	public const string ScriptPath = "eng/ci/Select-NewestDotNetSdk.ps1";

	/// <summary>The value of "version" inside the "sdk" object (group 2).</summary>
	public const string SdkVersionPattern = """("sdk"\s*:\s*\{[^{}]*?"version"\s*:\s*")([^"]*)(")""";

	/// <summary>The JSON-escaped value of "errorMessage" inside the "sdk" object (group 2).</summary>
	public const string ErrorMessagePattern = """("sdk"\s*:\s*\{[^{}]*?"errorMessage"\s*:\s*")((?:[^"\\]|\\.)*)(")""";

	/// <summary>Look-behind that keeps a version match whole (10.0.401 never matches inside 110.0.401).</summary>
	public const string VersionStart = "(?<![0-9.])";

	/// <summary>Look-ahead that keeps a version match whole (10.0.401 never matches inside 10.0.4011).</summary>
	public const string VersionEnd = "(?![0-9])";

	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

	/// <summary>The pattern of every whole occurrence of <paramref name="version"/>.</summary>
	public static Regex WholeVersion(string version)
	{
		return new Regex(VersionStart + Regex.Escape(version) + VersionEnd, RegexOptions.None, _timeout);
	}

	/// <summary>
	/// Rewrites the text of a global.json whose sdk.version is <paramref name="pinned"/> so that it names
	/// <paramref name="selected"/>, exactly as the script does before its parse-and-compare guard.
	/// </summary>
	public static string Apply(string globalJson, string pinned, string selected)
	{
		Regex version = new(SdkVersionPattern, RegexOptions.None, _timeout);
		if (version.Count(globalJson) != 1)
		{
			throw new InvalidOperationException("global.json must contain exactly one sdk.version.");
		}

		string updated = version.Replace(globalJson, match => match.Groups[1].Value + selected + match.Groups[3].Value, 1);
		Regex message = new(ErrorMessagePattern, RegexOptions.None, _timeout);
		int messages = message.Count(updated);
		if (messages > 1)
		{
			throw new InvalidOperationException("global.json must contain at most one sdk.errorMessage.");
		}

		if (messages == 0 || selected == pinned)
		{
			return updated;
		}

		Regex pinnedVersion = WholeVersion(pinned);
		return message.Replace(
			updated,
			match => match.Groups[1].Value + pinnedVersion.Replace(match.Groups[2].Value, selected) + match.Groups[3].Value,
			1);
	}
}
