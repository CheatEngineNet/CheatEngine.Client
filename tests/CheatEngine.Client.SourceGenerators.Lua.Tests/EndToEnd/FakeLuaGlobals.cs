using CheatEngine.SDK.Lua.Calls;

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

/// <summary>What <see cref="FakeLuaGlobals.Observe" /> saw; mapped one to one onto the generated ownership enum.</summary>
public enum FakeObservation
{
	ReadFailed,
	Absent,
	Owned,
	Replaced,
	OwnershipUnresolvable
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

/// <summary>A pin on a published value, standing for an SDK <c>LuaRef</c>: current only in the generation it was made in.</summary>
public sealed class FakeLuaPin
{
	internal FakeLuaPin(int id, string export, FakeLuaValue value, int generation)
	{
		Id = id;
		Export = export;
		Value = value;
		Generation = generation;
	}

	public int Id
	{
		get;
	}

	public string Export
	{
		get;
	}

	public FakeLuaValue Value
	{
		get;
	}

	public int Generation
	{
		get;
	}

	public bool Released
	{
		get;
		internal set;
	}
}

/// <summary>
///     Managed double of the Lua global table seen through the generated module's port. It records every read, write, pin,
///     and release so tests assert behaviour, never generated text; failures are injected per global name.
/// </summary>
/// <remarks>
///     Log entries are <c>read:&lt;name&gt;</c>, <c>publish:&lt;name&gt;</c>, <c>clear:&lt;name&gt;</c>,
///     <c>pin:&lt;name&gt;#&lt;id&gt;</c>, <c>release:&lt;name&gt;#&lt;id&gt;</c> (a current pin),
///     <c>release-noop:&lt;name&gt;#&lt;id&gt;</c> (a stale or already released pin), and the failed variants
///     <c>read-failed</c>, <c>publish-failed</c>, <c>clear-failed</c>, <c>pin-failed</c>, <c>release-failed</c>. Third-party
///     assignments made by a test are not logged.
/// </remarks>
public sealed class FakeLuaGlobals
{
	private readonly Dictionary<string, FakeLuaValue> _globals = new(StringComparer.Ordinal);
	private readonly List<string> _log = [];
	private readonly List<FakeLuaPin> _pins = [];
	private int _nextPin = 1;

	/// <summary>Gets the Lua state generation; a pin is current only in the generation that created it.</summary>
	public int Generation
	{
		get;
		private set;
	} = 1;

	public IReadOnlyList<string> Log => _log;

	public IReadOnlyList<FakeLuaPin> Pins => _pins;

	/// <summary>Gets the globals whose read fails with a Lua error.</summary>
	public HashSet<string> FailReads
	{
		get;
	} = new(StringComparer.Ordinal);

	/// <summary>Gets the globals whose clear (write of <c>nil</c>) fails with a Lua error.</summary>
	public HashSet<string> FailClears
	{
		get;
	} = new(StringComparer.Ordinal);

	/// <summary>Gets the globals whose pin creation fails with a Lua error after a successful read.</summary>
	public HashSet<string> FailPins
	{
		get;
	} = new(StringComparer.Ordinal);

	/// <summary>Gets the globals whose pin release fails with a Lua error (the pin is still marked released).</summary>
	public HashSet<string> FailReleases
	{
		get;
	} = new(StringComparer.Ordinal);

	/// <summary>Gets the globals whose publication is silently dropped (a <c>__newindex</c> that stores nothing).</summary>
	public HashSet<string> SwallowPublications
	{
		get;
	} = new(StringComparer.Ordinal);

	/// <summary>Gets or sets the number of exports publication writes before it fails; <see langword="null" /> never fails.</summary>
	public int? PublishFailsAfter
	{
		get;
		set;
	}

	/// <summary>Gets or sets whether a current pin cannot be pushed back (the SDK <c>TryPushRef</c> returning false).</summary>
	public bool UnresolvablePins
	{
		get;
		set;
	}

	/// <summary>Gets the current value of a global, or <see langword="null" /> for <c>nil</c>.</summary>
	public FakeLuaValue? this[string name] => _globals.GetValueOrDefault(name);

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

	/// <summary>Replaces the Lua state: every global is gone and every existing pin becomes stale.</summary>
	public void ReplaceLuaState()
	{
		_globals.Clear();
		Generation++;
	}

	public void ClearLog()
	{
		_log.Clear();
	}

	public int CountOf(string prefix)
	{
		return _log.Count(entry => entry.StartsWith(prefix, StringComparison.Ordinal));
	}

	// ----- Port operations: called only through the generated module's private port. -----

	public Exception? ProbeVacant(string name, out bool vacant)
	{
		if (FailReads.Contains(name))
		{
			_log.Add("read-failed:" + name);
			vacant = false;
			return new LuaException("fake read failure: " + name);
		}

		_log.Add("read:" + name);
		vacant = !_globals.ContainsKey(name);
		return null;
	}

	public Exception? Publish(IReadOnlyList<string> names, string moduleName)
	{
		for (int index = 0; index < names.Count; index++)
		{
			if (PublishFailsAfter == index)
			{
				_log.Add("publish-failed:" + names[index]);
				return new LuaException("fake publication failure: " + names[index]);
			}

			_log.Add("publish:" + names[index]);
			if (!SwallowPublications.Contains(names[index]))
			{
				_globals[names[index]] = new FakeLuaValue(moduleName + ":" + names[index]);
			}
		}

		return null;
	}

	public Exception? Capture(string name, out FakeLuaPin? pin)
	{
		pin = null;
		if (FailReads.Contains(name))
		{
			_log.Add("read-failed:" + name);
			return new LuaException("fake read failure: " + name);
		}

		_log.Add("read:" + name);
		if (!_globals.TryGetValue(name, out FakeLuaValue? value))
		{
			return null;
		}

		if (FailPins.Contains(name))
		{
			_log.Add("pin-failed:" + name);
			return new LuaException("fake pin failure: " + name);
		}

		pin = new FakeLuaPin(_nextPin++, name, value, Generation);
		_pins.Add(pin);
		_log.Add("pin:" + name + "#" + pin.Id);
		return null;
	}

	public bool IsCurrent(FakeLuaPin pin)
	{
		return !pin.Released && pin.Generation == Generation;
	}

	public FakeObservation Observe(string name, FakeLuaPin pin, out Exception? failure)
	{
		failure = null;
		if (FailReads.Contains(name))
		{
			_log.Add("read-failed:" + name);
			failure = new LuaException("fake read failure: " + name);
			return FakeObservation.ReadFailed;
		}

		_log.Add("read:" + name);
		if (!_globals.TryGetValue(name, out FakeLuaValue? value))
		{
			return FakeObservation.Absent;
		}

		if (UnresolvablePins || !IsCurrent(pin))
		{
			return FakeObservation.OwnershipUnresolvable;
		}

		return ReferenceEquals(value, pin.Value) ? FakeObservation.Owned : FakeObservation.Replaced;
	}

	public Exception? Clear(string name)
	{
		if (FailClears.Contains(name))
		{
			_log.Add("clear-failed:" + name);
			return new LuaException("fake clear failure: " + name);
		}

		_log.Add("clear:" + name);
		_globals.Remove(name);
		return null;
	}

	public Exception? Release(FakeLuaPin pin)
	{
		bool current = IsCurrent(pin);
		pin.Released = true;
		if (!current)
		{
			_log.Add("release-noop:" + pin.Export + "#" + pin.Id);
			return null;
		}

		if (FailReleases.Contains(pin.Export))
		{
			_log.Add("release-failed:" + pin.Export + "#" + pin.Id);
			return new LuaException("fake release failure: " + pin.Export);
		}

		_log.Add("release:" + pin.Export + "#" + pin.Id);
		return null;
	}
}
