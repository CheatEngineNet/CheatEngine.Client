using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class MemoryClientTests
{
	[Fact]
	public void TypedPrimitiveReadUsesTheCallerSuppliedCodec()
	{
		RecordingInt32Codec codec = new()
		{
			ReadValue = 1234
		};
		MemoryClient client = new(new InlineDispatcher(), InertCoreLifetime.Create());
		Address address = 0x401000;

		bool succeeded = client.TryRead(new MemoryReadRequest<int>(address, codec), out int value,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(1234, value);
		Assert.Equal(default, failure);
		Assert.Equal(1, codec.ReadCount);
		Assert.Equal(address, codec.LastReadAddress);
	}

	[Fact]
	public void TypedPrimitiveWriteUsesTheCallerSuppliedCodec()
	{
		RecordingInt32Codec codec = new();
		MemoryClient client = new(new InlineDispatcher(), InertCoreLifetime.Create());
		Address address = 0x402000;

		bool succeeded = client.TryWrite(new MemoryWriteRequest<int>(address, 77, codec),
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(1, codec.WriteCount);
		Assert.Equal(address, codec.LastWriteAddress);
		Assert.Equal(77, codec.LastWrittenValue);
	}

	[Fact]
	public void TypedCodecFailureBecomesAClassifiedMemoryReadFailure()
	{
		RecordingInt32Codec codec = new()
		{
			ReadSucceeds = false
		};
		MemoryClient client = new(new InlineDispatcher(), InertCoreLifetime.Create());

		bool succeeded = client.TryRead(new MemoryReadRequest<int>(0x403000, codec), out int value,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, value);
		Assert.Equal(CheatEngineFailureKind.MemoryReadFailed, failure.Kind);
		Assert.Equal("Memory.Read", failure.Operation);
		Assert.Contains("codec", failure.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(1, codec.ReadCount);
	}

	/// <summary>
	///     A12-12: <see cref="MemoryStringReadRequest.MaximumLength" /> reaches Cheat Engine's <c>readString</c> unchanged,
	///     for both encodings, because the host does not document its unit.
	/// </summary>
	[Theory]
	[InlineData(1, false)]
	[InlineData(1, true)]
	[InlineData(255, false)]
	[InlineData(255, true)]
	[InlineData(MemoryResourceLimits.DefaultMaximumStringBytes, false)]
	[InlineData(MemoryResourceLimits.DefaultMaximumStringBytes / sizeof(char), true)]
	public void StringReadForwardsMaximumLengthUnchanged(int maximumLength, bool wideCharacter)
	{
		RecordingStringPort port = new();
		MemoryClient client = new(new InlineDispatcher(), InertCoreLifetime.Create(), port, new MemoryResourceLimits());
		Address address = 0x404000;

		bool succeeded = client.TryReadString(MemoryStringReadRequest.Create(address, maximumLength,
				wideCharacter ? MemoryStringEncoding.Utf16 : MemoryStringEncoding.Utf8),
			out string? value, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded, failure.ToString());
		Assert.Equal(RecordingStringPort.Text, value);
		(Address Address, int MaximumLength, bool WideCharacter) call = Assert.Single(port.StringReads);
		Assert.Equal(address, call.Address);
		Assert.Equal(maximumLength, call.MaximumLength);
		Assert.Equal(wideCharacter, call.WideCharacter);
	}

	[Theory]
	[InlineData(8, false, true)]
	[InlineData(9, false, false)]
	[InlineData(4, true, true)]
	[InlineData(5, true, false)]
	public void StringReadAdmissionChargesUtf16TwiceTheForwardedLength(int maximumLength, bool wideCharacter,
		bool admitted)
	{
		RecordingStringPort port = new();
		MemoryClient client = new(new InlineDispatcher(), InertCoreLifetime.Create(), port,
			new MemoryResourceLimits(64, 64, 8, 64, 1));

		bool succeeded = client.TryReadString(MemoryStringReadRequest.Create(0x405000, maximumLength,
				wideCharacter ? MemoryStringEncoding.Utf16 : MemoryStringEncoding.Utf8),
			out _, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.Equal(admitted, succeeded);
		if (admitted)
		{
			Assert.Equal(maximumLength, Assert.Single(port.StringReads).MaximumLength);
		}
		else
		{
			Assert.Empty(port.StringReads);
			Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
			Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		}
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void DetailedByteReadReportsTheConfirmedPrefixOfAPartialRead()
	{
		// Cheat Engine returned three of the eight requested bytes: the prefix is reported, never confused with a read
		// that copied nothing, and the Try form still publishes nothing.
		ByteReadPort port = new([0x10, 0x20, 0x30], MemoryAccessFailure.PartialRead);
		MemoryClient client = CreateClient(port);
		MemoryBytesReadRequest request = new(0x406000, 8);

		MemoryBytesReadOutcome outcome = client.ReadBytesDetailed(request, TestContext.Current.CancellationToken);
		bool succeeded = client.TryReadBytes(request, out ImmutableArray<byte> bytes, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(outcome.IsSuccess);
		Assert.False(outcome.IsComplete);
		Assert.Equal(8, outcome.RequestedLength);
		Assert.Equal(3, outcome.ConfirmedLength);
		Assert.Equal([0x10, 0x20, 0x30], outcome.Bytes);
		Assert.Equal(CheatEngineFailureKind.MemoryReadFailed, outcome.Failure!.Value.Kind);
		Assert.Equal("Memory.ReadBytes", outcome.Failure.Value.Operation);
		Assert.False(succeeded);
		Assert.True(bytes.IsEmpty);
		Assert.Equal(outcome.Failure.Value.Kind, failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void DetailedByteReadKeepsTheVerifiedPrefixOfAMalformedResultWithItsOwnKind()
	{
		ByteReadPort port = new([0x01, 0x02], MemoryAccessFailure.InvalidResult);

		MemoryBytesReadOutcome outcome = CreateClient(port).ReadBytesDetailed(new MemoryBytesReadRequest(0x406000, 4),
			TestContext.Current.CancellationToken);

		Assert.Equal([0x01, 0x02], outcome.Bytes);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, outcome.Failure!.Value.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void DetailedByteReadReturnsEveryRequestedByteOnSuccess()
	{
		ByteReadPort port = new([0x0A, 0x0B, 0x0C, 0x0D], MemoryAccessFailure.None);

		MemoryBytesReadOutcome outcome = CreateClient(port).ReadBytesDetailed(new MemoryBytesReadRequest(0x406000, 4),
			TestContext.Current.CancellationToken);

		Assert.True(outcome.IsSuccess);
		Assert.True(outcome.IsComplete);
		Assert.Null(outcome.Failure);
		Assert.Equal(4, outcome.ConfirmedLength);
		Assert.Equal([0x0A, 0x0B, 0x0C, 0x0D], outcome.Bytes);
	}

	[Fact]
	public void DetailedByteReadOverTheBudgetIsRefusedBeforeDispatchWithAnEmptyPrefix()
	{
		ByteReadPort port = new([0x01], MemoryAccessFailure.None);
		MemoryClient client = new(new InlineDispatcher(), InertCoreLifetime.Create(), port,
			new MemoryResourceLimits(2, 64, 64, 64, 1));

		MemoryBytesReadOutcome outcome = client.ReadBytesDetailed(new MemoryBytesReadRequest(0x406000, 3),
			TestContext.Current.CancellationToken);

		Assert.Equal(0, outcome.ConfirmedLength);
		Assert.Equal(3, outcome.RequestedLength);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, outcome.Failure!.Value.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, outcome.Failure.Value.HostEffect);
		Assert.Equal(0, port.Reads);
	}

	[Fact]
	[Trait("Qualification", "Q20")]
	public void CodecContextReportsAPartialReadAsAFailureAndKeepsNoPartialBytes()
	{
		ByteReadPort port = new([0x7F, 0x7F], MemoryAccessFailure.PartialRead);
		PrefixCodec codec = new();

		bool succeeded = CreateClient(port).TryRead(new MemoryReadRequest<int>(0x406000, codec), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.MemoryReadFailed, failure.Kind);
		Assert.Equal([0, 0, 0, 0], codec.Buffer);
	}

	private static MemoryClient CreateClient(IMemoryCodecContextPort port)
	{
		return new MemoryClient(new InlineDispatcher(), InertCoreLifetime.Create(), port, new MemoryResourceLimits());
	}

	/// <summary>
	///     Behaves like the counted <c>TargetMemory.TryReadBytes</c>: it copies its bytes as the verified prefix and reports
	///     its failure, or success when it holds every requested byte.
	/// </summary>
	private sealed class ByteReadPort(byte[] bytes, MemoryAccessFailure failure)
		: TargetObservationDouble, IMemoryCodecContextPort
	{
		internal int Reads
		{
			get;
			private set;
		}

		public bool TryReadBytes(Address address, Span<byte> destination, out int written,
			out MemoryAccessFailure hostFailure)
		{
			Reads++;
			written = Math.Min(bytes.Length, destination.Length);
			bytes.AsSpan(0, written).CopyTo(destination);
			hostFailure = failure;
			return failure == MemoryAccessFailure.None && written == destination.Length;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out MemoryAccessFailure hostFailure)
		{
			throw new InvalidOperationException("A byte read must not write.");
		}
	}

	/// <summary>Reads four bytes through its context and keeps the buffer it passed.</summary>
	private sealed class PrefixCodec : IMemoryCodec<int>
	{
		internal byte[] Buffer
		{
			get;
		} = new byte[sizeof(int)];

		public bool TryRead(IMemoryReadContext context, Address address, out int value)
		{
			value = 0;
			return context.TryReadBytes(address, Buffer);
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value)
		{
			return false;
		}
	}

	private sealed class RecordingStringPort : TargetObservationDouble, IMemoryCodecContextPort
	{
		internal const string Text = "copied";

		internal List<(Address Address, int MaximumLength, bool WideCharacter)> StringReads
		{
			get;
		} = [];

		public bool TryReadBytes(Address address, Span<byte> destination, out int written,
			out MemoryAccessFailure failure)
		{
			throw new InvalidOperationException("A string read must not use the byte port.");
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out MemoryAccessFailure failure)
		{
			throw new InvalidOperationException("A string read must not use the byte port.");
		}

		public bool TryReadString(Address address, int maximumLength, bool wideCharacter, out string? value,
			out MemoryAccessFailure failure)
		{
			StringReads.Add((address, maximumLength, wideCharacter));
			value = Text;
			failure = MemoryAccessFailure.None;
			return true;
		}
	}

	private sealed class RecordingInt32Codec : IMemoryCodec<int>
	{
		internal int ReadValue
		{
			get;
			init;
		}

		internal bool ReadSucceeds
		{
			get;
			init;
		} = true;

		internal int ReadCount
		{
			get;
			private set;
		}

		internal int WriteCount
		{
			get;
			private set;
		}

		internal Address LastReadAddress
		{
			get;
			private set;
		}

		internal Address LastWriteAddress
		{
			get;
			private set;
		}

		internal int LastWrittenValue
		{
			get;
			private set;
		}

		public bool TryRead(IMemoryReadContext context, Address address, out int value)
		{
			Assert.NotNull(context);
			ReadCount++;
			LastReadAddress = address;
			value = ReadValue;
			return ReadSucceeds;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value)
		{
			Assert.NotNull(context);
			WriteCount++;
			LastWriteAddress = address;
			LastWrittenValue = value;
			return true;
		}
	}

	private sealed class InlineDispatcher : ICheatEngineDispatcher
	{
		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Test.Dispatcher", "Cancelled.");
				return false;
			}

			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				result = default;
				failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Test.Dispatcher", "Cancelled.");
				return false;
			}

			result = callback();
			failure = default;
			return true;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			if (!TryInvoke(callback, out CheatEngineFailure failure, cancellationToken))
			{
				failure.Throw(cancellationToken);
			}
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out T? result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw(cancellationToken);
			return default!;
		}
	}
}
