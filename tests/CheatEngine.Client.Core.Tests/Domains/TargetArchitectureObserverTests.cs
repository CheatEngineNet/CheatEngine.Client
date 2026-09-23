using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class TargetArchitectureObserverTests
{
	[Fact]
	[Trait("Qualification", "Q32")]
	public void ObserverReadsFactsInPidFirstBracketedOrder()
	{
		RecordingProbe probe = new(42);

		ObservedTargetArchitecture observed = TargetArchitectureObserver.Observe(probe, readConfiguredPointerSize: true);

		Assert.Equal(
		[
			nameof(ITargetArchitectureProbe.GetOpenedProcessId),
			nameof(ITargetArchitectureProbe.TargetIs64Bit),
			nameof(ITargetArchitectureProbe.TargetIsX86),
			nameof(ITargetArchitectureProbe.TargetIsArm),
			nameof(ITargetArchitectureProbe.GetConfiguredPointerSize),
			nameof(ITargetArchitectureProbe.GetOpenedProcessId)
		], probe.Calls);
		Assert.True(observed.HasTarget);
		Assert.False(observed.TargetChangedDuringObservation);
		Assert.Equal(CheatEngineArchitecture.X64, observed.Architecture);
		Assert.Equal(PointerSize.Bit64, observed.ProcessPointerSize);
		Assert.Equal(8, observed.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Bit64, observed.ConfiguredPointerSizeKnown);
		Assert.False(observed.ConfiguredPointerSizeDiffersFromProcessWidth);
	}

	[Fact]
	public void ObserverNeverReadsTheConfiguredPointerSizeWhenNotRequested()
	{
		RecordingProbe probe = new(42);

		ObservedTargetArchitecture observed = TargetArchitectureObserver.Observe(probe, readConfiguredPointerSize: false);

		Assert.DoesNotContain(nameof(ITargetArchitectureProbe.GetConfiguredPointerSize), probe.Calls);
		Assert.Null(observed.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Unknown, observed.ConfiguredPointerSizeKnown);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, observed.ConfiguredPointerSize.Evidence.State);
		Assert.Equal(CheatEngineArchitecture.X64, observed.Architecture);
		Assert.False(observed.ConfiguredPointerSizeDiffersFromProcessWidth);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void ObserverTreatsTheFileAsProcessSentinelAsNoTarget()
	{
		RecordingProbe probe = new(4294967295L);

		ObservedTargetArchitecture observed = TargetArchitectureObserver.Observe(probe, readConfiguredPointerSize: true);

		Assert.Equal([nameof(ITargetArchitectureProbe.GetOpenedProcessId)], probe.Calls);
		Assert.False(observed.HasTarget);
		Assert.Equal(ClientCapabilityEvidenceState.Malformed, observed.ProcessId.Evidence.State);
		Assert.Equal(CheatEngineArchitecture.Unknown, observed.Architecture);
		Assert.Equal(PointerSize.Unknown, observed.ProcessPointerSize);
		Assert.Null(observed.ConfiguredPointerSizeBytes);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void ObserverKeepsTheProcessWidthWhenTheIsaIsUnknown()
	{
		RecordingProbe probe = new(42)
		{
			IsX86 = false,
			IsArm = false,
			Is64Bit = false,
			ConfiguredPointerSize = 4
		};

		ObservedTargetArchitecture observed = TargetArchitectureObserver.Observe(probe, readConfiguredPointerSize: true);

		Assert.Equal(CheatEngineArchitecture.Unknown, observed.Architecture);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, observed.ArchitectureEvidence.Evidence.State);
		Assert.Equal(PointerSize.Bit32, observed.ProcessPointerSize);
	}

	[Fact]
	[Trait("Qualification", "Q31")]
	public void ObserverReportsAConfiguredPointerSizeThatDiffersFromTheProcessWidth()
	{
		RecordingProbe probe = new(42)
		{
			ConfiguredPointerSize = 4
		};

		ObservedTargetArchitecture observed = TargetArchitectureObserver.Observe(probe, readConfiguredPointerSize: true);

		Assert.Equal(PointerSize.Bit64, observed.ProcessPointerSize);
		Assert.Equal(4, observed.ConfiguredPointerSizeBytes);
		Assert.Equal(PointerSize.Bit32, observed.ConfiguredPointerSizeKnown);
		Assert.True(observed.ConfiguredPointerSizeDiffersFromProcessWidth);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void ObserverReportsAFaultedClosingPidReadAsUnconfirmedRatherThanAsATargetChange()
	{
		RecordingProbe probe = new(42)
		{
			ClosingProcessIdException = new LuaException("getOpenedProcessID failed")
		};

		ObservedTargetArchitecture observed = TargetArchitectureObserver.Observe(probe, readConfiguredPointerSize: true);

		Assert.False(observed.HasTarget);
		Assert.False(observed.TargetChangedDuringObservation);
		Assert.True(observed.TargetUnconfirmed);
		Assert.False(observed.NoTargetSelected);
		Assert.Equal(CheatEngineArchitecture.Unknown, observed.Architecture);
		Assert.Equal(PointerSize.Unknown, observed.ProcessPointerSize);
		Assert.Equal(ClientCapabilityEvidenceState.Faulted, observed.Is64Bit.Evidence.State);
		Assert.Equal(TargetArchitectureObserver.TargetUnconfirmedReason, observed.Is64Bit.Evidence.Reason);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void ObserverReportsADifferentClosingPidAsATargetChange()
	{
		RecordingProbe probe = new(42)
		{
			ClosingProcessId = 43
		};

		ObservedTargetArchitecture observed = TargetArchitectureObserver.Observe(probe, readConfiguredPointerSize: true);

		Assert.False(observed.HasTarget);
		Assert.True(observed.TargetChangedDuringObservation);
		Assert.False(observed.TargetUnconfirmed);
		Assert.Equal(ClientCapabilityEvidenceState.Faulted, observed.Is64Bit.Evidence.State);
		Assert.Equal(TargetArchitectureObserver.TargetChangedReason, observed.Is64Bit.Evidence.Reason);
	}

	private sealed class RecordingProbe(long processId) : ITargetArchitectureProbe
	{
		internal List<string> Calls
		{
			get;
		} = [];

		/// <summary>The PID returned by the reads after the first one; <see langword="null" /> repeats the first.</summary>
		internal long? ClosingProcessId
		{
			get;
			init;
		}

		/// <summary>Thrown by the reads of the opened PID after the first one.</summary>
		internal Exception? ClosingProcessIdException
		{
			get;
			init;
		}

		internal bool Is64Bit
		{
			get;
			init;
		} = true;

		internal bool IsX86
		{
			get;
			init;
		} = true;

		internal bool IsArm
		{
			get;
			init;
		}

		internal int ConfiguredPointerSize
		{
			get;
			init;
		} = 8;

		public long GetOpenedProcessId()
		{
			bool closing = Calls.Contains(nameof(GetOpenedProcessId));
			Calls.Add(nameof(GetOpenedProcessId));
			if (closing && ClosingProcessIdException is { } exception)
			{
				throw exception;
			}

			return closing ? ClosingProcessId ?? processId : processId;
		}

		public bool TargetIs64Bit()
		{
			Calls.Add(nameof(TargetIs64Bit));
			return Is64Bit;
		}

		public bool TargetIsX86()
		{
			Calls.Add(nameof(TargetIsX86));
			return IsX86;
		}

		public bool TargetIsArm()
		{
			Calls.Add(nameof(TargetIsArm));
			return IsArm;
		}

		public int GetConfiguredPointerSize()
		{
			Calls.Add(nameof(GetConfiguredPointerSize));
			return ConfiguredPointerSize;
		}
	}
}
