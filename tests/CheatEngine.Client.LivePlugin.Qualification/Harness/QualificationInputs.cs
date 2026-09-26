namespace LivePlugin.Qualification.Harness;

/// <summary>
///     The session inputs the live qualification runner hands the harness through <c>CECLIENT_QUALIFICATION_*</c>
///     process variables. They are read once per enable and honored only when the qualification gate authorized the run,
///     so a stray variable on a user's machine never composes an opt-in or writes a file.
/// </summary>
/// <param name="EnableAutoAssembler">
///     Whether the activation composes <c>EnableAutoAssemblerPatches()</c> (Q35): only for the exact value <c>1</c>.
/// </param>
/// <param name="TableRoot">The one allowed table root of the activation (Q34), an absolute path, or <see langword="null" />.</param>
/// <param name="LifecycleFile">
///     The lifecycle receipt sink (Q43): an absolute file the harness appends its lifecycle record and captured log
///     templates to, so the runner reads what happened after the last Lua call (the disable at <c>closeCE</c>).
/// </param>
internal sealed record QualificationInputs(bool EnableAutoAssembler, string? TableRoot, string? LifecycleFile)
{
	internal const string EnableAutoAssemblerVariable = "CECLIENT_QUALIFICATION_ENABLE_AA";
	internal const string TableRootVariable = "CECLIENT_QUALIFICATION_TABLE_ROOT";
	internal const string LifecycleFileVariable = "CECLIENT_QUALIFICATION_LIFECYCLE_FILE";

	/// <summary>Gets the inputs of an unauthorized run: no opt-in, no table root, no sink.</summary>
	internal static QualificationInputs None
	{
		get;
	} = new(false, null, null);

	/// <summary>Reads the inputs under the gate decision of this enable.</summary>
	internal static QualificationInputs Read(AuthorizationDecision authorization, IQualificationEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(authorization);
		ArgumentNullException.ThrowIfNull(environment);
		if (!authorization.IsAllowed)
		{
			return None;
		}

		return new QualificationInputs(
			string.Equals(environment.GetVariable(EnableAutoAssemblerVariable), "1", StringComparison.Ordinal),
			AbsolutePath(environment.GetVariable(TableRootVariable)),
			AbsolutePath(environment.GetVariable(LifecycleFileVariable)));
	}

	private static string? AbsolutePath(string? value)
	{
		return !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value) ? Path.GetFullPath(value) : null;
	}
}
