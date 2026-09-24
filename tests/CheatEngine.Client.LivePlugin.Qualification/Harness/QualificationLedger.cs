namespace LivePlugin.Qualification.Harness;

/// <summary>
///     The harness's record of its own lifecycle, kept in static state because Cheat Engine never unloads a managed
///     plugin: after a failed enable (Q06) or a faulty disable (Q43), the next enable reports what the previous one did.
///     Every entry is <c>#&lt;enable number&gt; &lt;stage&gt;[ &lt;exception type&gt;]</c>: stage names and exception
///     type names only, never a message. The record is bounded; the oldest entries are dropped and counted.
/// </summary>
internal static class QualificationLedger
{
	internal const int Capacity = 128;

	private static readonly Lock Gate = new();
	private static readonly List<string> RecordedEntries = [];
	private static int _enableAttempts;
	private static int _activations;
	private static long _lastEpoch;
	private static long _previousEpoch;
	private static int _dropped;
	private static FaultDecision _lastFault = FaultDecision.NoFault;

	/// <summary>Gets the number of enables that reached <c>Configure</c>.</summary>
	internal static int EnableAttempts
	{
		get
		{
			lock (Gate)
			{
				return _enableAttempts;
			}
		}
	}

	/// <summary>Gets the number of enables whose modules all entered.</summary>
	internal static int Activations
	{
		get
		{
			lock (Gate)
			{
				return _activations;
			}
		}
	}

	/// <summary>Gets the Client epoch of the last successful enable.</summary>
	internal static long LastEpoch
	{
		get
		{
			lock (Gate)
			{
				return _lastEpoch;
			}
		}
	}

	/// <summary>Gets the Client epoch of the successful enable before the last one, or 0.</summary>
	internal static long PreviousEpoch
	{
		get
		{
			lock (Gate)
			{
				return _previousEpoch;
			}
		}
	}

	/// <summary>Gets the number of entries dropped because the capacity was reached.</summary>
	internal static int Dropped
	{
		get
		{
			lock (Gate)
			{
				return _dropped;
			}
		}
	}

	/// <summary>Gets the fault decision of the last enable.</summary>
	internal static FaultDecision LastFault
	{
		get
		{
			lock (Gate)
			{
				return _lastFault;
			}
		}
	}

	/// <summary>Starts the record of a new enable and remembers its fault decision.</summary>
	internal static void BeginEnable(FaultDecision fault)
	{
		ArgumentNullException.ThrowIfNull(fault);
		lock (Gate)
		{
			_enableAttempts++;
			_lastFault = fault;
			AddLocked("configure fault=" + fault.Stage + " reason=" + fault.Reason);
		}
	}

	/// <summary>Records a completed enable with its Client epoch.</summary>
	internal static void RecordActivated(long epoch)
	{
		lock (Gate)
		{
			_activations++;
			_previousEpoch = _lastEpoch;
			_lastEpoch = epoch;
			AddLocked("activated");
		}
	}

	/// <summary>Records one stage, with the type name of the exception it threw when it failed.</summary>
	internal static void Record(string stage, Exception? failure = null)
	{
		lock (Gate)
		{
			AddLocked(failure is null ? stage : stage + " " + failure.GetType().Name);
		}
	}

	/// <summary>Copies the entries, oldest first.</summary>
	internal static IReadOnlyList<string> Entries()
	{
		lock (Gate)
		{
			return [.. RecordedEntries];
		}
	}

	private static void AddLocked(string entry)
	{
		if (RecordedEntries.Count == Capacity)
		{
			RecordedEntries.RemoveAt(0);
			_dropped++;
		}

		RecordedEntries.Add("#" + _enableAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + entry);
	}
}
