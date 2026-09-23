using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using CheatEngine.Client;
using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Debugger;
using CheatEngine.Client.Events;
using CheatEngine.Client.Hashing;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Hotkeys;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.RemoteExecution;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;
using CheatEngine.Client.Timers;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Values;

using LivePlugin.Qualification.Harness;

namespace LivePlugin.Qualification;

/// <summary>
///     The scenario bodies behind the harness Lua functions. Every Cheat Engine interaction goes through the public
///     CheatEngine.Client API of the current activation (ADR-01): Cheat Engine-level setup such as opening the target,
///     allocating the scratch region, changing the pointer size or redefining a symbol belongs to the scenario's driver
///     Lua, never to this plugin. Each function returns one bounded, redacted JSON observation
///     (<see cref="QualificationObservation" />); an exception is reported by its type name only.
/// </summary>
internal static class QualificationScenarios
{
	/// <summary>Every name the driver and the harness create in Cheat Engine starts with this prefix.</summary>
	internal const string NamePrefix = "cheatengine_client_qualification_";

	/// <summary>The size of the scratch region the driver allocates (<c>alloc(&lt;prefix&gt;scratch,4096)</c>).</summary>
	internal const int ScratchLength = 4096;

	private const string BridgeFileName = "cheatengine-sdk-lua-bridge.dll";
	private const string RecordDescription = NamePrefix + "record";
	private const int ModuleLimit = 1024;
	private const int DefaultResultLimit = 100_000;
	private const int MaximumResultLimit = 1_000_000;

	// Offsets inside the scratch region, one slot per scenario kind so that no write overlaps another.
	private const int BytesOffset = 0;
	private const int Utf8Offset = 64;
	private const int Utf16Offset = 256;
	private const int Int32Offset = 512;
	private const int UInt32Offset = 528;
	private const int Int64Offset = 544;
	private const int AddressOffset = 640;
	private const int BatchOffset = 1024;

	private static readonly Lock _recordGate = new();
	private static int? _lastRecordId;

	/// <summary>Plugin identity, gate, fault switch, activation history and bridge hash (Q05, Q06, Q40, Q43).</summary>
	internal static string Status()
	{
		return Guarded("status", static observation =>
		{
			AuthorizationDecision gate = QualificationSession.Authorization;
			FaultDecision fault = QualificationLedger.LastFault;
			bool active = QualificationSession.TryGetActive(out QualificationSession.ActiveClient? current);
			observation.Boolean("ok", true)
				.BeginObject("gate").Boolean("allowed", gate.IsAllowed).String("denial", gate.Denial.ToString()).EndObject()
				.BeginObject("fault").String("stage", fault.Stage.ToString()).String("reason", fault.Reason.ToString())
				.EndObject()
				.BeginObject("plugin")
				.String("name", QualificationPlugin.DisplayName)
				.Number("pluginId", QualificationSession.PluginId)
				.Boolean("active", active)
				.Number("epoch", current?.Client.Epoch ?? 0)
				.Number("enableAttempts", QualificationLedger.EnableAttempts)
				.Number("activations", QualificationLedger.Activations)
				.Number("lastEpoch", QualificationLedger.LastEpoch)
				.Number("previousEpoch", QualificationLedger.PreviousEpoch)
				.EndObject();

			observation.BeginArray("assemblies");
			Identity(observation, "plugin", typeof(QualificationPlugin).Assembly);
			Identity(observation, "clientHosting", typeof(CheatEngineClientPlugin).Assembly);
			if (current is not null)
			{
				Identity(observation, "clientCore", current.Client.Memory.GetType().Assembly);
			}

			if (typeof(CheatEngineClientPlugin).BaseType is { } sdkPlugin)
			{
				Identity(observation, "sdkHosting", sdkPlugin.Assembly);
			}

			Identity(observation, "sdkEngine", typeof(Address).Assembly);
			observation.EndArray();

			string? bridge = BridgeSha256();
			observation.BeginObject("bridge").Boolean("present", bridge is not null).String("sha256", bridge).EndObject()
				.Strings("ledger", QualificationLedger.Entries())
				.Number("ledgerDropped", QualificationLedger.Dropped);
			return observation.Complete();
		});
	}

	/// <summary>The Client's process and runtime snapshots, the process id first (spike D2; Q31, Q32, Q45).</summary>
	internal static string Runtime()
	{
		return Guarded("runtime", static observation =>
		{
			if (!QualificationSession.TryGetActive(out QualificationSession.ActiveClient? active))
			{
				return Inactive(observation);
			}

			if (!active.Client.Processes.TryRefresh(out ProcessSnapshot process, out CheatEngineFailure failure))
			{
				return observation.Boolean("ok", false).Failure("failure", failure).Complete();
			}

			observation.BeginObject("process")
				.Number("processId", process.Id.Value)
				.String("targetArchitecture", process.TargetArchitecture.ToString())
				.Number("selectionEpoch", process.SelectionEpoch)
				.EndObject()
				.Boolean("targetSelected", process.Id.Value != 0);

			// With no target Cheat Engine reports the facts of an x64 target (spike D2): nothing is concluded without a PID.
			if (process.Id.Value != 0)
			{
				if (active.Client.Runtime.TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot, out failure))
				{
					observation.BeginObject("runtime")
						.String("systemArchitecture", snapshot.SystemArchitecture.ToString())
						.String("targetArchitecture", snapshot.TargetArchitecture.ToString())
						.Number("targetPointerSizeBytes", snapshot.TargetPointerSize.Bytes)
						.String("targetAbi", snapshot.TargetAbi.ToString())
						.Number("activationEpoch", snapshot.Epoch)
						.EndObject();
				}
				else
				{
					observation.Failure("runtimeFailure", failure);
				}
			}

			// This Client version exposes no configured pointer size apart from the process width; the observation says
			// so instead of deriving one.
			return observation.BeginObject("configuredPointerSize").Boolean("exposedByClient", false).EndObject()
				.Boolean("ok", true)
				.Complete();
		});
	}

	/// <summary>
	///     Availability of every Client capability, and with <paramref name="probeOnly" /> = 0 one harmless Try call of
	///     each contract-only family that reports itself unavailable (Q44). With 1 it only reads availability (Q45).
	/// </summary>
	internal static string Capabilities(long probeOnly)
	{
		return probeOnly == 0
			? RunMutating("capabilities", static (observation, active, processId) =>
				WriteCapabilities(observation, active, processId, performCalls: true))
			: Guarded("capabilities", static observation =>
				QualificationSession.TryGetActive(out QualificationSession.ActiveClient? active)
					? WriteCapabilities(observation, active, ClientProcessId(active), performCalls: false)
					: Inactive(observation));
	}

	/// <summary>
	///     Declares the scratch region the driver allocated in the authorized target, after verifying it through the Client
	///     inspection API: a harness-prefixed symbol, resolving to the base of a committed, private, writable region that
	///     holds <see cref="ScratchLength" /> bytes. Every later write must stay inside it.
	/// </summary>
	internal static string DeclareTarget(string symbolName)
	{
		return RunMutating("target_declare", (observation, active, processId) =>
		{
			if (!TryResolveScratch(observation, active, symbolName, out Address scratch, out MemoryRegionInfo region))
			{
				return observation.Complete();
			}

			bool committed = region.State == MemoryRegionState.Committed;
			bool privateMemory = region.Type == MemoryRegionType.Private;
			bool writable = (region.Protection & (MemoryProtection.ReadWrite | MemoryProtection.ExecuteReadWrite)) != 0;
			ulong regionEnd = region.BaseAddress.Value + region.Size.Value;
			bool fits = scratch.Value >= region.BaseAddress.Value && regionEnd >= region.BaseAddress.Value &&
						ScratchLength <= regionEnd - scratch.Value;
			observation.BeginObject("region")
				.Boolean("committed", committed)
				.Boolean("private", privateMemory)
				.Boolean("writable", writable)
				.Boolean("holdsScratch", fits)
				.EndObject();
			if (!(committed && privateMemory && writable && fits))
			{
				return observation.Boolean("ok", false).String("refusal", "RegionNotWritableScratch").Complete();
			}

			QualificationSession.Declare(new TargetDeclaration(processId,
				[new WritableRegion("scratch", scratch.Value, ScratchLength)]));
			QualificationSession.Logs.DeclareSensitive(QualificationObservation.Hex(scratch.Value));
			return observation.Boolean("ok", true).Address("scratch", scratch.Value).Number("length", ScratchLength)
				.Complete();
		});
	}

	/// <summary>
	///     One Client AOB scan with its detailed outcome (Q27, Q28, Q29): result bounds, host and copy metrics, managed
	///     allocations, and for a module scan an exactness check against the unfiltered scan.
	/// </summary>
	internal static string Aob(string pattern, string moduleName, long maxResults, long cancelAfterMs)
	{
		return Guarded("aob", observation =>
		{
			if (!QualificationSession.TryGetActive(out QualificationSession.ActiveClient? active))
			{
				return Inactive(observation);
			}

			if (!AobPattern.TryParse(pattern, out AobPattern aobPattern))
			{
				return observation.Boolean("ok", false).String("refusal", "InvalidPattern").Complete();
			}

			QualificationSession.Logs.DeclareSensitive(pattern);
			int limit = maxResults > 0 ? (int) Math.Min(maxResults, MaximumResultLimit) : DefaultResultLimit;
			ModuleName? module = string.IsNullOrWhiteSpace(moduleName) ? null : new ModuleName(moduleName);
			AobScanRequest request = new(aobPattern, AobScanOptions.Default, limit, module);
			using CancellationTokenSource? cancellation = cancelAfterMs > 0
				? new CancellationTokenSource(TimeSpan.FromMilliseconds(cancelAfterMs))
				: null;

			long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
			long started = Stopwatch.GetTimestamp();
			PatternScanOutcome outcome = active.Scans.ScanDetailed(request, cancellation?.Token ?? CancellationToken.None);
			TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
			long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

			List<ulong> matches = outcome.Result is { } result ? [.. result.Matches.Select(static match => match.Value)] : [];
			bool truncated = outcome.Result?.IsTruncated ?? false;
			observation.Boolean("ok", outcome.Succeeded)
				.BeginObject("request")
				.Number("patternLength", aobPattern.ByteLength)
				.Boolean("moduleFilter", module is not null)
				.Number("limit", limit)
				.Number("cancelAfterMs", cancelAfterMs)
				.EndObject();
			if (outcome.Cause is { } cause)
			{
				observation.Failure("failure", cause);
			}

			observation.Boolean("resultPublished", outcome.Result is not null)
				.Boolean("truncated", truncated)
				.Addresses("matches", matches)
				.Number("elapsedMicroseconds", Microseconds(elapsed))
				.Number("managedAllocatedBytes", allocated);
			WriteMetrics(observation, outcome.Metrics);

			bool cancelled = outcome.Cause is { Kind: CheatEngineFailureKind.Cancelled };
			observation.BeginObject("checks")
				.Boolean("truncationExplicit", outcome.Succeeded && truncated && matches.Count == limit)
				.Boolean("cancellationHonest", (cancelled && outcome.Result is null) || (outcome.Succeeded && !truncated))
				.Boolean("noPrefixPublished", outcome.Succeeded || outcome.Result is null)
				.Boolean("notFoundReported", outcome.Cause is { Kind: CheatEngineFailureKind.NotFound })
				.Boolean("indeterminateReported", outcome.Cause is { Kind: CheatEngineFailureKind.IndeterminateHostResult })
				.EndObject();

			if (module is { } moduleFilter && outcome.Succeeded && !truncated)
			{
				WriteModuleExactness(observation, active, aobPattern, moduleFilter, limit, matches);
			}

			return observation.Complete();
		});
	}

	/// <summary>
	///     Writes one boundary value or byte pattern into the declared scratch region through the Client memory API,
	///     reads it back exactly, and restores the original bytes (Q20, Q21).
	/// </summary>
	internal static string MemoryRoundTrip(string kind, string symbolName)
	{
		return RunMutating("memory_roundtrip", (observation, active, processId) =>
		{
			if (!TryResolveDeclaredScratch(observation, active, symbolName, processId, out Address scratch))
			{
				return observation.Complete();
			}

			observation.String("kind", kind);
			return kind switch
			{
				"bytes-with-nul" => BytesRoundTrip(observation, active, processId, scratch + BytesOffset,
					[0x41, 0x00, 0x42, 0x00, 0x00, 0x43]),
				"utf8-multibyte" => StringRoundTrip(observation, active, processId, scratch + Utf8Offset,
					"Qualification éü 漢字 🙂", wide: false),
				"utf16-with-nul" => StringRoundTrip(observation, active, processId, scratch + Utf16Offset,
					"A\0Bé", wide: true),
				"int32-minus-one" => Int32RoundTrip(observation, active, processId, scratch + Int32Offset, -1),
				"uint32-max" => UInt32RoundTrip(observation, active, processId, scratch + UInt32Offset, uint.MaxValue),
				"int64-limits" => Int64RoundTrip(observation, active, processId, scratch + Int64Offset),
				"address-above-4gib" => AddressRoundTrip(observation, active, processId, scratch + AddressOffset),
				_ => observation.Boolean("ok", false).String("refusal", "UnknownKind").Complete()
			};
		});
	}

	/// <summary>
	///     A four-element batch whose third address lies in the never-mapped null region (Q33): the detailed outcome must
	///     expose two completed writes, the failed index and a partial effect, confirmed by reading the scratch back.
	/// </summary>
	internal static string MemoryBatchPartial(string symbolName, long invalidAddress)
	{
		return RunMutating("memory_batch_partial", (observation, active, processId) =>
		{
			if (!TryResolveDeclaredScratch(observation, active, symbolName, processId, out Address scratch))
			{
				return observation.Complete();
			}

			ulong invalid = unchecked((ulong) invalidAddress);
			if (!QualificationWriteGuard.IsNeverMapped(invalid))
			{
				return observation.Boolean("ok", false).String("refusal", "InvalidAddressMayBeMapped").Complete();
			}

			Address first = scratch + BatchOffset;
			Address second = scratch + (BatchOffset + 4);
			Address fourth = scratch + (BatchOffset + 12);
			if (!GuardWrite(observation, processId, first, 16))
			{
				return observation.Complete();
			}

			if (!active.Client.Memory.TryReadBytes(new MemoryBytesReadRequest(first, 16), out ImmutableArray<byte> original,
					out CheatEngineFailure failure))
			{
				return observation.Boolean("ok", false).Failure("readOriginalFailure", failure).Complete();
			}

			int[] values = [0x11111111, 0x22222222, 0x33333333, 0x44444444];
			MemoryPrimitiveBatchWriteOutcome outcome = active.Batches.WritePrimitiveBatchDetailed(
				new MemoryPrimitiveBatchWriteRequest<int>(
				[
					new MemoryAddressValue<int>(first, values[0]),
					new MemoryAddressValue<int>(second, values[1]),
					new MemoryAddressValue<int>(new Address(invalid), values[2]),
					new MemoryAddressValue<int>(fourth, values[3])
				]));

			bool readFirst = active.Client.Memory.TryReadPrimitive(first, out int firstValue, out _);
			bool readSecond = active.Client.Memory.TryReadPrimitive(second, out int secondValue, out _);
			bool readFourth = active.Client.Memory.TryReadPrimitive(fourth, out int fourthValue, out _);
			int originalFourth = BitConverter.ToInt32(original.AsSpan(12, 4));
			bool restored = active.Client.Memory.TryWriteBytes(new MemoryBytesWriteRequest(first, original.AsSpan()),
				out _);

			observation.Boolean("ok", true)
				.BeginObject("outcome")
				.Number("attempted", outcome.AttemptedCount)
				.Number("completed", outcome.CompletedCount)
				.Number("failedIndex", outcome.FailedIndex ?? -1)
				.String("effectState", outcome.EffectState.ToString());
			if (outcome.Cause is { } cause)
			{
				observation.Failure("cause", cause);
			}

			bool partialExposed = outcome.AttemptedCount == 4 && outcome.CompletedCount == 2 && outcome.FailedIndex == 2 &&
								  outcome.EffectState == MemoryBatchWriteEffectState.Partial;
			bool readBackConfirms = readFirst && readSecond && readFourth && firstValue == values[0] &&
									secondValue == values[1] && fourthValue == originalFourth;
			return observation.EndObject()
				.BeginObject("checks")
				.Boolean("partialEffectExposed", partialExposed)
				.Boolean("readBackConfirms", readBackConfirms)
				.Boolean("originalRestored", restored)
				.EndObject()
				.Complete();
		});
	}

	/// <summary>Creates one memory record on the scratch symbol through the Client table API and remembers its id (Q34).</summary>
	internal static string TableCreate(string symbolName)
	{
		return RunMutating("table_create", (observation, active, processId) =>
		{
			if (!TryResolveDeclaredScratch(observation, active, symbolName, processId, out _))
			{
				return observation.Complete();
			}

			if (!active.Client.Tables.TryCreate(new MemoryRecordDefinition(RecordDescription, symbolName, "0",
					VariableType.Dword), out MemoryRecordSnapshot record, out CheatEngineFailure failure))
			{
				return observation.Boolean("ok", false).Failure("failure", failure).Complete();
			}

			lock (_recordGate)
			{
				_lastRecordId = record.Id.Value;
			}

			return observation.Boolean("ok", true)
				.Number("recordId", record.Id.Value)
				.String("description", record.Description)
				.Complete();
		});
	}

	/// <summary>
	///     Reads the record created by <see cref="TableCreate" /> again after the driver destroyed it or reloaded the table
	///     (Q34): the old id must be refused, never answered by another record.
	/// </summary>
	internal static string TableProbe()
	{
		return Guarded("table_probe", static observation =>
		{
			if (!QualificationSession.TryGetActive(out QualificationSession.ActiveClient? active))
			{
				return Inactive(observation);
			}

			int? recordId;
			lock (_recordGate)
			{
				recordId = _lastRecordId;
			}

			if (recordId is not { } id)
			{
				return observation.Boolean("ok", false).String("refusal", "NoRecordCreated").Complete();
			}

			bool found = active.Client.Tables.TryGetRecord(new MemoryRecordId(id), out MemoryRecordSnapshot record,
				out CheatEngineFailure failure);
			bool sameRecord = found && string.Equals(record.Description, RecordDescription, StringComparison.Ordinal);
			observation.Boolean("ok", true).Number("recordId", id).Boolean("found", found).Boolean("sameRecord", sameRecord);
			if (!found)
			{
				observation.Failure("failure", failure);
			}

			return observation.BeginObject("checks")
				.Boolean("oldReferenceRefused", !found)
				.Boolean("reusedAsAnotherRecord", found && !sameRecord)
				.EndObject()
				.Complete();
		});
	}

	/// <summary>Registers a harness-prefixed symbol on the scratch address through the Client symbol lease (Q16.b).</summary>
	internal static string SymbolRegister(string name, string symbolName)
	{
		return RunMutating("symbol_register", (observation, active, processId) =>
		{
			if (!name.StartsWith(NamePrefix, StringComparison.Ordinal))
			{
				return observation.Boolean("ok", false).String("refusal", "NameNotHarness").Complete();
			}

			if (!TryResolveDeclaredScratch(observation, active, symbolName, processId, out Address scratch))
			{
				return observation.Complete();
			}

			if (!active.Client.Inspection.TryRegisterSymbol(new SymbolRegistration(name, scratch),
					out ISymbolRegistrationLease? lease, out CheatEngineFailure failure))
			{
				return observation.Boolean("ok", false).Failure("failure", failure).Complete();
			}

			QualificationSession.KeepSymbolLease(lease);
			return observation.Boolean("ok", true).String("name", lease.Name).Address("address", lease.Address.Value)
				.Complete();
		});
	}

	/// <summary>Disposes the harness's lease of a symbol, as a plugin disable would (Q16.b).</summary>
	internal static string SymbolRelease(string name)
	{
		return RunMutating("symbol_release", (observation, _, _) =>
		{
			if (!QualificationSession.TryGetSymbolLease(name, out ISymbolRegistrationLease? lease))
			{
				return observation.Boolean("ok", false).String("refusal", "NoLease").Complete();
			}

			bool releasedBefore = lease.IsReleased;
			lease.Dispose();
			return observation.Boolean("ok", true).Boolean("releasedBefore", releasedBefore)
				.Boolean("released", lease.IsReleased).Complete();
		});
	}

	/// <summary>The harness's lease of a symbol and what the name resolves to now, through the Client (Q16.b).</summary>
	internal static string SymbolState(string name)
	{
		return Guarded("symbol_state", observation =>
		{
			if (!QualificationSession.TryGetActive(out QualificationSession.ActiveClient? active))
			{
				return Inactive(observation);
			}

			bool hasLease = QualificationSession.TryGetSymbolLease(name, out ISymbolRegistrationLease? lease);
			observation.Boolean("ok", true).Boolean("hasLease", hasLease);
			if (lease is not null)
			{
				observation.BeginObject("lease").Address("address", lease.Address.Value)
					.Boolean("released", lease.IsReleased).EndObject();
			}

			bool resolves = active.Client.Inspection.TryResolveAddress(new SymbolExpression(name),
				new AddressResolutionOptions(), out Address current, out CheatEngineFailure failure);
			observation.Boolean("resolves", resolves);
			if (resolves)
			{
				observation.Address("resolvedAddress", current.Value)
					.Boolean("resolvesToLeasedAddress", lease is not null && lease.Address == current);
			}
			else
			{
				observation.Failure("resolveFailure", failure);
			}

			return observation.Complete();
		});
	}

	/// <summary>The captured log events: templates and event ids, never formatted messages (Q46).</summary>
	internal static string Logs()
	{
		return Guarded("logs", static observation =>
		{
			const int Reported = 64;
			IReadOnlyList<CapturedLogEvent> events = QualificationSession.Logs.Events();
			observation.Boolean("ok", true)
				.Number("eventCount", events.Count)
				.Number("dropped", QualificationSession.Logs.Dropped)
				.Number("sensitiveHits", QualificationSession.Logs.SensitiveHits)
				.BeginArray("events");
			for (int index = Math.Max(0, events.Count - Reported); index < events.Count; index++)
			{
				CapturedLogEvent captured = events[index];
				observation.BeginItem()
					.String("category", captured.Category)
					.Number("eventId", captured.EventId)
					.String("eventName", captured.EventName)
					.String("level", captured.Level.ToString())
					.String("template", captured.Template)
					.EndObject();
			}

			return observation.EndArray().Complete();
		});
	}

	/// <summary>
	///     Runs a function that changes the target or Cheat Engine state: only for an enabled activation, a gate that
	///     authorized the run, and a Client that observes exactly the authorized target.
	/// </summary>
	internal static string RunMutating(string function,
		Func<QualificationObservation, QualificationSession.ActiveClient, int, string> body)
	{
		return Guarded(function, observation =>
		{
			if (!QualificationSession.TryGetActive(out QualificationSession.ActiveClient? active))
			{
				return Inactive(observation);
			}

			AuthorizationDecision gate = QualificationSession.Authorization;
			int processId = ClientProcessId(active);
			if (!gate.IsAllowed)
			{
				return observation.Boolean("ok", false).String("refusal", nameof(WriteRefusal.NotAuthorized))
					.String("denial", gate.Denial.ToString()).Complete();
			}

			if (!gate.Allows(processId))
			{
				return observation.Boolean("ok", false).String("refusal", nameof(WriteRefusal.TargetNotAuthorized))
					.Complete();
			}

			return body(observation, active, processId);
		});
	}

	private static string Guarded(string function, Func<QualificationObservation, string> body)
	{
		using QualificationObservation observation = new(function);
		try
		{
			return body(observation);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			using QualificationObservation failed = new(function);
			return failed.Boolean("ok", false).String("exception", exception.GetType().Name).Complete();
		}
	}

	private static string Inactive(QualificationObservation observation)
	{
		return observation.Boolean("ok", false).String("refusal", "NoActiveClient").Complete();
	}

	private static int ClientProcessId(QualificationSession.ActiveClient active)
	{
		return active.Client.Processes.TryRefresh(out ProcessSnapshot process, out _) ? process.Id.Value : 0;
	}

	private static string WriteCapabilities(QualificationObservation observation,
		QualificationSession.ActiveClient active, int processIdBefore, bool performCalls)
	{
		observation.Boolean("probeOnly", !performCalls).BeginArray("families");
		foreach (ClientCapabilityId capability in (ClientCapabilityId[])
				 [
					 ClientCapabilityId.ProcessSelection, ClientCapabilityId.TypedMemory, ClientCapabilityId.PatternScanning,
					 ClientCapabilityId.ValueScanning, ClientCapabilityId.Inspection, ClientCapabilityId.Tables,
					 ClientCapabilityId.ProtectedLua, ClientCapabilityId.UnsafeLuaExecution, ClientCapabilityId.Allocations,
					 ClientCapabilityId.Assembly, ClientCapabilityId.RemoteExecution, ClientCapabilityId.Debugger,
					 ClientCapabilityId.Hotkeys, ClientCapabilityId.Timers, ClientCapabilityId.Speed,
					 ClientCapabilityId.Hashing, ClientCapabilityId.Dbvm
				 ])
		{
			observation.BeginItem().String("capability", capability.Value);
			bool known = active.Client.Runtime.TryGetClientCapability(capability,
				out ClientCapabilityAvailability availability, out CheatEngineFailure failure);
			if (!known)
			{
				observation.Failure("availabilityFailure", failure).EndObject();
				continue;
			}

			observation.String("state", availability.State.ToString());
			if (performCalls && availability.State == ClientCapabilityAvailabilityState.Unavailable &&
				TryHarmlessCall(active.Client, capability, out bool succeeded, out CheatEngineFailure callFailure))
			{
				observation.BeginObject("call").Boolean("succeeded", succeeded);
				if (!succeeded)
				{
					observation.Failure("failure", callFailure);
				}

				observation.EndObject();
			}

			observation.EndObject();
		}

		int processIdAfter = ClientProcessId(active);
		return observation.EndArray()
			.Number("processIdBefore", processIdBefore)
			.Number("processIdAfter", processIdAfter)
			.Boolean("processUnchanged", processIdBefore == processIdAfter)
			.Boolean("ok", true)
			.Complete();
	}

	// One call per contract-only family, chosen to be harmless even if the family were available: nothing is written,
	// executed or kept (every lease is disposed at once), and the address is in the never-mapped null region. It is only
	// made when the capability already reports the family unavailable, so it checks that refusal (Q44).
	private static bool TryHarmlessCall(ICheatEngineClient client, ClientCapabilityId capability, out bool succeeded,
		out CheatEngineFailure failure)
	{
		Address nullRegion = new(0x10);
		failure = default;
		succeeded = false;
		switch (capability.Value)
		{
			case "Client.Allocations":
				succeeded = client.Allocations.TryAllocate(new TargetAllocationRequest(16), out ITargetMemoryLease? lease,
					out failure, client.Stopping);
				lease?.Dispose();
				return true;
			case "Client.Assembly":
				succeeded = client.Assembly.TryDisassemble(nullRegion, out _, out failure, client.Stopping);
				return true;
			case "Client.RemoteExecution":
				succeeded = client.RemoteExecution.TryInjectLibrary(
					new RemoteDllInjectionRequest(Path.Combine(Path.GetTempPath(), NamePrefix + "absent.dll")), out failure,
					client.Stopping);
				return true;
			case "Client.Debugger":
				succeeded = client.Debugger.TryRegisterBreakpoint(new BreakpointRequest(nullRegion),
					static _ => BreakpointDisposition.Continue, new EventStreamOptions(1), out IBreakpointLease? breakpoint,
					out failure, client.Stopping);
				breakpoint?.Dispose();
				return true;
			case "Client.Hotkeys":
				succeeded = client.Hotkeys.TryRegister(new HotkeyRegistration(NamePrefix + "hotkey", new HotkeyGesture(0x87)),
					static _ =>
					{
					}, new EventStreamOptions(1), out IHotkeyLease? hotkey, out failure, client.Stopping);
				hotkey?.Dispose();
				return true;
			case "Client.Timers":
				succeeded = client.Timers.TryRegister(new TimerRequest(TimeSpan.FromHours(1)), static _ =>
				{
				}, new EventStreamOptions(1), out ITimerLease? timer, out failure, client.Stopping);
				timer?.Dispose();
				return true;
			case "Client.Speed":
				succeeded = client.Speed.TryGetMultiplier(out _, out failure, client.Stopping);
				return true;
			case "Client.Hashing":
				succeeded = client.Hashing.TryHashMemory(new MemoryHashRequest(nullRegion, 1), out _, out failure,
					client.Stopping);
				return true;
			case "Client.Dbvm":
				succeeded = client.Dbvm.TryGetStatus(out _, out failure, client.Stopping);
				return true;
			case "Client.ValueScanning":
				succeeded = client.Scans.TryCreateSession(out IValueScanSession? session, out failure, client.Stopping);
				session?.Dispose();
				return true;
			default:
				return false;
		}
	}

	private static bool TryResolveScratch(QualificationObservation observation, QualificationSession.ActiveClient active,
		string symbolName, out Address scratch, out MemoryRegionInfo region)
	{
		scratch = default;
		region = default;
		if (!symbolName.StartsWith(NamePrefix, StringComparison.Ordinal))
		{
			observation.Boolean("ok", false).String("refusal", "SymbolNotHarness");
			return false;
		}

		if (!active.Client.Inspection.TryResolveAddress(new SymbolExpression(symbolName), new AddressResolutionOptions(),
				out scratch, out CheatEngineFailure failure))
		{
			observation.Boolean("ok", false).Failure("resolveFailure", failure);
			return false;
		}

		if (!active.Client.Inspection.TryGetMemoryRegion(scratch, out region, out failure))
		{
			observation.Boolean("ok", false).Failure("regionFailure", failure);
			return false;
		}

		return true;
	}

	private static bool TryResolveDeclaredScratch(QualificationObservation observation,
		QualificationSession.ActiveClient active, string symbolName, int processId, out Address scratch)
	{
		if (!TryResolveScratch(observation, active, symbolName, out scratch, out _))
		{
			return false;
		}

		return GuardWrite(observation, processId, scratch, ScratchLength);
	}

	private static bool GuardWrite(QualificationObservation observation, int processId, Address address, int length)
	{
		WriteRefusal refusal = QualificationWriteGuard.Evaluate(QualificationSession.Authorization, processId,
			QualificationSession.Declaration, address.Value, length);
		if (refusal == WriteRefusal.None)
		{
			return true;
		}

		observation.Boolean("ok", false).String("refusal", refusal.ToString());
		return false;
	}

	private static string BytesRoundTrip(QualificationObservation observation, QualificationSession.ActiveClient active,
		int processId, Address address, byte[] payload)
	{
		if (!GuardWrite(observation, processId, address, payload.Length) ||
			!TryReadOriginal(observation, active, address, payload.Length, out ImmutableArray<byte> original))
		{
			return observation.Complete();
		}

		bool written = active.Client.Memory.TryWriteBytes(new MemoryBytesWriteRequest(address, payload),
			out CheatEngineFailure failure);
		bool read = active.Client.Memory.TryReadBytes(new MemoryBytesReadRequest(address, payload.Length),
			out ImmutableArray<byte> readBack, out CheatEngineFailure readFailure);
		Restore(observation, active, address, original);
		observation.Boolean("ok", written && read).Number("writtenLength", payload.Length)
			.Number("readLength", read ? readBack.Length : 0)
			.Boolean("bytesEqual", read && readBack.AsSpan().SequenceEqual(payload));
		WriteFailures(observation, written ? null : failure, read ? null : readFailure);
		return observation.Complete();
	}

	private static string StringRoundTrip(QualificationObservation observation, QualificationSession.ActiveClient active,
		int processId, Address address, string value, bool wide)
	{
		byte[] expected = wide ? Encoding.Unicode.GetBytes(value) : Encoding.UTF8.GetBytes(value);
		int window = expected.Length + (wide ? 2 : 1);
		if (!GuardWrite(observation, processId, address, window) ||
			!TryReadOriginal(observation, active, address, window, out ImmutableArray<byte> original))
		{
			return observation.Complete();
		}

		bool written = active.Client.Memory.TryWriteString(new MemoryStringWriteRequest(address, value, wide),
			out CheatEngineFailure failure);
		bool readBytes = active.Client.Memory.TryReadBytes(new MemoryBytesReadRequest(address, expected.Length),
			out ImmutableArray<byte> readBack, out CheatEngineFailure readFailure);
		bool readText = active.Client.Memory.TryReadString(new MemoryStringReadRequest(address, value.Length, wide),
			out string? text, out _);
		Restore(observation, active, address, original);
		observation.Boolean("ok", written && readBytes)
			.String("encoding", wide ? "Utf16" : "Utf8")
			.Number("expectedByteLength", expected.Length)
			.Boolean("bytesEqual", readBytes && readBack.AsSpan().SequenceEqual(expected))
			.Boolean("textRead", readText)
			.Boolean("textEqual", readText && string.Equals(text, value, StringComparison.Ordinal))
			.Number("textLength", readText ? text!.Length : -1);
		WriteFailures(observation, written ? null : failure, readBytes ? null : readFailure);
		return observation.Complete();
	}

	private static string Int32RoundTrip(QualificationObservation observation, QualificationSession.ActiveClient active,
		int processId, Address address, int value)
	{
		if (!GuardWrite(observation, processId, address, sizeof(int)) ||
			!TryReadOriginal(observation, active, address, sizeof(int), out ImmutableArray<byte> original))
		{
			return observation.Complete();
		}

		bool written = active.Client.Memory.TryWritePrimitive(address, value, out CheatEngineFailure failure);
		bool readSigned = active.Client.Memory.TryReadPrimitive(address, out int signed, out _);
		bool readUnsigned = active.Client.Memory.TryReadPrimitive(address, out uint unsigned, out _);
		bool readBytes = active.Client.Memory.TryReadBytes(new MemoryBytesReadRequest(address, sizeof(int)),
			out ImmutableArray<byte> bytes, out _);
		Restore(observation, active, address, original);
		observation.Boolean("ok", written && readSigned && readUnsigned && readBytes)
			.Number("signed", signed)
			.Number("unsigned", unsigned)
			.String("bytes", readBytes ? Convert.ToHexString(bytes.AsSpan()) : null)
			.Boolean("signedEqual", readSigned && signed == value)
			.Boolean("unsignedEqual", readUnsigned && unsigned == unchecked((uint) value));
		WriteFailures(observation, written ? null : failure, null);
		return observation.Complete();
	}

	private static string UInt32RoundTrip(QualificationObservation observation, QualificationSession.ActiveClient active,
		int processId, Address address, uint value)
	{
		if (!GuardWrite(observation, processId, address, sizeof(uint)) ||
			!TryReadOriginal(observation, active, address, sizeof(uint), out ImmutableArray<byte> original))
		{
			return observation.Complete();
		}

		bool written = active.Client.Memory.TryWritePrimitive(address, value, out CheatEngineFailure failure);
		bool readUnsigned = active.Client.Memory.TryReadPrimitive(address, out uint unsigned, out _);
		bool readSigned = active.Client.Memory.TryReadPrimitive(address, out int signed, out _);
		Restore(observation, active, address, original);
		observation.Boolean("ok", written && readUnsigned && readSigned)
			.Number("unsigned", unsigned)
			.Number("signed", signed)
			.Boolean("unsignedEqual", readUnsigned && unsigned == value)
			.Boolean("signedEqual", readSigned && signed == unchecked((int) value));
		WriteFailures(observation, written ? null : failure, null);
		return observation.Complete();
	}

	private static string Int64RoundTrip(QualificationObservation observation, QualificationSession.ActiveClient active,
		int processId, Address address)
	{
		// 2^53 + 1 is the first integer a double cannot represent: a round trip through a double would lose it.
		long[] signedValues = [long.MinValue, long.MaxValue, -1, (1L << 53) + 1];
		ulong[] unsignedValues = [ulong.MaxValue, (1UL << 63) + 1];
		if (!GuardWrite(observation, processId, address, sizeof(long)) ||
			!TryReadOriginal(observation, active, address, sizeof(long), out ImmutableArray<byte> original))
		{
			return observation.Complete();
		}

		bool allEqual = true;
		observation.BeginArray("values");
		foreach (long value in signedValues)
		{
			bool written = active.Client.Memory.TryWritePrimitive(address, value, out _);
			bool read = active.Client.Memory.TryReadPrimitive(address, out long readBack, out _);
			bool equal = written && read && readBack == value;
			allEqual &= equal;
			observation.BeginItem().String("type", "long").String("value", value.ToString(CultureInfo.InvariantCulture))
				.Boolean("equal", equal).EndObject();
		}

		foreach (ulong value in unsignedValues)
		{
			bool written = active.Client.Memory.TryWritePrimitive(address, value, out _);
			bool read = active.Client.Memory.TryReadPrimitive(address, out ulong readBack, out _);
			bool equal = written && read && readBack == value;
			allEqual &= equal;
			observation.BeginItem().String("type", "ulong").String("value", value.ToString(CultureInfo.InvariantCulture))
				.Boolean("equal", equal).EndObject();
		}

		Restore(observation.EndArray(), active, address, original);
		return observation.Boolean("ok", true).Boolean("allEqual", allEqual).Complete();
	}

	private static string AddressRoundTrip(QualificationObservation observation, QualificationSession.ActiveClient active,
		int processId, Address address)
	{
		const ulong AboveFourGibibytes = 0x0000_7FF7_1234_5678;
		if (!GuardWrite(observation, processId, address, sizeof(ulong)) ||
			!TryReadOriginal(observation, active, address, sizeof(ulong), out ImmutableArray<byte> original))
		{
			return observation.Complete();
		}

		bool written = active.Client.Memory.TryWritePrimitive(address, new Address(AboveFourGibibytes),
			out CheatEngineFailure failure);
		bool readAddress = active.Client.Memory.TryReadPrimitive(address, out Address readBack, out _);
		bool readRaw = active.Client.Memory.TryReadPrimitive(address, out ulong raw, out _);
		Restore(observation, active, address, original);
		observation.Boolean("ok", written && readAddress && readRaw)
			.Boolean("valueEqual", readAddress && readBack.Value == AboveFourGibibytes)
			.Boolean("rawEqual", readRaw && raw == AboveFourGibibytes)
			.Boolean("scratchAboveFourGibibytes", address.Value > uint.MaxValue);
		WriteFailures(observation, written ? null : failure, null);

		// The target's own image base (above 4 GiB for the x64 qualification target) is read, never written.
		if (active.Client.Inspection.TryGetModules(new InspectionCollectionRequest(ModuleLimit),
				out ImmutableArray<ModuleInfo> modules, out _) &&
			modules.FirstOrDefault(static module => module.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) is
			{ Name.Length: > 0 } image)
		{
			bool readHeader = active.Client.Memory.TryReadBytes(new MemoryBytesReadRequest(image.BaseAddress, 2),
				out ImmutableArray<byte> header, out _);
			observation.BeginObject("imageBase")
				.Address("address", image.BaseAddress.Value)
				.Boolean("aboveFourGibibytes", image.BaseAddress.Value > uint.MaxValue)
				.Boolean("headerIsMz", readHeader && header.Length == 2 && header[0] == 0x4D && header[1] == 0x5A)
				.EndObject();
		}

		return observation.Complete();
	}

	private static bool TryReadOriginal(QualificationObservation observation, QualificationSession.ActiveClient active,
		Address address, int length, out ImmutableArray<byte> original)
	{
		if (active.Client.Memory.TryReadBytes(new MemoryBytesReadRequest(address, length), out original,
				out CheatEngineFailure failure))
		{
			return true;
		}

		observation.Boolean("ok", false).Failure("readOriginalFailure", failure);
		return false;
	}

	private static void Restore(QualificationObservation observation, QualificationSession.ActiveClient active,
		Address address, ImmutableArray<byte> original)
	{
		observation.Boolean("originalRestored",
			active.Client.Memory.TryWriteBytes(new MemoryBytesWriteRequest(address, original.AsSpan()), out _));
	}

	private static void WriteFailures(QualificationObservation observation, CheatEngineFailure? writeFailure,
		CheatEngineFailure? readFailure)
	{
		if (writeFailure is { } write)
		{
			observation.Failure("writeFailure", write);
		}

		if (readFailure is { } read)
		{
			observation.Failure("readFailure", read);
		}
	}

	private static void WriteMetrics(QualificationObservation observation, PatternScanMetrics? metrics)
	{
		if (metrics is not { } value)
		{
			observation.Boolean("metricsReported", false);
			return;
		}

		observation.Boolean("metricsReported", true)
			.BeginObject("metrics")
			.Number("hostMatchCount", value.HostMatchCount)
			.Number("examinedCount", value.ExaminedCount)
			.Number("filteredOutCount", value.FilteredOutCount)
			.Number("materializedCount", value.MaterializedCount)
			.String("scope", value.Scope.ToString())
			.Number("hostScanMicroseconds", Microseconds(value.HostScanElapsed))
			.Number("materializationMicroseconds", Microseconds(value.MaterializationElapsed))
			.EndObject();
	}

	private static void WriteModuleExactness(QualificationObservation observation,
		QualificationSession.ActiveClient active, AobPattern pattern, ModuleName module, int limit,
		List<ulong> filtered)
	{
		if (!active.Client.Inspection.TryGetModules(new InspectionCollectionRequest(ModuleLimit),
				out ImmutableArray<ModuleInfo> modules, out CheatEngineFailure failure))
		{
			observation.Failure("moduleFailure", failure);
			return;
		}

		ModuleInfo? match = null;
		foreach (ModuleInfo candidate in modules)
		{
			if (string.Equals(candidate.Name, module.Value, StringComparison.OrdinalIgnoreCase))
			{
				match = candidate;
				break;
			}
		}

		if (match is not { ImageSize: { } size } found)
		{
			observation.Boolean("moduleFound", false);
			return;
		}

		ulong start = found.BaseAddress.Value;
		ulong end = start + size.Value;
		bool allInside = filtered.TrueForAll(address => address >= start && address < end);
		PatternScanOutcome global = active.Scans.ScanDetailed(new AobScanRequest(pattern, AobScanOptions.Default, limit));
		List<ulong> globalMatches = global.Result is { } result
			? [.. result.Matches.Select(static address => address.Value)]
			: [];
		int insideCount = globalMatches.Count(address => address >= start && address < end);
		bool globalComplete = global.Succeeded && global.Result is { IsTruncated: false };
		observation.Boolean("moduleFound", true)
			.BeginObject("moduleCheck")
			.Address("base", start)
			.Number("size", (long) size.Value)
			.Boolean("allInside", allInside)
			.Number("globalCount", globalMatches.Count)
			.Number("globalInsideCount", insideCount)
			.Boolean("filterExact", globalComplete && allInside && insideCount == filtered.Count)
			.EndObject();
	}

	private static void Identity(QualificationObservation observation, string role, System.Reflection.Assembly assembly)
	{
		observation.BeginItem()
			.String("role", role)
			.String("name", assembly.GetName().Name)
			.String("informationalVersion",
				assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
			.String("mvid", assembly.ManifestModule.ModuleVersionId.ToString())
			.EndObject();
	}

	private static string? BridgeSha256()
	{
		string? directory = QualificationPlugin.PluginDirectory();
		string? bridge = directory is null ? null : Path.Combine(directory, BridgeFileName);
		if (bridge is null || !File.Exists(bridge))
		{
			return null;
		}

		using FileStream stream = File.OpenRead(bridge);
		return Convert.ToHexStringLower(SHA256.HashData(stream));
	}

	private static long Microseconds(TimeSpan elapsed)
	{
		return (long) Math.Round(elapsed.TotalMicroseconds);
	}
}
