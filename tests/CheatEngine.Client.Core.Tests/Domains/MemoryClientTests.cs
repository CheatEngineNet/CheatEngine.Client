using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
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
		RecordingInt32Codec codec = new() { ReadValue = 1234 };
		MemoryClient client = new(new InlineDispatcher());
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
		MemoryClient client = new(new InlineDispatcher());
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
		RecordingInt32Codec codec = new() { ReadSucceeds = false };
		MemoryClient client = new(new InlineDispatcher());

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
