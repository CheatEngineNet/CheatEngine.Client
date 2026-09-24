namespace CheatEngine.Client.Scanning;

/// <summary>The memory protection a scan requires, one tri-state requirement per Cheat Engine protection flag.</summary>
/// <remarks>
///     <para>
///         Core passes the filter to Cheat Engine as its protection text, in the order executable, copy-on-write,
///         writable: for example executable, not copy-on-write and not writable memory is <c>+X-C-W</c>. The default
///         value leaves every flag unspecified and is passed as the empty text, Cheat Engine's documented "find
///         everything" value, on every route.
///     </para>
///     <para>
///         The filter is a Client value; CheatEngine.SDK's option type never appears in a public signature.
///     </para>
/// </remarks>
public readonly record struct ScanProtectionFilter
{
	/// <summary>Creates a protection filter.</summary>
	/// <param name="executable">The requirement on the executable flag (<c>X</c>).</param>
	/// <param name="copyOnWrite">The requirement on the copy-on-write flag (<c>C</c>).</param>
	/// <param name="writable">The requirement on the writable flag (<c>W</c>).</param>
	/// <exception cref="ArgumentOutOfRangeException">A requirement is not a defined value.</exception>
	public ScanProtectionFilter(ScanProtectionRequirement executable, ScanProtectionRequirement copyOnWrite,
		ScanProtectionRequirement writable)
	{
		Executable = Validate(executable, nameof(executable));
		CopyOnWrite = Validate(copyOnWrite, nameof(copyOnWrite));
		Writable = Validate(writable, nameof(writable));
	}

	/// <summary>Gets the requirement on the executable flag (<c>X</c>).</summary>
	public ScanProtectionRequirement Executable
	{
		get;
	}

	/// <summary>Gets the requirement on the copy-on-write flag (<c>C</c>).</summary>
	public ScanProtectionRequirement CopyOnWrite
	{
		get;
	}

	/// <summary>Gets the requirement on the writable flag (<c>W</c>).</summary>
	public ScanProtectionRequirement Writable
	{
		get;
	}

	/// <summary>Gets whether every flag is <see cref="ScanProtectionRequirement.Unspecified" />.</summary>
	public bool IsUnspecified => Executable == ScanProtectionRequirement.Unspecified &&
								 CopyOnWrite == ScanProtectionRequirement.Unspecified &&
								 Writable == ScanProtectionRequirement.Unspecified;

	private static ScanProtectionRequirement Validate(ScanProtectionRequirement requirement, string parameterName)
	{
		return Enum.IsDefined(requirement)
			? requirement
			: throw new ArgumentOutOfRangeException(parameterName, requirement,
				"A scan protection requirement must be a defined value.");
	}
}
