using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Assembly;

/// <summary>The copied verdict of one Auto Assembler syntax check (<see cref="IAutoAssemblerClient.TryCheck" />).</summary>
/// <remarks>
///     <see cref="HostMessages" /> is Cheat Engine's bounded, unparsed error text for a rejected section, when Cheat
///     Engine returned one. It is user data (it can contain script source and file paths), so <see cref="ToString" />
///     omits it.
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.AutoAssemblerPatches, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public readonly record struct AutoAssemblerCheckResult
{
	/// <summary>Creates a check verdict.</summary>
	/// <param name="isAccepted">Whether Cheat Engine accepted the checked section.</param>
	/// <param name="hostMessages">Cheat Engine's bounded error text, or <see langword="null" />.</param>
	/// <param name="hostMessagesTruncated">Whether <paramref name="hostMessages" /> was cut at the Client's bound.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="hostMessagesTruncated" /> is <see langword="true" /> without <paramref name="hostMessages" />.
	/// </exception>
	public AutoAssemblerCheckResult(bool isAccepted, string? hostMessages, bool hostMessagesTruncated)
	{
		if (hostMessagesTruncated && hostMessages is null)
		{
			throw new ArgumentException("Truncated host messages require the copied text.",
				nameof(hostMessagesTruncated));
		}

		IsAccepted = isAccepted;
		HostMessages = hostMessages;
		HostMessagesTruncated = hostMessagesTruncated;
	}

	/// <summary>Gets whether Cheat Engine accepted the checked section.</summary>
	/// <remarks>An accepted section does not prove that an activation will succeed.</remarks>
	public bool IsAccepted
	{
		get;
	}

	/// <summary>Gets Cheat Engine's bounded, unparsed error text, or <see langword="null" /> when it returned none.</summary>
	public string? HostMessages
	{
		get;
	}

	/// <summary>Gets whether <see cref="HostMessages" /> was cut at the Client's host-text bound.</summary>
	public bool HostMessagesTruncated
	{
		get;
	}

	/// <summary>Returns the verdict only, never the host messages.</summary>
	/// <returns><c>Accepted</c> or <c>Rejected</c>.</returns>
	public override string ToString()
	{
		return IsAccepted ? "Accepted" : "Rejected";
	}
}
