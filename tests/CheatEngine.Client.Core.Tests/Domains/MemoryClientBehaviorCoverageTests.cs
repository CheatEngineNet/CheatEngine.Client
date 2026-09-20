using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class MemoryClientBehaviorCoverageTests
{
	private static readonly Address _address = new(0x405000);

	[Fact]
	public void TypedReadForwardsCancellationAndReturnsTheCodecValue()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		RecordingDispatcher dispatcher = new();
		ProbeCodec codec = new() { ReadValue = 1337 };
		MemoryClient client = new(dispatcher);

		bool succeeded = client.TryRead(new MemoryReadRequest<int>(_address, codec), out int value,
			out CheatEngineFailure failure, cancellationToken);

		Assert.True(succeeded);
		Assert.Equal(1337, value);
		Assert.Equal(default, failure);
		Assert.Equal(1, dispatcher.ActionInvocationCount);
		Assert.Equal(cancellationToken, dispatcher.LastCancellationToken);
		Assert.Equal(1, codec.ReadCount);
		Assert.Equal(_address, codec.LastReadAddress);
		Assert.NotNull(codec.LastReadContext);
	}

	[Fact]
	public void TypedWriteForwardsCancellationAndPreservesTheValueForTheCodec()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		RecordingDispatcher dispatcher = new();
		ProbeCodec codec = new();
		MemoryClient client = new(dispatcher);

		bool succeeded = client.TryWrite(new MemoryWriteRequest<int>(_address, 42, codec),
			out CheatEngineFailure failure, cancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(1, dispatcher.ActionInvocationCount);
		Assert.Equal(cancellationToken, dispatcher.LastCancellationToken);
		Assert.Equal(1, codec.WriteCount);
		Assert.Equal(_address, codec.LastWriteAddress);
		Assert.Equal(42, codec.LastWrittenValue);
		Assert.NotNull(codec.LastWriteContext);
	}

	[Fact]
	public void FailedCodecReadUsesTheFallbackFailureAndTheConvenienceMethodThrowsIt()
	{
		ProbeCodec codec = new() { ReadSucceeds = false };
		MemoryClient client = new(new RecordingDispatcher());
		MemoryReadRequest<int> request = new(_address, codec);

		bool succeeded = client.TryRead(request, out int value, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);
		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.Read(request, TestContext.Current.CancellationToken));

		Assert.False(succeeded);
		Assert.Equal(default, value);
		Assert.Equal(CheatEngineFailureKind.MemoryReadFailed, failure.Kind);
		Assert.Equal("Memory.Read", failure.Operation);
		Assert.Equal("The codec for 'Int32' rejected the target-memory read.", failure.Message);
		Assert.Equal(failure, exception.Failure);
		Assert.Equal(2, codec.ReadCount);
	}

	[Fact]
	public void FailedCodecWriteUsesTheFallbackFailureAndTheConvenienceMethodThrowsIt()
	{
		ProbeCodec codec = new() { WriteSucceeds = false };
		MemoryClient client = new(new RecordingDispatcher());
		MemoryWriteRequest<int> request = new(_address, 77, codec);

		bool succeeded = client.TryWrite(request, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);
		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.Write(request, TestContext.Current.CancellationToken));

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.MemoryWriteFailed, failure.Kind);
		Assert.Equal("Memory.Write", failure.Operation);
		Assert.Equal("The codec for 'Int32' rejected the target-memory write.", failure.Message);
		Assert.Equal(failure, exception.Failure);
		Assert.Equal(2, codec.WriteCount);
		Assert.Equal(77, codec.LastWrittenValue);
	}

	[Fact]
	public void CancelledDispatchPreventsCodecExecutionAndPreservesTheCancellationFailure()
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		CancellationToken cancellationToken = cancellation.Token;
		CancellationAwareDispatcher dispatcher = new();
		ProbeCodec codec = new();
		MemoryClient client = new(dispatcher);
		MemoryReadRequest<int> readRequest = new(_address, codec);
		MemoryWriteRequest<int> writeRequest = new(_address, 9, codec);

		bool readSucceeded = client.TryRead(readRequest, out int value, out CheatEngineFailure readFailure,
			cancellationToken);
		bool writeSucceeded = client.TryWrite(writeRequest, out CheatEngineFailure writeFailure, cancellationToken);

		Assert.False(readSucceeded);
		Assert.Equal(default, value);
		Assert.Equal(CheatEngineFailureKind.Cancelled, readFailure.Kind);
		Assert.Equal("Test.Dispatcher", readFailure.Operation);
		Assert.False(writeSucceeded);
		Assert.Equal(CheatEngineFailureKind.Cancelled, writeFailure.Kind);
		Assert.Equal("Test.Dispatcher", writeFailure.Operation);
		Assert.Equal(2, dispatcher.ActionInvocationCount);
		Assert.Equal(cancellationToken, dispatcher.LastCancellationToken);
		Assert.Equal(0, codec.ReadCount);
		Assert.Equal(0, codec.WriteCount);
	}

	[Fact]
	public void ConstructorRejectsANullDispatcher()
	{
		Assert.Throws<ArgumentNullException>(() => new MemoryClient(null!));
	}

	private sealed class ProbeCodec : IMemoryCodec<int>
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

		internal bool WriteSucceeds
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

		internal IMemoryReadContext? LastReadContext
		{
			get;
			private set;
		}

		internal IMemoryWriteContext? LastWriteContext
		{
			get;
			private set;
		}

		public bool TryRead(IMemoryReadContext context, Address address, out int value)
		{
			LastReadContext = context;
			LastReadAddress = address;
			ReadCount++;
			value = ReadValue;
			return ReadSucceeds;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value)
		{
			LastWriteContext = context;
			LastWriteAddress = address;
			LastWrittenValue = value;
			WriteCount++;
			return WriteSucceeds;
		}
	}

	private class RecordingDispatcher : ICheatEngineDispatcher
	{
		internal int ActionInvocationCount
		{
			get;
			private set;
		}

		internal CancellationToken LastCancellationToken
		{
			get;
			private set;
		}

		public bool IsMainThread => true;

		public virtual bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			RecordActionInvocation(cancellationToken);
			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			LastCancellationToken = cancellationToken;
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

		protected void RecordActionInvocation(CancellationToken cancellationToken)
		{
			ActionInvocationCount++;
			LastCancellationToken = cancellationToken;
		}
	}

	private sealed class CancellationAwareDispatcher : RecordingDispatcher
	{
		public override bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			RecordActionInvocation(cancellationToken);
			if (!cancellationToken.IsCancellationRequested)
			{
				callback();
				failure = default;
				return true;
			}

			failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Test.Dispatcher", "Cancelled.");
			return false;
		}
	}
}
