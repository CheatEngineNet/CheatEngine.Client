using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
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

		bool succeeded = client.TryReadString(new MemoryStringReadRequest(address, maximumLength, wideCharacter),
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

		bool succeeded = client.TryReadString(new MemoryStringReadRequest(0x405000, maximumLength, wideCharacter),
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

	private sealed class RecordingStringPort : IMemoryCodecContextPort
	{
		internal const string Text = "copied";

		internal List<(Address Address, int MaximumLength, bool WideCharacter)> StringReads
		{
			get;
		} = [];

		public long GetOpenedProcessId()
		{
			return 42;
		}

		public bool TargetIs64Bit()
		{
			return true;
		}

		public bool TargetIsX86()
		{
			return true;
		}

		public bool TargetIsArm()
		{
			return false;
		}

		public int GetConfiguredPointerSize()
		{
			return sizeof(ulong);
		}

		public bool TryReadBytes(Address address, Span<byte> destination, out string? failure)
		{
			throw new InvalidOperationException("A string read must not use the byte port.");
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out string? failure)
		{
			throw new InvalidOperationException("A string read must not use the byte port.");
		}

		public bool TryReadString(Address address, int maximumLength, bool wideCharacter, out string? value,
			out string? failure)
		{
			StringReads.Add((address, maximumLength, wideCharacter));
			value = Text;
			failure = null;
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
				failure.Throw();
			}
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out T? result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw();
			return default!;
		}
	}
}
