using CheatEngine.Client.Lua;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.EndToEnd;

/// <summary>
///     C1 execution of the emitted ownership-aware registration and release (F12, Q16): the generated
///     <c>RegisterCore</c>/<c>UnregisterCore</c> run unchanged against a managed Lua-globals double. This is not a Lua
///     fixture (C2) and not a host observation (C3/C4).
/// </summary>
[Trait("Qualification", "Q16")]
public sealed class LuaModuleOwnershipEndToEndTests
{
	private const string Status = "status";
	private const string Ping = "ping";
	private const string Marker = "marker";

	private static ModuleHarness Harness => ModuleHarness.Shared;

	[Fact]
	public void UnregisterRemovesEveryExportTheModuleStillOwns()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();

		Harness.Unregister(module, globals);

		LuaModuleReleaseOutcome outcome = AssertOutcome(module, LuaModuleReleaseKind.Released,
			LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed);
		Assert.Equal(ModuleHarness.ModuleName, outcome.ModuleName);
		Assert.All(ModuleHarness.Exports, export => Assert.Null(globals[export]));
		Assert.All(globals.Pins, static pin => Assert.True(pin.Released));
		Assert.Equal(
			["read:status", "clear:status", "release:status#1", "read:ping", "clear:ping", "release:ping#2",
				"read:marker", "clear:marker", "release:marker#3"],
			globals.Log);
	}

	[Fact]
	public void UnregisterLeavesAThirdPartyReplacementUntouched()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		FakeLuaValue thirdParty = new("third-party:ping");
		globals.AssignByThirdParty(Ping, thirdParty);

		Harness.Unregister(module, globals);

		AssertOutcome(module, LuaModuleReleaseKind.Released,
			LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Replaced, LuaExportReleaseStatus.Removed);
		Assert.Same(thirdParty, globals[Ping]);
		Assert.DoesNotContain(globals.Log, static entry => entry.StartsWith("clear:ping", StringComparison.Ordinal));
		Assert.Contains("release:ping#2", globals.Log);
		Assert.Null(globals[Status]);
		Assert.Null(globals[Marker]);
	}

	[Fact]
	public void UnregisterTreatsAWrappedFunctionAsAReplacement()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		// A wrapper that would call the module's function is still a different value: ownership is identity, not behaviour.
		FakeLuaValue wrapper = new("function(...) return plugin_ping(...) end");
		globals.AssignByThirdParty(Ping, wrapper);

		Harness.Unregister(module, globals);

		Assert.Equal(LuaExportReleaseStatus.Replaced, module.LastReleaseOutcome!.Exports[1].Status);
		Assert.Same(wrapper, globals[Ping]);
		Assert.Equal(0, globals.CountOf("clear:ping"));
	}

	[Fact]
	public void UnregisterRemovesAValueAThirdPartyRestoredToTheModulesOwn()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		FakeLuaValue original = globals[Ping]!;
		globals.AssignByThirdParty(Ping, new FakeLuaValue("third-party:ping"));
		globals.AssignByThirdParty(Ping, original);

		Harness.Unregister(module, globals);

		Assert.Equal(LuaExportReleaseStatus.Removed, module.LastReleaseOutcome!.Exports[1].Status);
		Assert.Null(globals[Ping]);
	}

	[Fact]
	public void UnregisterDoesNotWriteAnExportThatIsAlreadyAbsent()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		globals.AssignByThirdParty(Marker, null);

		Harness.Unregister(module, globals);

		AssertOutcome(module, LuaModuleReleaseKind.Released,
			LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Absent);
		Assert.Equal(0, globals.CountOf("clear:marker"));
		Assert.Equal(1, module.LastReleaseOutcome!.AbsentCount);
		Assert.Contains("release:marker#3", globals.Log);
	}

	[Fact]
	public void UnregisterAfterALuaStateReplacementWritesNothingAndReportsStale()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		globals.ReplaceLuaState();
		FakeLuaValue newStateValue = new("new-state:status");
		globals.AssignByThirdParty(Status, newStateValue);

		Harness.Unregister(module, globals);

		AssertOutcome(module, LuaModuleReleaseKind.Stale,
			LuaExportReleaseStatus.NotAttempted, LuaExportReleaseStatus.NotAttempted, LuaExportReleaseStatus.NotAttempted);
		Assert.True(module.LastReleaseOutcome!.IsComplete);
		Assert.Equal(["release-noop:status#1", "release-noop:ping#2", "release-noop:marker#3"], globals.Log);
		Assert.Same(newStateValue, globals[Status]);
	}

	[Fact]
	public void UnregisterAttemptsEveryExportAndAggregatesIndependentFailures()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		globals.FailReads.Add(Status);
		globals.FailClears.Add(Ping);

		AggregateException failure = Assert.Throws<AggregateException>(() => Harness.Unregister(module, globals));

		Assert.Equal(2, failure.InnerExceptions.Count);
		Assert.All(failure.InnerExceptions, static inner => Assert.IsType<LuaException>(inner));
		Assert.Contains("status", failure.InnerExceptions[0].Message, StringComparison.Ordinal);
		Assert.Contains("ping", failure.InnerExceptions[1].Message, StringComparison.Ordinal);
		Assert.StartsWith("Client Lua module 'plugin' could not release every export.", failure.Message,
			StringComparison.Ordinal);
		LuaModuleReleaseOutcome outcome = AssertOutcome(module, LuaModuleReleaseKind.PartiallyReleased,
			LuaExportReleaseStatus.Failed, LuaExportReleaseStatus.Failed, LuaExportReleaseStatus.Removed);
		Assert.Equal(2, outcome.FailedCount);
		Assert.False(outcome.IsComplete);
		Assert.Null(globals[Marker]);
		Assert.NotNull(globals[Status]);
		Assert.NotNull(globals[Ping]);
		Assert.All(globals.Pins, static pin => Assert.True(pin.Released));
	}

	[Fact]
	public void UnregisterConsumesOwnershipBeforeTouchingLuaSoARetryIsANoOp()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		globals.FailReads.Add(Status);
		globals.FailClears.Add(Ping);
		Assert.Throws<AggregateException>(() => Harness.Unregister(module, globals));
		LuaModuleReleaseOutcome? first = module.LastReleaseOutcome;
		globals.ClearLog();

		Harness.Unregister(module, globals);

		Assert.Empty(globals.Log);
		Assert.Same(first, module.LastReleaseOutcome);
	}

	[Fact]
	public void UnregisterConsumesOwnershipEvenWhenAnSdkExceptionEscapesTheRelease()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		globals.ThrowOnReads.Add(Ping);

		InvalidOperationException escaped =
			Assert.Throws<InvalidOperationException>(() => Harness.Unregister(module, globals));

		// F15: a programming or lifecycle exception is not a Lua failure; it propagates as is, the remaining exports are
		// not attempted, their pins stay unreleased, and no outcome is published.
		Assert.Equal("fake lifecycle failure: ping", escaped.Message);
		Assert.Equal(["read:status", "clear:status", "release:status#1", "read-threw:ping"], globals.Log);
		Assert.Equal([true, false, false], globals.Pins.Select(static pin => pin.Released));
		Assert.Null(module.LastReleaseOutcome);
		globals.ThrowOnReads.Clear();
		globals.ClearLog();

		// The registration was consumed before the first Lua call, so the lease retry is still a no-op.
		Harness.Unregister(module, globals);

		Assert.Empty(globals.Log);
		Assert.Null(module.LastReleaseOutcome);
	}

	[Fact]
	public void UnregisterWithoutAnOwnedRegistrationMakesNoPortCallAndPublishesNothing()
	{
		IOwnershipAwareLuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = new();

		Harness.Unregister(module, globals);

		Assert.Empty(globals.Log);
		Assert.Null(module.LastReleaseOutcome);
	}

	[Fact]
	public void PublicUnregisterWithoutAnOwnedRegistrationReturnsWithoutAcquiringTheLuaRuntime()
	{
		IOwnershipAwareLuaModule module = Harness.CreateModule();

		// No SDK runtime is attached in this process: Register must reach LuaRuntime.AcquireOperation and fail, while
		// Unregister of a module that owns nothing returns before it (the lease retry path stays harmless).
		Assert.Throws<InvalidOperationException>(module.Register);
		module.Unregister();

		Assert.Null(module.LastReleaseOutcome);
	}

	[Fact]
	public void UnregisterReportsAnUnresolvableOwnershipReferenceAsFailedWithoutWriting()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		globals.UnresolvablePins = true;

		AggregateException failure = Assert.Throws<AggregateException>(() => Harness.Unregister(module, globals));

		Assert.Equal(3, failure.InnerExceptions.Count);
		Assert.All(failure.InnerExceptions, static inner =>
		{
			Assert.IsType<InvalidOperationException>(inner);
			Assert.Contains("could not resolve the ownership reference", inner.Message, StringComparison.Ordinal);
		});
		AssertOutcome(module, LuaModuleReleaseKind.PartiallyReleased,
			LuaExportReleaseStatus.Failed, LuaExportReleaseStatus.Failed, LuaExportReleaseStatus.Failed);
		Assert.Equal(0, globals.CountOf("clear:"));
		Assert.All(ModuleHarness.Exports, export => Assert.NotNull(globals[export]));
	}

	[Fact]
	public void UnregisterReportsAPinReleaseFailureWithoutChangingTheExportStatus()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		globals.FailReleases.Add(Ping);

		LuaException failure = Assert.Throws<LuaException>(() => Harness.Unregister(module, globals));

		Assert.Contains("release failure: ping", failure.Message, StringComparison.Ordinal);
		AssertOutcome(module, LuaModuleReleaseKind.Released,
			LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed);
		Assert.All(ModuleHarness.Exports, export => Assert.Null(globals[export]));
		Assert.All(globals.Pins, static pin => Assert.True(pin.Released));
	}

	[Fact]
	public void RegisterRefusesAnOccupiedExportBeforeAnyWrite()
	{
		IOwnershipAwareLuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = new();
		FakeLuaValue existing = new("third-party:marker");
		globals.AssignByThirdParty(Marker, existing);

		InvalidOperationException failure =
			Assert.Throws<InvalidOperationException>(() => Harness.Register(module, globals));

		Assert.Equal("Lua global 'marker' is already defined and cannot be replaced by Client module 'plugin'.",
			failure.Message);
		Assert.Equal(["read:status", "read:ping", "read:marker"], globals.Log);
		Assert.Empty(globals.Pins);
		Assert.Same(existing, globals[Marker]);
		Assert.Null(globals[Status]);
	}

	[Fact]
	public void RegisterThrowsAPreflightReadFailureBeforeAnyWrite()
	{
		IOwnershipAwareLuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = new();
		globals.FailReads.Add(Ping);

		LuaException failure = Assert.Throws<LuaException>(() => Harness.Register(module, globals));

		Assert.Contains("read failure: ping", failure.Message, StringComparison.Ordinal);
		Assert.Equal(["read:status", "read-failed:ping"], globals.Log);
		Assert.Empty(globals.Pins);
	}

	[Fact]
	public void RegisterRollsBackOnlyTheExportsItPublishedWhenPublicationFails()
	{
		IOwnershipAwareLuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = new()
		{
			PublishFailsAfter = 1
		};

		LuaException failure = Assert.Throws<LuaException>(() => Harness.Register(module, globals));

		Assert.Equal("fake publication failure: ping", failure.Message);
		Assert.Null(failure.InnerException);
		Assert.Equal(
			["read:status", "read:ping", "read:marker", "publish:status", "publish-failed:ping", "read:status",
				"clear:status", "read:ping", "read:marker"],
			globals.Log);
		Assert.All(ModuleHarness.Exports, export => Assert.Null(globals[export]));
		Assert.Empty(globals.Pins);

		Harness.Unregister(module, globals);
		Assert.Null(module.LastReleaseOutcome);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void RegisterRollsBackWhenOwnershipCannotBeCaptured(bool publishedValueReadsBackNil)
	{
		IOwnershipAwareLuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = new();
		if (publishedValueReadsBackNil)
		{
			globals.SwallowPublications.Add(Ping);
		}
		else
		{
			globals.FailPins.Add(Ping);
		}

		Exception failure = Assert.ThrowsAny<Exception>(() => Harness.Register(module, globals));

		if (publishedValueReadsBackNil)
		{
			Assert.IsType<InvalidOperationException>(failure);
			Assert.Equal("Lua global 'ping' could not be confirmed after Client module 'plugin' registered it.",
				failure.Message);
		}
		else
		{
			Assert.IsType<LuaException>(failure);
			Assert.Equal("fake pin failure: ping", failure.Message);
		}

		Assert.Null(failure.InnerException);
		// Export 1 was captured: ownership-aware release. Exports 2 and 3 were not: cleared only when non-nil.
		Assert.Equal(1, globals.CountOf("clear:status"));
		Assert.Contains("release:status#1", globals.Log);
		Assert.True(globals.Log.ToList().IndexOf("clear:status") < globals.Log.ToList().IndexOf("release:status#1"));
		Assert.Equal(publishedValueReadsBackNil ? 0 : 1, globals.CountOf("clear:ping"));
		Assert.Equal(1, globals.CountOf("clear:marker"));
		Assert.All(ModuleHarness.Exports, export => Assert.Null(globals[export]));
		Assert.All(globals.Pins, static pin => Assert.True(pin.Released));
		Assert.Single(globals.Pins);
	}

	[Fact]
	public void RollbackFailureIsReportedWithoutReplacingThePrimaryRegistrationFailure()
	{
		IOwnershipAwareLuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = new()
		{
			PublishFailsAfter = 2
		};
		globals.FailClears.Add(Status);

		LuaException failure = Assert.Throws<LuaException>(() => Harness.Register(module, globals));

		Assert.StartsWith("fake publication failure: marker", failure.Message, StringComparison.Ordinal);
		Assert.Contains(" The rollback also failed: fake clear failure: status", failure.Message,
			StringComparison.Ordinal);
		AggregateException inner = Assert.IsType<AggregateException>(failure.InnerException);
		Assert.Equal(2, inner.InnerExceptions.Count);
		Assert.Equal("fake publication failure: marker", inner.InnerExceptions[0].Message);
		Assert.Equal("fake clear failure: status", inner.InnerExceptions[1].Message);
		Assert.Null(globals[Ping]);
		Assert.NotNull(globals[Status]);
	}

	[Fact]
	public void ARegistrationFailureWithoutRollbackFailureKeepsItsLuaStatus()
	{
		IOwnershipAwareLuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = new()
		{
			PublishFailsAfter = 1,
			PublishFailureStatus = LuaStatus.MemoryError
		};

		LuaException failure = Assert.Throws<LuaException>(() => Harness.Register(module, globals));

		Assert.Equal(LuaStatus.MemoryError, failure.Status);
		Assert.Null(failure.InnerException);
	}

	[Fact]
	public void RollbackFailureKeepsThePrimaryLuaStatusOnTheFirstInnerException()
	{
		IOwnershipAwareLuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = new()
		{
			PublishFailsAfter = 2,
			PublishFailureStatus = LuaStatus.MemoryError
		};
		globals.FailClears.Add(Status);

		LuaException failure = Assert.Throws<LuaException>(() => Harness.Register(module, globals));

		// SDK 1.0.0 has no LuaException constructor that takes both a status and an inner exception: the combined
		// exception keeps the primary type and message, and the primary status stays on the primary itself.
		Assert.Equal(LuaStatus.Ok, failure.Status);
		AggregateException inner = Assert.IsType<AggregateException>(failure.InnerException);
		LuaException primary = Assert.IsType<LuaException>(inner.InnerExceptions[0]);
		Assert.Equal(LuaStatus.MemoryError, primary.Status);
		Assert.StartsWith(primary.Message, failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void RollbackFailureKeepsAnInvalidOperationPrimaryType()
	{
		IOwnershipAwareLuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = new();
		globals.SwallowPublications.Add(Marker);
		globals.FailReleases.Add(Status);

		InvalidOperationException failure =
			Assert.Throws<InvalidOperationException>(() => Harness.Register(module, globals));

		Assert.StartsWith("Lua global 'marker' could not be confirmed after Client module 'plugin' registered it.",
			failure.Message, StringComparison.Ordinal);
		AggregateException inner = Assert.IsType<AggregateException>(failure.InnerException);
		Assert.IsType<InvalidOperationException>(inner.InnerExceptions[0]);
		Assert.IsType<LuaException>(inner.InnerExceptions[1]);
		Assert.All(ModuleHarness.Exports, export => Assert.Null(globals[export]));
	}

	[Fact]
	public void RegisterWhileStillRegisteredIsRefusedWithoutLuaMutation()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		globals.ClearLog();

		InvalidOperationException failure =
			Assert.Throws<InvalidOperationException>(() => Harness.Register(module, globals));

		Assert.Equal("Client Lua module 'plugin' is already registered in the current Lua state.", failure.Message);
		Assert.Empty(globals.Log);
		Harness.Unregister(module, globals);
		AssertOutcome(module, LuaModuleReleaseKind.Released,
			LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed);
	}

	[Fact]
	public void RegisterAfterAStaleRegistrationReleasesTheStaleTokensFirst()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		globals.ReplaceLuaState();
		globals.ClearLog();

		Harness.Register(module, globals);

		Assert.Equal(
			["release-noop:status#1", "release-noop:ping#2", "release-noop:marker#3", "read:status", "read:ping",
				"read:marker", "publish:status", "publish:ping", "publish:marker", "read:status", "pin:status#4",
				"read:ping", "pin:ping#5", "read:marker", "pin:marker#6"],
			globals.Log);
		Harness.Unregister(module, globals);
		AssertOutcome(module, LuaModuleReleaseKind.Released,
			LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed);
	}

	[Fact]
	public void ModuleCanRegisterAgainAfterAPartiallyFailedRelease()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		globals.FailReleases.Add(Status);
		Assert.Throws<LuaException>(() => Harness.Unregister(module, globals));
		globals.FailReleases.Clear();

		Harness.Register(module, globals);
		Harness.Unregister(module, globals);

		AssertOutcome(module, LuaModuleReleaseKind.Released,
			LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed);
		Assert.All(ModuleHarness.Exports, export => Assert.Null(globals[export]));
	}

	[Fact]
	public void RegisterAfterAFailedClearRefusesTheLeftoverGlobalUntilItIsRemoved()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		globals.FailClears.Add(Ping);
		Assert.Throws<LuaException>(() => Harness.Unregister(module, globals));
		globals.FailClears.Clear();

		// The failed export still holds the module's old value: the module never overwrites a defined global.
		InvalidOperationException refused =
			Assert.Throws<InvalidOperationException>(() => Harness.Register(module, globals));
		Assert.Contains("'ping' is already defined", refused.Message, StringComparison.Ordinal);

		globals.AssignByThirdParty(Ping, null);
		Harness.Register(module, globals);
		Harness.Unregister(module, globals);
		AssertOutcome(module, LuaModuleReleaseKind.Released,
			LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed, LuaExportReleaseStatus.Removed);
	}

	[Theory]
	[InlineData(FakeLuaType.TableValue)]
	[InlineData(FakeLuaType.NumberValue)]
	[InlineData(FakeLuaType.StringValue)]
	[InlineData(FakeLuaType.UserdataValue)]
	[InlineData(FakeLuaType.BooleanValue)]
	[InlineData(FakeLuaType.FunctionValue)]
	public void ThirdPartyValuesOfAnyLuaTypeAreReportedAsReplaced(FakeLuaType type)
	{
		// The double compares by object identity: this proves that the algorithm keeps any non-owned value, not how
		// lua_rawequal compares strings, numbers or light C functions (that needs a Lua state, C2).
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		FakeLuaValue thirdParty = new("third-party:" + type, type);
		globals.AssignByThirdParty(Status, thirdParty);

		Harness.Unregister(module, globals);

		Assert.Equal(LuaExportReleaseStatus.Replaced, module.LastReleaseOutcome!.Exports[0].Status);
		Assert.Same(thirdParty, globals[Status]);
		Assert.Equal(0, globals.CountOf("clear:status"));
	}

	[Fact]
	public void LastReleaseOutcomeIsNullBeforeAnyReleaseAndReplacedByEachRelease()
	{
		(IOwnershipAwareLuaModule module, FakeLuaGlobals globals) = Registered();
		Assert.Null(module.LastReleaseOutcome);

		globals.AssignByThirdParty(Ping, new FakeLuaValue("third-party:ping"));
		Harness.Unregister(module, globals);
		LuaModuleReleaseOutcome first = module.LastReleaseOutcome!;
		globals.AssignByThirdParty(Ping, null);
		Harness.Register(module, globals);
		Harness.Unregister(module, globals);

		Assert.NotSame(first, module.LastReleaseOutcome);
		Assert.Equal(1, first.ReplacedCount);
		Assert.Equal(0, module.LastReleaseOutcome!.ReplacedCount);
	}

	private static (IOwnershipAwareLuaModule Module, FakeLuaGlobals Globals) Registered()
	{
		IOwnershipAwareLuaModule module = Harness.CreateModule();
		FakeLuaGlobals globals = new();
		Harness.Register(module, globals);
		Assert.Equal(
			["read:status", "read:ping", "read:marker", "publish:status", "publish:ping", "publish:marker",
				"read:status", "pin:status#1", "read:ping", "pin:ping#2", "read:marker", "pin:marker#3"],
			globals.Log);
		Assert.All(ModuleHarness.Exports, export => Assert.NotNull(globals[export]));
		Assert.Null(module.LastReleaseOutcome);
		globals.ClearLog();
		return (module, globals);
	}

	private static LuaModuleReleaseOutcome AssertOutcome(IOwnershipAwareLuaModule module, LuaModuleReleaseKind kind,
		params LuaExportReleaseStatus[] statuses)
	{
		LuaModuleReleaseOutcome outcome = Assert.IsType<LuaModuleReleaseOutcome>(module.LastReleaseOutcome);
		Assert.Equal(kind, outcome.Kind);
		Assert.Equal(ModuleHarness.Exports, outcome.Exports.Select(static export => export.Name));
		Assert.Equal(statuses, outcome.Exports.Select(static export => export.Status));
		return outcome;
	}
}
