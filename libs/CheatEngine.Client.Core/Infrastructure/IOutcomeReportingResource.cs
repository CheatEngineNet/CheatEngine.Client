namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>
///     A Client-owned resource whose <see cref="IDisposable.Dispose" /> never throws and whose release reports an outcome
///     instead: the activation drain asks it for the failure to report.
/// </summary>
internal interface IOutcomeReportingResource : IDisposable
{
	/// <summary>
	///     Releases the resource for the deactivation of its activation, retrying a retryable release once more, and
	///     returns the failure the deactivation report must carry.
	/// </summary>
	/// <returns>
	///     <see langword="null" /> when the release is complete; otherwise an exception that describes the incomplete
	///     outcome with safe fields only (kind, operation and effect).
	/// </returns>
	/// <remarks>
	///     Called by the activation drain, on Cheat Engine's main thread inside the cleanup scope when the plugin is
	///     disabled. It never throws.
	/// </remarks>
	public Exception? ReleaseForDeactivation();
}
