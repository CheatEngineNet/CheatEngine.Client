using CheatEngine.SDK.Lua.Registration;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.EndToEnd;

/// <summary>The Lua type a <see cref="FakeLuaValue" /> stands for.</summary>
public enum FakeLuaType
{
	FunctionValue,
	TableValue,
	NumberValue,
	StringValue,
	UserdataValue,
	BooleanValue
}

/// <summary>A Lua value stand-in whose identity is its object reference (the double's <c>lua_rawequal</c>).</summary>
public sealed class FakeLuaValue(string label, FakeLuaType type = FakeLuaType.FunctionValue)
{
	public string Label
	{
		get;
	} = label;

	public FakeLuaType Type
	{
		get;
	} = type;

	public override string ToString()
	{
		return Label;
	}
}

/// <summary>The registration lease the double hands out, standing for an SDK <c>LuaRegistrationLease</c>.</summary>
public sealed class FakeLease
{
	internal FakeLease(int identity, IReadOnlyList<(string Name, FakeLuaValue Installed)> entries)
	{
		Identity = identity;
		Entries = entries;
	}

	/// <summary>Gets the attachment and Lua state identity captured at publication.</summary>
	public int Identity
	{
		get;
	}

	/// <summary>Gets whether ownership was consumed by a release.</summary>
	public bool IsConsumed
	{
		get;
		internal set;
	}

	internal IReadOnlyList<(string Name, FakeLuaValue Installed)> Entries
	{
		get;
	}
}

/// <summary>What the double reports for one release, with the fields of the SDK <c>LuaRegistrationReleaseOutcome</c>.</summary>
public sealed record FakeRelease(
	LuaRegistrationReleaseKind Kind,
	int RemovedCount,
	int RestoredCount,
	int ReplacementCount,
	int RemainingCount,
	string[] FailedExports);

/// <summary>What the double reports for one registration, with the fields of the SDK <c>LuaRegistrationResult</c>.</summary>
public sealed record FakePublication(
	LuaRegistrationResultKind Kind,
	FakeLease? Lease,
	string? FailedExport,
	string? FailedStatus,
	FakeRelease Rollback);

/// <summary>
///     Managed double of the CheatEngine.SDK 2.0.0 Lua admission and registration set (<c>LuaRuntime</c>,
///     <c>LuaRegistrationSet</c> and <c>LuaRegistrationLease</c>) over a Lua global table, called one SDK call at a time by
///     the replacement adapter of <see cref="ModuleHarness" />. It follows the SDK source step by step: a
///     <c>RejectExisting</c> preflight that reads every global, a publication that installs one function per export, a
///     compensation that releases what a failed publication installed, and an ownership-aware release that compares each
///     global with the installed value by identity and writes only while it is still the installed one. Failures are
///     injected per global name; every read and write is logged.
/// </summary>
/// <remarks>
///     Log entries are <c>read:&lt;name&gt;</c>, <c>write:&lt;name&gt;</c> (a publication), <c>clear:&lt;name&gt;</c> (a
///     release that wrote <c>nil</c>), and the failed variants <c>read-failed</c>, <c>write-failed</c> and
///     <c>clear-failed</c>. A stale or already-consumed lease makes no Lua operation and logs nothing. Third-party
///     assignments made by a test are not logged. Admitted operations are counted apart from the log. This is C1
///     evidence of the Client code around the adapter, never of the SDK itself.
/// </remarks>
public sealed class FakeLuaGlobals
{
	[ThreadStatic]
	private static FakeLuaGlobals? t_current;

	private readonly List<LuaRegistrationCollisionPolicy> _bindingsRegistrations = [];
	private readonly Dictionary<string, FakeLuaValue> _globals = new(StringComparer.Ordinal);
	private readonly List<string> _log = [];

	public FakeLuaGlobals(string moduleName, IReadOnlyList<string> exports)
	{
		ModuleName = moduleName;
		Exports = exports;
	}

	/// <summary>Gets the double the replacement adapter uses on this thread.</summary>
	public static FakeLuaGlobals Current =>
		t_current ?? throw new InvalidOperationException("No FakeLuaGlobals is active on this thread.");

	public string ModuleName
	{
		get;
	}

	/// <summary>Gets the globals the SDK-generated registration of the module publishes, in registration order.</summary>
	public IReadOnlyList<string> Exports
	{
		get;
	}

	/// <summary>Gets the attachment and Lua state identity; a lease captured under another one is stale.</summary>
	public int Identity
	{
		get;
		private set;
	} = 1;

	/// <summary>Gets or sets the admission CheatEngine.SDK grants to the next Lua operation.</summary>
	public LuaAdmissionStatus Admission
	{
		get;
		set;
	} = LuaAdmissionStatus.Admitted;

	public IReadOnlyList<string> Log => _log;

	/// <summary>Gets how many Lua operations CheatEngine.SDK admitted.</summary>
	public int AdmittedOperationCount
	{
		get;
		private set;
	}

	/// <summary>Gets how many admitted Lua operations have not been ended.</summary>
	public int OpenOperationCount
	{
		get;
		private set;
	}

	/// <summary>Gets the collision policy of every call the module made to its bindings' registration.</summary>
	public IReadOnlyList<LuaRegistrationCollisionPolicy> BindingsRegistrations => _bindingsRegistrations;

	/// <summary>Gets how many leases were consumed without a Lua state (the parameterless SDK release).</summary>
	public int StaleReleaseCount
	{
		get;
		private set;
	}

	/// <summary>Gets the globals whose protected read fails with a Lua error.</summary>
	public HashSet<string> FailReads
	{
		get;
	} = new(StringComparer.Ordinal);

	/// <summary>Gets the globals whose publication (the protected assignment of the module's function) fails.</summary>
	public HashSet<string> FailPublications
	{
		get;
	} = new(StringComparer.Ordinal);

	/// <summary>Gets the globals whose release (the protected assignment of <c>nil</c>) fails, every time.</summary>
	public HashSet<string> FailClears
	{
		get;
	} = new(StringComparer.Ordinal);

	/// <summary>Gets the globals whose next release fails once; a later release of the same global succeeds.</summary>
	public HashSet<string> FailClearsOnce
	{
		get;
	} = new(StringComparer.Ordinal);

	/// <summary>
	///     Gets or sets what the next release with a state reports instead of releasing: it consumes the lease, writes
	///     nothing, and returns this value once (a result outside the documented shape, or a kind a later SDK adds).
	/// </summary>
	public FakeRelease? NextRelease
	{
		get;
		set;
	}

	/// <summary>Gets the current value of a global, or <see langword="null" /> for <c>nil</c>.</summary>
	public FakeLuaValue? this[string name] => _globals.GetValueOrDefault(name);

	/// <summary>Makes this double the one the replacement adapter uses on this thread until the scope is disposed.</summary>
	public IDisposable Activate()
	{
		FakeLuaGlobals? previous = t_current;
		t_current = this;
		return new Scope(previous);
	}

	/// <summary>Assigns a global as a third party (a script, a table, another plugin); not logged.</summary>
	public void AssignByThirdParty(string name, FakeLuaValue? value)
	{
		if (value is null)
		{
			_globals.Remove(name);
		}
		else
		{
			_globals[name] = value;
		}
	}

	/// <summary>Replaces the Lua state: every global is gone and every lease becomes stale.</summary>
	public void ReplaceLuaState()
	{
		_globals.Clear();
		Identity++;
	}

	/// <summary>
	///     Disables and re-enables the plugin on the same Cheat Engine Lua state: the globals stay, but every lease belongs
	///     to the earlier attachment.
	/// </summary>
	public void Reattach()
	{
		Identity++;
	}

	public void ClearLog()
	{
		_log.Clear();
	}

	public int CountOf(string prefix)
	{
		return _log.Count(entry => entry.StartsWith(prefix, StringComparison.Ordinal));
	}

	/// <summary>Records a call of the harness module's bindings registration (the stand-in for the SDK-generated one).</summary>
	public void RecordBindingsRegistration(LuaRegistrationCollisionPolicy collisionPolicy)
	{
		RequireOpenOperation();
		_bindingsRegistrations.Add(collisionPolicy);
	}

	// ----- SDK calls: made only by the replacement adapter, one adapter member each. -----

	/// <summary><c>LuaRuntime.TryAcquireOperationWithOutcome</c>: grants <see cref="Admission" />.</summary>
	public LuaAdmissionStatus Admit()
	{
		if (Admission == LuaAdmissionStatus.Admitted)
		{
			AdmittedOperationCount++;
			OpenOperationCount++;
		}

		return Admission;
	}

	/// <summary><c>LuaRuntimeOperation.Dispose</c> of an admitted operation.</summary>
	public void EndOperation()
	{
		RequireOpenOperation();
		OpenOperationCount--;
	}

	/// <summary>The result of <c>TryRegisterLuaFunctions(state, RejectExisting)</c> in an admitted operation.</summary>
	public FakePublication Publish()
	{
		RequireOpenOperation();
		for (int index = 0; index < Exports.Count; index++)
		{
			string name = Exports[index];
			if (!TryRead(name, out FakeLuaValue? existing))
			{
				return new FakePublication(LuaRegistrationResultKind.PreflightFailed, null, name, "LUA_ERRRUN",
					NotAttempted());
			}

			if (existing is not null)
			{
				return new FakePublication(LuaRegistrationResultKind.Collision, null, name, "LUA_OK", NotAttempted());
			}
		}

		List<(string Name, FakeLuaValue Installed)> installed = [];
		foreach (string name in Exports)
		{
			FakeLuaValue function = new(ModuleName + ":" + name);
			// The SDK creates the installed reference before the protected assignment, so a failed write is compensated too.
			installed.Add((name, function));
			if (FailPublications.Contains(name))
			{
				_log.Add("write-failed:" + name);
				(FakeRelease rollback, List<(string Name, FakeLuaValue Installed)> residual) = ReleaseEntries(installed, true);
				FakeLease? residualLease = residual.Count == 0 ? null : new FakeLease(Identity, residual);
				return new FakePublication(LuaRegistrationResultKind.PublicationFailed, residualLease, name, "LUA_ERRRUN",
					rollback);
			}

			_log.Add("write:" + name);
			_globals[name] = function;
		}

		return new FakePublication(LuaRegistrationResultKind.Succeeded, new FakeLease(Identity, installed), null, null,
			NotAttempted());
	}

	/// <summary><c>LuaRegistrationLease.ReleaseWithOutcome(state)</c> in an admitted operation.</summary>
	public FakeRelease Release(FakeLease lease)
	{
		ArgumentNullException.ThrowIfNull(lease);
		RequireOpenOperation();
		if (lease.IsConsumed)
		{
			return new FakeRelease(LuaRegistrationReleaseKind.AlreadyReleased, 0, 0, 0, 0, []);
		}

		lease.IsConsumed = true;
		if (NextRelease is { } scripted)
		{
			NextRelease = null;
			return scripted;
		}

		if (lease.Identity != Identity)
		{
			return new FakeRelease(LuaRegistrationReleaseKind.Stale, 0, 0, 0, lease.Entries.Count, []);
		}

		return ReleaseEntries(lease.Entries, false).Outcome;
	}

	/// <summary><c>LuaRegistrationLease.ReleaseWithOutcome()</c> without an admitted state: the SDK reports it stale.</summary>
	public FakeRelease ReleaseStale(FakeLease lease)
	{
		ArgumentNullException.ThrowIfNull(lease);
		if (lease.IsConsumed)
		{
			return new FakeRelease(LuaRegistrationReleaseKind.AlreadyReleased, 0, 0, 0, 0, []);
		}

		lease.IsConsumed = true;
		StaleReleaseCount++;
		return new FakeRelease(LuaRegistrationReleaseKind.Stale, 0, 0, 0, lease.Entries.Count, []);
	}

	private static FakeRelease NotAttempted()
	{
		return new FakeRelease(LuaRegistrationReleaseKind.NotAttempted, 0, 0, 0, 0, []);
	}

	private void RequireOpenOperation()
	{
		if (OpenOperationCount == 0)
		{
			throw new InvalidOperationException("CheatEngine.SDK Lua work requires an admitted operation.");
		}
	}

	private (FakeRelease Outcome, List<(string Name, FakeLuaValue Installed)> Residual) ReleaseEntries(
		IReadOnlyList<(string Name, FakeLuaValue Installed)> entries, bool retainFailures)
	{
		List<string> failed = [];
		List<(string Name, FakeLuaValue Installed)> residual = [];
		int removed = 0;
		int replaced = 0;
		foreach ((string name, FakeLuaValue installed) in entries)
		{
			if (!TryRead(name, out FakeLuaValue? current))
			{
				failed.Add(name);
				residual.Add((name, installed));
				continue;
			}

			if (!ReferenceEquals(current, installed))
			{
				// A replacement, a wrapper, or nil: never the installed value, so nothing is written.
				replaced++;
				continue;
			}

			if (FailClears.Contains(name) || FailClearsOnce.Remove(name))
			{
				_log.Add("clear-failed:" + name);
				failed.Add(name);
				residual.Add((name, installed));
				continue;
			}

			_log.Add("clear:" + name);
			_globals.Remove(name);
			removed++;
		}

		LuaRegistrationReleaseKind kind = failed.Count == 0
			? LuaRegistrationReleaseKind.Released
			: LuaRegistrationReleaseKind.PartiallyReleased;
		return (new FakeRelease(kind, removed, 0, replaced, failed.Count, [.. failed]),
			retainFailures ? residual : []);
	}

	private bool TryRead(string name, out FakeLuaValue? value)
	{
		if (FailReads.Contains(name))
		{
			_log.Add("read-failed:" + name);
			value = null;
			return false;
		}

		_log.Add("read:" + name);
		value = _globals.GetValueOrDefault(name);
		return true;
	}

	private sealed class Scope(FakeLuaGlobals? previous) : IDisposable
	{
		public void Dispose()
		{
			t_current = previous;
		}
	}
}
