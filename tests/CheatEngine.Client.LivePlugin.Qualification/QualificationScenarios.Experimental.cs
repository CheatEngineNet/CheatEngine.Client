using System.Collections.Immutable;
using System.Globalization;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using LivePlugin.Qualification.Harness;

// The scenarios of the experimental value scans (CECLIENT5001), allocations (CECLIENT5002), instructions (CECLIENT5003)
// and Auto Assembler patches (CECLIENT5004); the source is compiled standalone against the packed packages.
#pragma warning disable CECLIENT5001, CECLIENT5002, CECLIENT5003, CECLIENT5004

namespace LivePlugin.Qualification;

/// <summary>
///     The value scan (Q25, Q26), allocation (Q30.a, Q30.b), instruction (Q32) and Auto Assembler patch (Q35) scenarios.
///     Every step that changes the target or Cheat Engine state runs behind <see cref="RunMutating" /> and writes only
///     inside the declared scratch region; the leases they create are kept in <see cref="QualificationSession" /> so a
///     later step, after a target change or a re-enable, reports what the Client made of them.
/// </summary>
internal static partial class QualificationScenarios
{
	/// <summary>The scratch slots of the value scans: an int32 marker, then a float and a double for Q25's decimals.</summary>
	private const int ValueScanOffset = 1536;

	private const int SingleOffset = 1552;
	private const int DoubleOffset = 1560;

	/// <summary>The scratch window of the instruction round trip (Q32).</summary>
	private const int InstructionOffset = 2048;

	private const int InstructionWindow = 64;

	/// <summary>How many value-scan matches the harness reads back; the scan range is the 4096-byte scratch region.</summary>
	private const int ValueScanPageSize = 64;

	/// <summary>The value Q25 scans for with texts of several decimals (<see cref="ValueScanDecimals" />).</summary>
	private const float SingleProbe = 3.14159f;

	private const double DoubleProbe = 3.14159;

	/// <summary>The symbol the benign Auto Assembler patch allocates and registers, and unregisters and frees on disable.</summary>
	internal const string PatchSymbol = NamePrefix + "aa_memory";

	private const string PatchName = NamePrefix + "aa_patch";

	/// <summary>The benign Q35 patch: an allocation and a symbol, both undone by its <c>[DISABLE]</c> section.</summary>
	internal const string BenignPatchSource = "// " + CapturingLoggerProvider.ScriptMarker + "\n" +
											  "[ENABLE]\n" +
											  "alloc(" + PatchSymbol + ",64)\n" +
											  "registersymbol(" + PatchSymbol + ")\n" +
											  PatchSymbol + ":\n" +
											  "dd 0\n" +
											  "[DISABLE]\n" +
											  "unregistersymbol(" + PatchSymbol + ")\n" +
											  "dealloc(" + PatchSymbol + ")\n";

	/// <summary>The failing Q35 variant: it writes to a label nothing defines, so Cheat Engine refuses it.</summary>
	internal const string FailingPatchSource = "// " + CapturingLoggerProvider.ScriptMarker + "\n" +
											   "[ENABLE]\n" +
											   NamePrefix + "aa_undefined_label:\n" +
											   "db 90\n" +
											   "[DISABLE]\n";

	/// <summary>
	///     One value-scan step (Q25, Q26) on the scratch region: <c>first</c> writes the int32 marker
	///     <paramref name="value" /> into its slot and runs a first scan for it, <c>next</c> writes the new marker and runs
	///     a next scan, <c>reset</c> resets the session, <c>decimals</c> checks Cheat Engine's tolerance for the decimal
	///     text of <c>FromSingle</c> and <c>FromDouble</c>, <c>state</c> reads the session, <c>release</c> releases it
	///     (also after a target change) and <c>create-unidentified</c> checks that no session is created on a file opened
	///     as a process.
	/// </summary>
	internal static string ValueScan(string action, string symbolName, long value)
	{
		const string Function = "value_scan";
		return action switch
		{
			"state" => Guarded(Function, static observation => DescribeValueScan(observation.String("action", "state"))),
			"release" => RunMutating(Function, static (observation, active, processId) =>
				ReleaseValueScan(observation.String("action", "release"), active, processId), MutationScope.OwnedResource),
			"create-unidentified" => RunMutating(Function, static (observation, active, _) =>
					CreateUnidentifiedValueScan(observation.String("action", "create-unidentified"), active),
				MutationScope.FileAsProcessTarget),
			"first" or "next" or "reset" or "decimals" => RunMutating(Function, (observation, active, processId) =>
				RunValueScan(observation.String("action", action), active, processId, action, symbolName, value)),
			_ => Guarded(Function, static observation =>
				observation.Boolean("ok", false).String("refusal", "UnknownAction").Complete())
		};
	}

	/// <summary>
	///     One allocation step (Q30.a, Q30.b) under a scenario name: <c>allocate</c> makes <paramref name="size" /> bytes
	///     on the authorized target and checks the region through the inspection API, <c>state</c> reads the lease,
	///     <c>release</c> releases it (also after a target change, when the Client must refuse to free anything) and
	///     <c>allocate-unidentified</c> checks that nothing is allocated on a file opened as a process.
	/// </summary>
	internal static string Allocation(string action, string name, long size)
	{
		const string Function = "allocation";
		if (!name.StartsWith(NamePrefix, StringComparison.Ordinal))
		{
			return Guarded(Function, static observation =>
				observation.Boolean("ok", false).String("refusal", "NameNotHarness").Complete());
		}

		return action switch
		{
			"allocate" => RunMutating(Function, (observation, active, _) =>
				Allocate(observation.String("action", action).String("name", name), active, name, size)),
			"state" => Guarded(Function, observation =>
				DescribeAllocation(observation.String("action", action).String("name", name), name)),
			"release" => RunMutating(Function, (observation, active, processId) =>
					ReleaseAllocation(observation.String("action", action).String("name", name), active, processId, name),
				MutationScope.OwnedResource),
			"allocate-unidentified" => RunMutating(Function, (observation, active, _) =>
					AllocateUnidentified(observation.String("action", action), active, size),
				MutationScope.FileAsProcessTarget),
			_ => Guarded(Function, static observation =>
				observation.Boolean("ok", false).String("refusal", "UnknownAction").Complete())
		};
	}

	/// <summary>
	///     The instruction profile of the selected target (Q32): the bytes Cheat Engine assembles for a fixed instruction
	///     list at the scratch window, both jump encodings, and a round trip that writes <c>mov eax,1; ret</c> into the
	///     window, disassembles it, measures it, finds the previous instruction, and restores the window.
	/// </summary>
	internal static string Instructions(string symbolName)
	{
		return RunMutating("instructions", (observation, active, processId) =>
		{
			if (!TryResolveDeclaredScratch(observation, active, symbolName, processId, out Address scratch))
			{
				return observation.Complete();
			}

			Address window = scratch + InstructionOffset;
			if (!GuardWrite(observation, processId, window, InstructionWindow) ||
				!TryReadOriginal(observation, active, window, InstructionWindow, out ImmutableArray<byte> original))
			{
				return observation.Complete();
			}

			int bitness = active.Client.Processes.TryRefresh(out ProcessSnapshot process, out _) ? process.Bitness.Bytes : 0;
			string framePointer = bitness == 4 ? "ebp" : "rbp";
			string jumpTarget = HexOperand(window.Value + 0x20, bitness);
			IAssemblyClient assembly = active.Client.Assembly;
			observation.Number("bitnessBytes", bitness).BeginArray("assembled");
			foreach ((string id, string text, InstructionEncodingPreference preference) in
					 (ReadOnlySpan<(string, string, InstructionEncodingPreference)>)
					 [
						 ("nop", "nop", InstructionEncodingPreference.None),
						 ("ret", "ret", InstructionEncodingPreference.None),
						 ("int3", "int3", InstructionEncodingPreference.None),
						 ("mov-eax-1", "mov eax,1", InstructionEncodingPreference.None),
						 ("push-frame-pointer", "push " + framePointer, InstructionEncodingPreference.None),
						 ("jmp-short", "jmp " + jumpTarget, InstructionEncodingPreference.Short),
						 ("jmp-long", "jmp " + jumpTarget, InstructionEncodingPreference.Long)
					 ])
			{
				bool assembled = assembly.TryAssemble(new AssemblyInstructionRequest(window, text, preference),
					out ImmutableArray<byte> bytes, out CheatEngineFailure failure);
				observation.BeginItem().String("id", id).Boolean("assembled", assembled)
					.String("bytes", assembled ? Convert.ToHexString(bytes.AsSpan()) : null);
				if (!assembled)
				{
					observation.Failure("failure", failure);
				}

				observation.EndObject();
			}

			observation.EndArray();
			return InstructionRoundTrip(observation, active, window, original);
		});
	}

	/// <summary>
	///     One Auto Assembler step (Q35, Q44), available only when the activation composed
	///     <c>EnableAutoAssemblerPatches()</c> (an authorized run with <c>CECLIENT_QUALIFICATION_ENABLE_AA=1</c>):
	///     <c>check</c> checks the benign script, <c>apply</c> applies the <c>benign</c> or the <c>failing</c> variant,
	///     <c>state</c> reads the patch lease and <c>release</c> disables it (also after a target change).
	/// </summary>
	internal static string AutoAssemblerPatch(string action, string variant)
	{
		const string Function = "aa_patch";
		return action switch
		{
			"state" => Guarded(Function, static observation =>
				QualificationSession.TryGetActive(out QualificationSession.ActiveClient? active)
					? DescribePatch(observation.String("action", "state"), active)
					: Inactive(observation)),
			"release" => RunMutating(Function, static (observation, active, _) =>
				ReleasePatch(observation.String("action", "release"), active), MutationScope.OwnedResource),
			"check" or "apply" => RunMutating(Function, (observation, active, processId) =>
				RunPatch(observation.String("action", action).String("variant", variant), active, processId, action,
					variant)),
			_ => Guarded(Function, static observation =>
				observation.Boolean("ok", false).String("refusal", "UnknownAction").Complete())
		};
	}

	private static string RunValueScan(QualificationObservation observation, QualificationSession.ActiveClient active,
		int processId, string action, string symbolName, long value)
	{
		if (!TryResolveDeclaredScratch(observation, active, symbolName, processId, out Address scratch))
		{
			return observation.Complete();
		}

		if (action == "decimals")
		{
			return ValueScanDecimals(observation, active, processId, scratch);
		}

		Address slot = scratch + ValueScanOffset;
		QualificationSession.ValueScanState? state = QualificationSession.ValueScan;
		if (action == "reset")
		{
			if (state is null)
			{
				return observation.Boolean("ok", false).String("refusal", "NoSession").Complete();
			}

			bool reset = state.Session.TryReset(out CheatEngineFailure resetFailure);
			if (!reset)
			{
				observation.Failure("failure", resetFailure);
			}

			return DescribeValueScan(observation.Boolean("reset", reset));
		}

		int marker = unchecked((int) value);
		if (!GuardWrite(observation, processId, slot, sizeof(int)))
		{
			return observation.Complete();
		}

		if (action == "first")
		{
			if (state is not null && !state.Session.IsReleased)
			{
				return observation.Boolean("ok", false).String("refusal", "SessionAlreadyCreated").Complete();
			}

			if (!TryReadOriginal(observation, active, slot, sizeof(int), out ImmutableArray<byte> original))
			{
				return observation.Complete();
			}

			if (!active.Client.ValueScans.TryCreateSession(out IValueScanSession? session,
					out CheatEngineFailure createFailure))
			{
				return observation.Boolean("ok", false).Failure("createFailure", createFailure).Complete();
			}

			state = new QualificationSession.ValueScanState(session, slot.Value, [.. original]);
			QualificationSession.ValueScan = state;
		}
		else if (state is null)
		{
			return observation.Boolean("ok", false).String("refusal", "NoSession").Complete();
		}

		QualificationSession.Logs.DeclareSensitive(marker.ToString(CultureInfo.InvariantCulture));
		if (!active.Client.Memory.TryWritePrimitive(slot, marker, out CheatEngineFailure writeFailure))
		{
			return observation.Boolean("ok", false).Failure("writeFailure", writeFailure).Complete();
		}

		ValueScanValue scanned = ValueScanValue.FromInt32(marker);
		CheatEngineFailure scanFailure;
		bool scannedOk = action == "first"
			? state.Session.TryFirstScan(ValueScanFirstRequest.Exact(scanned).WithRange(scratch, scratch + ScratchLength),
				out scanFailure)
			: state.Session.TryNextScan(ValueScanNextRequest.Exact(scanned), out scanFailure);
		observation.Boolean("scanned", scannedOk);
		if (!scannedOk)
		{
			observation.Failure("scanFailure", scanFailure);
		}

		WriteScanPage(observation, state.Session, slot);
		return DescribeValueScan(observation);
	}

	// Q25: Cheat Engine compares a float scan with the decimals of its text (rtRounded). Two rules fit its Lua
	// documentation, and both find the probe 3.14159 by its own 5 decimals, by 3.14 and by 3, and not by 3.2 or 3.15:
	// those cases carry an expectation. The 3-decimal text 3.142 is the discriminating case, recorded without one:
	// ordinary rounding to 3 decimals (3.14159 rounds to 3.142) finds the probe, while the range the documentation
	// states ("3" matches 3.0 to 3.4999, the text up to half a unit above it) stops below it. The evaluator names the
	// rule the host applied, and ValueScanValue's remarks follow the recorded run.
	private static string ValueScanDecimals(QualificationObservation observation,
		QualificationSession.ActiveClient active, int processId, Address scratch)
	{
		Address singleSlot = scratch + SingleOffset;
		Address doubleSlot = scratch + DoubleOffset;
		if (!GuardWrite(observation, processId, singleSlot, 16) ||
			!TryReadOriginal(observation, active, singleSlot, 16, out ImmutableArray<byte> original))
		{
			return observation.Complete();
		}

		bool written = active.Client.Memory.TryWritePrimitive(singleSlot, SingleProbe, out CheatEngineFailure failure) &
					   active.Client.Memory.TryWritePrimitive(doubleSlot, DoubleProbe, out _);
		if (!written)
		{
			Restore(observation, active, singleSlot, original);
			return observation.Boolean("ok", false).Failure("writeFailure", failure).Complete();
		}

		if (!active.Client.ValueScans.TryCreateSession(out IValueScanSession? session,
				out CheatEngineFailure createFailure))
		{
			Restore(observation, active, singleSlot, original);
			return observation.Boolean("ok", false).Failure("createFailure", createFailure).Complete();
		}

		observation.BeginArray("cases");
		// A null expectation marks the discriminating case (3.142), whose result names the rounding rule.
		foreach ((ValueScanValue scanned, Address slot, int decimals, bool? expectedFound) in
				 (ReadOnlySpan<(ValueScanValue, Address, int, bool?)>)
				 [
					 (ValueScanValue.FromSingle(SingleProbe, 5), singleSlot, 5, true),
					 (ValueScanValue.FromSingle(SingleProbe, 3), singleSlot, 3, null),
					 (ValueScanValue.FromSingle(SingleProbe, 2), singleSlot, 2, true),
					 (ValueScanValue.FromSingle(SingleProbe, 0), singleSlot, 0, true),
					 (ValueScanValue.FromSingle(3.2f, 1), singleSlot, 1, false),
					 (ValueScanValue.FromSingle(3.15f, 2), singleSlot, 2, false),
					 (ValueScanValue.FromDouble(DoubleProbe, 5), doubleSlot, 5, true),
					 (ValueScanValue.FromDouble(DoubleProbe, 3), doubleSlot, 3, null),
					 (ValueScanValue.FromDouble(DoubleProbe, 2), doubleSlot, 2, true),
					 (ValueScanValue.FromDouble(DoubleProbe, 0), doubleSlot, 0, true),
					 (ValueScanValue.FromDouble(3.2, 1), doubleSlot, 1, false),
					 (ValueScanValue.FromDouble(3.15, 2), doubleSlot, 2, false)
				 ])
		{
			bool scannedOk = session.TryFirstScan(
				ValueScanFirstRequest.Exact(scanned).WithRange(scratch, scratch + ScratchLength), out CheatEngineFailure scanFailure);
			observation.BeginItem()
				.String("type", scanned.Type.ToString())
				.String("text", scanned.Text)
				.Number("decimals", decimals)
				.Boolean("discriminating", expectedFound is null)
				.OptionalBoolean("expectedFound", expectedFound)
				.Boolean("scanned", scannedOk);
			if (scannedOk && session.TryRead(new ValueScanReadRequest(0, ValueScanPageSize), out ValueScanPage page, out _))
			{
				observation.Boolean("found", page.Matches.Any(match => match.Address == slot));
			}
			else if (!scannedOk)
			{
				observation.Failure("scanFailure", scanFailure);
			}

			observation.EndObject();
			session.TryReset(out _);
		}

		LeaseReleaseOutcome released = session.Release();
		Restore(observation.EndArray(), active, singleSlot, original);
		return WriteReleaseOutcome(observation, "sessionRelease", released).Boolean("ok", true).Complete();
	}

	private static void WriteScanPage(QualificationObservation observation, IValueScanSession session, Address slot)
	{
		if (!session.TryRead(new ValueScanReadRequest(0, ValueScanPageSize), out ValueScanPage page,
				out CheatEngineFailure failure))
		{
			observation.Failure("readFailure", failure);
			return;
		}

		observation.BeginObject("results")
			.Number("resultCount", (long) Math.Min(page.ResultCount, long.MaxValue))
			.Number("read", page.Matches.Length)
			.Boolean("containsMarker", page.Matches.Any(match => match.Address == slot))
			.EndObject();
	}

	private static string DescribeValueScan(QualificationObservation observation)
	{
		if (QualificationSession.ValueScan is not { } state)
		{
			return observation.Boolean("ok", false).String("refusal", "NoSession").Complete();
		}

		IValueScanSession session = state.Session;
		observation.BeginObject("session")
			.String("state", session.State.ToString())
			.String("invalidation", session.Invalidation.ToString())
			.Boolean("released", session.IsReleased)
			.EndObject();
		WriteLastRelease(observation, session.LastReleaseOutcome);
		return observation.Boolean("ok", true).Complete();
	}

	private static string ReleaseValueScan(QualificationObservation observation,
		QualificationSession.ActiveClient active, int processId)
	{
		if (QualificationSession.ValueScan is not { } state)
		{
			return observation.Boolean("ok", false).String("refusal", "NoSession").Complete();
		}

		LeaseReleaseOutcome released = state.Session.Release();
		WriteReleaseOutcome(observation, "release", released);

		// The marker slot is restored only in the authorized target the session was created in.
		Address slot = new(state.Slot);
		if (QualificationWriteGuard.Evaluate(QualificationSession.Authorization, processId,
				QualificationSession.Declaration, state.Slot, state.OriginalBytes.Length) == WriteRefusal.None)
		{
			Restore(observation, active, slot, [.. state.OriginalBytes]);
		}

		return DescribeValueScan(observation);
	}

	private static string CreateUnidentifiedValueScan(QualificationObservation observation,
		QualificationSession.ActiveClient active)
	{
		bool created = active.Client.ValueScans.TryCreateSession(out IValueScanSession? session,
			out CheatEngineFailure failure);
		observation.Boolean("created", created);
		if (session is not null)
		{
			// Unexpected: a session on a file opened as a process. It is released at once and reported.
			WriteReleaseOutcome(observation, "unexpectedSessionRelease", session.Release());
		}
		else
		{
			observation.Failure("failure", failure);
		}

		return observation.Boolean("ok", true).Complete();
	}

	private static string Allocate(QualificationObservation observation, QualificationSession.ActiveClient active,
		string name, long size)
	{
		if (QualificationSession.TryGetAllocation(name, out ITargetMemoryLease? previous) && !previous.IsReleased)
		{
			return observation.Boolean("ok", false).String("refusal", "AllocationAlreadyHeld").Complete();
		}

		if (size is <= 0 or > 65_536)
		{
			return observation.Boolean("ok", false).String("refusal", "SizeOutOfRange").Complete();
		}

		if (!active.Client.Allocations.TryAllocate(new AllocationRequest(size), out ITargetMemoryLease? lease,
				out CheatEngineFailure failure))
		{
			return observation.Boolean("ok", false).Failure("failure", failure).Complete();
		}

		QualificationSession.KeepAllocation(name, lease);
		QualificationSession.Logs.DeclareSensitive(QualificationObservation.Hex(lease.Address.Value));
		observation.Boolean("allocated", true).Number("size", lease.Size).String("protection", lease.Protection.ToString())
			.Number("selectionEpoch", lease.SelectionEpoch);
		WriteRegion(observation, active, lease.Address, "region");
		return observation.Boolean("ok", true).Complete();
	}

	private static string DescribeAllocation(QualificationObservation observation, string name)
	{
		if (!QualificationSession.TryGetAllocation(name, out ITargetMemoryLease? lease))
		{
			return observation.Boolean("ok", false).String("refusal", "NoAllocation").Complete();
		}

		observation.BeginObject("lease")
			.Boolean("released", lease.IsReleased)
			.Boolean("requiresManualRecovery", lease.RequiresManualRecovery)
			.Number("selectionEpoch", lease.SelectionEpoch)
			.Number("size", lease.Size)
			.EndObject();
		WriteLastRelease(observation, lease.LastReleaseOutcome);
		if (QualificationSession.TryGetActive(out QualificationSession.ActiveClient? active) &&
			active.Client.Processes.TryRefresh(out ProcessSnapshot process, out _))
		{
			observation.Number("currentSelectionEpoch", process.SelectionEpoch)
				.Number("currentProcessId", process.Id.Value);
		}

		return observation.Boolean("ok", true).Complete();
	}

	private static string ReleaseAllocation(QualificationObservation observation,
		QualificationSession.ActiveClient active, int processId, string name)
	{
		if (!QualificationSession.TryGetAllocation(name, out ITargetMemoryLease? lease))
		{
			return observation.Boolean("ok", false).String("refusal", "NoAllocation").Complete();
		}

		bool releasedBefore = lease.IsReleased;
		WriteReleaseOutcome(observation.Boolean("releasedBefore", releasedBefore), "release", lease.Release());
		observation.Boolean("requiresManualRecovery", lease.RequiresManualRecovery)
			.Boolean("onAuthorizedTarget", QualificationSession.Authorization.Allows(processId));

		// Only in the authorized target does the region tell whether the memory was freed.
		if (QualificationSession.Authorization.Allows(processId))
		{
			WriteRegion(observation, active, lease.Address, "regionAfterRelease");
		}

		return observation.Boolean("ok", true).Complete();
	}

	private static string AllocateUnidentified(QualificationObservation observation,
		QualificationSession.ActiveClient active, long size)
	{
		bool allocated = active.Client.Allocations.TryAllocate(new AllocationRequest(Math.Clamp(size, 1, 4096)),
			out ITargetMemoryLease? lease, out CheatEngineFailure failure);
		observation.Boolean("allocated", allocated);
		if (lease is not null)
		{
			// Unexpected: an allocation on a file opened as a process. It is released at once and reported.
			WriteReleaseOutcome(observation, "unexpectedAllocationRelease", lease.Release());
		}
		else
		{
			observation.Failure("failure", failure);
		}

		return observation.Boolean("ok", true).Complete();
	}

	private static void WriteRegion(QualificationObservation observation, QualificationSession.ActiveClient active,
		Address address, string name)
	{
		if (!active.Client.Inspection.TryGetMemoryRegion(address, out MemoryRegionInfo region,
				out CheatEngineFailure failure))
		{
			observation.Failure(name + "Failure", failure);
			return;
		}

		observation.BeginObject(name)
			.String("state", region.State.ToString())
			.String("type", region.Type.ToString())
			.String("protection", region.Protection.ToString())
			.Boolean("startsAtAllocation", region.BaseAddress == address)
			.EndObject();
	}

	private static string InstructionRoundTrip(QualificationObservation observation,
		QualificationSession.ActiveClient active, Address window, ImmutableArray<byte> original)
	{
		IAssemblyClient assembly = active.Client.Assembly;
		bool assembledMove = assembly.TryAssemble(new AssemblyInstructionRequest(window, "mov eax,1"),
			out ImmutableArray<byte> move, out CheatEngineFailure failure);
		bool assembledReturn = assembly.TryAssemble(new AssemblyInstructionRequest(window + move.Length, "ret"),
			out ImmutableArray<byte> ret, out _);
		if (!assembledMove || !assembledReturn)
		{
			return observation.Boolean("ok", false).Failure("roundTripAssembleFailure", failure).Complete();
		}

		byte[] code = [.. move, .. ret];
		if (!active.Client.Memory.TryWriteBytes(new MemoryBytesWriteRequest(window, code), out CheatEngineFailure writeFailure))
		{
			return observation.Boolean("ok", false).Failure("roundTripWriteFailure", writeFailure).Complete();
		}

		bool disassembled = assembly.TryDisassemble(window, out AssemblyInstructionSnapshot instruction,
			out CheatEngineFailure disassembleFailure);
		bool measured = assembly.TryGetInstructionLength(window, out int length, out _);
		bool previousFound = assembly.TryGetPreviousInstructionAddress(window + move.Length, out Address previous,
			out _);
		Restore(observation, active, window, original);
		observation.BeginObject("roundTrip")
			.String("bytes", Convert.ToHexString(code))
			.Boolean("disassembled", disassembled)
			.String("opcode", disassembled ? instruction.Opcode : null)
			.String("extra", disassembled ? instruction.Extra : null)
			.Number("snapshotLength", disassembled ? instruction.Length : 0)
			.Boolean("bytesEqual", disassembled && instruction.Bytes.AsSpan().SequenceEqual(move.AsSpan()))
			.Boolean("measured", measured)
			.Number("length", measured ? length : 0)
			.Boolean("previousIsWindow", previousFound && previous == window);
		if (!disassembled)
		{
			observation.Failure("disassembleFailure", disassembleFailure);
		}

		return observation.EndObject().Boolean("ok", true).Complete();
	}

	private static string HexOperand(ulong address, int bitness)
	{
		// A leading digit keeps Cheat Engine's assembler from reading the operand as a symbol name.
		return address.ToString(bitness == 4 ? "X8" : "X16", CultureInfo.InvariantCulture);
	}

	private static string RunPatch(QualificationObservation observation, QualificationSession.ActiveClient active,
		int processId, string action, string variant)
	{
		if (active.Services.GetService(typeof(IAutoAssemblerClient)) is not IAutoAssemblerClient patches)
		{
			return observation.Boolean("ok", false).String("refusal", "AutoAssemblerNotEnabled")
				.Boolean("optInRequested", QualificationSession.Inputs.EnableAutoAssembler).Complete();
		}

		string? source = variant switch
		{
			"benign" => BenignPatchSource,
			"failing" => FailingPatchSource,
			_ => null
		};
		if (source is null)
		{
			return observation.Boolean("ok", false).String("refusal", "UnknownVariant").Complete();
		}

		QualificationSession.Logs.DeclareSensitive(PatchSymbol);
		AutoAssemblerScript script = new(source, PatchName);
		if (action == "check")
		{
			bool checkedOk = patches.TryCheck(script, out AutoAssemblerCheckResult result, out CheatEngineFailure failure);
			observation.Boolean("checked", checkedOk);
			if (checkedOk)
			{
				observation.Boolean("accepted", result.IsAccepted).Boolean("hostMessages", result.HostMessages is not null)
					.Boolean("hostMessagesTruncated", result.HostMessagesTruncated);
			}
			else
			{
				observation.Failure("failure", failure);
			}

			return observation.Boolean("ok", true).Complete();
		}

		if (QualificationSession.Patch is { IsReleased: false })
		{
			return observation.Boolean("ok", false).String("refusal", "PatchAlreadyApplied").Complete();
		}

		// The target was selected by the driver through Cheat Engine itself (openProcess), never through the Client.
		long selectionEpoch = active.Client.Processes.TryRefresh(out ProcessSnapshot process, out _)
			? process.SelectionEpoch
			: 0;
		observation.Number("processId", processId).Number("selectionEpochBeforeApply", selectionEpoch);
		bool applied = patches.TryApplyPatch(script, out IAutoAssemblerPatchLease? lease, out CheatEngineFailure applyFailure);
		observation.Boolean("applied", applied);
		if (lease is null)
		{
			return observation.Failure("failure", applyFailure).Boolean("ok", true).Complete();
		}

		QualificationSession.Patch = lease;
		return DescribePatch(observation, active);
	}

	private static string DescribePatch(QualificationObservation observation, QualificationSession.ActiveClient active)
	{
		if (QualificationSession.Patch is not { } lease)
		{
			return observation.Boolean("ok", false).String("refusal", "NoPatch").Complete();
		}

		observation.BeginObject("lease")
			.Boolean("canDisable", lease.CanDisable)
			.Boolean("released", lease.IsReleased)
			.Boolean("requiresManualRecovery", lease.RequiresManualRecovery)
			.Boolean("appliedAfterTargetChange", lease.AppliedAfterTargetChange)
			.Boolean("hostWarnings", lease.HostWarnings is not null)
			.Boolean("hostWarningsTruncated", lease.HostWarningsTruncated)
			.Number("selectionEpoch", lease.SelectionEpoch)
			.EndObject();
		WriteLastRelease(observation, lease.LastReleaseOutcome);
		bool resolves = active.Client.Inspection.TryResolveAddress(new SymbolExpression(PatchSymbol),
			AddressResolutionMode.Default, out _, out _);
		return observation.Boolean("symbolResolves", resolves).Boolean("ok", true).Complete();
	}

	private static string ReleasePatch(QualificationObservation observation, QualificationSession.ActiveClient active)
	{
		if (QualificationSession.Patch is not { } lease)
		{
			return observation.Boolean("ok", false).String("refusal", "NoPatch").Complete();
		}

		WriteReleaseOutcome(observation, "release", lease.Release());
		return DescribePatch(observation, active);
	}

	private static QualificationObservation WriteReleaseOutcome(QualificationObservation observation, string name,
		LeaseReleaseOutcome outcome)
	{
		return observation.BeginObject(name)
			.String("kind", outcome.Kind.ToString())
			.String("hostEffect", outcome.HostEffect.ToString())
			.Boolean("complete", outcome.IsComplete)
			.Boolean("retryable", outcome.IsRetryable)
			.Boolean("requiresManualRecovery", outcome.RequiresManualRecovery)
			.EndObject();
	}

	private static void WriteLastRelease(QualificationObservation observation, LeaseReleaseOutcome? outcome)
	{
		if (outcome is { } last)
		{
			WriteReleaseOutcome(observation, "lastRelease", last);
		}
		else
		{
			observation.String("lastRelease", null);
		}
	}
}
