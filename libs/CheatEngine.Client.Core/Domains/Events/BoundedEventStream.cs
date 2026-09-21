using System.Runtime.CompilerServices;

using CheatEngine.Client.Events;

namespace CheatEngine.Client.Core.Domains.Events;

/// <summary>
///     Provides a bounded, non-blocking handoff from a Cheat Engine callback to an asynchronous consumer. Callback
///     admission is synchronous and never waits for a reader; reader continuations always run asynchronously.
/// </summary>
/// <typeparam name="T">The copied event type.</typeparam>
internal sealed class BoundedEventStream<T> : IAsyncEnumerable<T>, IDisposable
{
	private readonly T[] _buffer;
	private readonly object _gate = new();
	private readonly EventStreamOverflowPolicy _overflowPolicy;
	private readonly LinkedList<PendingRead> _pendingReads = [];
	private bool _completed;
	private Exception? _completionError;
	private int _count;
	private bool _disposed;
	private bool _isAdmissionOpen = true;
	private long _lostCount;
	private int _readIndex;
	private int _writeIndex;

	/// <summary>Initializes a stream with the supplied bounded-buffer policy.</summary>
	internal BoundedEventStream(EventStreamOptions options)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Capacity);
		if (!Enum.IsDefined(options.OverflowPolicy))
		{
			throw new ArgumentOutOfRangeException(nameof(options));
		}

		_buffer = new T[options.Capacity];
		_overflowPolicy = options.OverflowPolicy;
	}

	/// <summary>Gets the number of events intentionally not delivered because the bounded buffer was full.</summary>
	internal long LostCount
	{
		get
		{
			lock (_gate)
			{
				return _lostCount;
			}
		}
	}

	/// <summary>Gets whether callbacks can still publish into the stream.</summary>
	internal bool IsAdmissionOpen
	{
		get
		{
			lock (_gate)
			{
				return _isAdmissionOpen;
			}
		}
	}

	/// <summary>Gets whether no further events will be admitted.</summary>
	internal bool IsCompleted
	{
		get
		{
			lock (_gate)
			{
				return _completed;
			}
		}
	}

	/// <summary>Returns an asynchronous enumerator over copied events.</summary>
	public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
	{
		return new Enumerator(this, cancellationToken);
	}

	/// <summary>Closes admission and discards buffered events during deterministic subscription teardown.</summary>
	public void Dispose()
	{
		List<PendingRead>? pendingReads = null;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_isAdmissionOpen = false;
			_completed = true;
			ClearBuffer();
			pendingReads = DetachAllPendingReads();
		}

		CompletePendingReads(pendingReads, ReadResult.End);
	}

	/// <summary>
	///     Attempts to publish a copied callback event without waiting for an asynchronous consumer. A
	///     <see langword="false" />
	///     result means admission has closed or the selected overflow policy terminated the subscription.
	/// </summary>
	internal bool TryPublish(T value)
	{
		PendingRead? pendingRead = null;
		lock (_gate)
		{
			if (!_isAdmissionOpen)
			{
				return false;
			}

			pendingRead = DetachNextPendingRead();
			if (pendingRead is null)
			{
				if (_count < _buffer.Length)
				{
					Enqueue(value);
					return true;
				}

				switch (_overflowPolicy)
				{
					case EventStreamOverflowPolicy.DropOldest:
						_buffer[_writeIndex] = value;
						_writeIndex = NextIndex(_writeIndex);
						_readIndex = _writeIndex;
						_lostCount++;
						return true;

					case EventStreamOverflowPolicy.DropNewest:
						_lostCount++;
						return true;

					case EventStreamOverflowPolicy.FailSubscription:
						_lostCount++;
						CompleteCore(new InvalidOperationException(
							"The bounded callback stream overflowed and the subscription was closed."), true);
						return false;

					default:
						throw new InvalidOperationException(
							"The bounded callback stream has an unknown overflow policy.");
				}
			}
		}

		pendingRead.Complete(new ReadResult(value));
		return true;
	}

	/// <summary>Closes admission and lets readers drain the copied events already accepted by the stream.</summary>
	internal void Complete()
	{
		lock (_gate)
		{
			CompleteCore(null, false);
		}
	}

	/// <summary>Closes callback admission without completing existing readers until the host callback is neutralized.</summary>
	internal void CloseAdmission()
	{
		lock (_gate)
		{
			_isAdmissionOpen = false;
		}
	}

	/// <summary>Closes admission and completes all consumers with the supplied failure.</summary>
	internal void Complete(Exception error)
	{
		ArgumentNullException.ThrowIfNull(error);
		lock (_gate)
		{
			CompleteCore(error, true);
		}
	}

	private ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ValueTask.FromCanceled<ReadResult>(cancellationToken);
		}

		PendingRead? pendingRead = null;
		lock (_gate)
		{
			if (_count != 0)
			{
				return ValueTask.FromResult(new ReadResult(Dequeue()));
			}

			if (_completionError is not null)
			{
				return ValueTask.FromException<ReadResult>(_completionError);
			}

			if (_completed)
			{
				return ValueTask.FromResult(ReadResult.End);
			}

			pendingRead = new PendingRead(this, cancellationToken);
			pendingRead.Node = _pendingReads.AddLast(pendingRead);
		}

		pendingRead.RegisterCancellation();
		return new ValueTask<ReadResult>(pendingRead.Task);
	}

	private void CancelPendingRead(PendingRead pendingRead, CancellationToken cancellationToken)
	{
		lock (_gate)
		{
			if (pendingRead.Node is null || !pendingRead.TryBeginCompletion())
			{
				return;
			}

			_pendingReads.Remove(pendingRead.Node);
			pendingRead.Node = null;
		}

		pendingRead.Cancel(cancellationToken);
	}

	private PendingRead? DetachNextPendingRead()
	{
		while (_pendingReads.First is LinkedListNode<PendingRead> node)
		{
			PendingRead pendingRead = node.Value;
			_pendingReads.Remove(node);
			pendingRead.Node = null;
			if (pendingRead.TryBeginCompletion())
			{
				return pendingRead;
			}
		}

		return null;
	}

	private List<PendingRead>? DetachAllPendingReads()
	{
		if (_pendingReads.Count == 0)
		{
			return null;
		}

		List<PendingRead> detached = new(_pendingReads.Count);
		while (_pendingReads.First is LinkedListNode<PendingRead> node)
		{
			PendingRead pendingRead = node.Value;
			_pendingReads.RemoveFirst();
			pendingRead.Node = null;
			if (pendingRead.TryBeginCompletion())
			{
				detached.Add(pendingRead);
			}
		}

		return detached;
	}

	private void CompleteCore(Exception? error, bool clearBufferedEvents)
	{
		if (_completed)
		{
			return;
		}

		_isAdmissionOpen = false;
		_completed = true;
		_completionError = error;
		if (clearBufferedEvents)
		{
			ClearBuffer();
		}

		List<PendingRead>? pendingReads = DetachAllPendingReads();
		if (pendingReads is null)
		{
			return;
		}

		if (error is null)
		{
			CompletePendingReads(pendingReads, ReadResult.End);
			return;
		}

		CompletePendingReads(pendingReads, error);
	}

	private void Enqueue(T value)
	{
		_buffer[_writeIndex] = value;
		_writeIndex = NextIndex(_writeIndex);
		_count++;
	}

	private T Dequeue()
	{
		T value = _buffer[_readIndex];
		_buffer[_readIndex] = default!;
		_readIndex = NextIndex(_readIndex);
		_count--;
		return value;
	}

	private void ClearBuffer()
	{
		if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
		{
			Array.Clear(_buffer);
		}

		_readIndex = 0;
		_writeIndex = 0;
		_count = 0;
	}

	private int NextIndex(int index)
	{
		return index == _buffer.Length - 1 ? 0 : index + 1;
	}

	private static void CompletePendingReads(IEnumerable<PendingRead>? pendingReads, ReadResult result)
	{
		if (pendingReads is null)
		{
			return;
		}

		foreach (PendingRead pendingRead in pendingReads)
		{
			pendingRead.Complete(result);
		}
	}

	private static void CompletePendingReads(IEnumerable<PendingRead> pendingReads, Exception error)
	{
		foreach (PendingRead pendingRead in pendingReads)
		{
			pendingRead.Fail(error);
		}
	}

	private readonly record struct ReadResult(bool HasValue, T? Value)
	{
		internal ReadResult(T value)
			: this(true, value)
		{
		}

		internal static ReadResult End => new(false, default);
	}

	private sealed class PendingRead(BoundedEventStream<T> owner, CancellationToken cancellationToken)
	{
		private readonly CancellationToken _cancellationToken = cancellationToken;
		private readonly BoundedEventStream<T> _owner = owner;

		private readonly TaskCompletionSource<ReadResult> _source = new(
			TaskCreationOptions.RunContinuationsAsynchronously);

		private int _completionStarted;
		private CancellationTokenRegistration _registration;

		internal LinkedListNode<PendingRead>? Node
		{
			get;
			set;
		}

		internal Task<ReadResult> Task => _source.Task;

		internal bool TryBeginCompletion()
		{
			return Interlocked.CompareExchange(ref _completionStarted, 1, 0) == 0;
		}

		internal void RegisterCancellation()
		{
			if (!_cancellationToken.CanBeCanceled)
			{
				return;
			}

			_registration = _cancellationToken.UnsafeRegister(static state =>
			{
				PendingRead pendingRead = (PendingRead) state!;
				pendingRead._owner.CancelPendingRead(pendingRead, pendingRead._cancellationToken);
			}, this);

			if (Volatile.Read(ref _completionStarted) != 0)
			{
				_registration.Unregister();
			}
		}

		internal void Complete(ReadResult result)
		{
			_registration.Unregister();
			_source.TrySetResult(result);
		}

		internal void Cancel(CancellationToken cancellationToken)
		{
			_source.TrySetCanceled(cancellationToken);
		}

		internal void Fail(Exception error)
		{
			_registration.Unregister();
			_source.TrySetException(error);
		}
	}

	private sealed class Enumerator(BoundedEventStream<T> owner, CancellationToken cancellationToken)
		: IAsyncEnumerator<T>
	{
		private readonly CancellationToken _cancellationToken = cancellationToken;
		private readonly BoundedEventStream<T> _owner = owner;
		private int _disposed;
		private int _moveNextInProgress;

		public T Current
		{
			get;
			private set;
		} = default!;

		public async ValueTask<bool> MoveNextAsync()
		{
			if (Volatile.Read(ref _disposed) != 0)
			{
				return false;
			}

			if (Interlocked.Exchange(ref _moveNextInProgress, 1) != 0)
			{
				throw new InvalidOperationException(
					"Concurrent MoveNextAsync calls are not supported for one event-stream enumerator.");
			}

			try
			{
				ReadResult result = await _owner.ReadAsync(_cancellationToken).ConfigureAwait(false);
				if (!result.HasValue)
				{
					Current = default!;
					return false;
				}

				Current = result.Value!;
				return true;
			}
			finally
			{
				Volatile.Write(ref _moveNextInProgress, 0);
			}
		}

		public ValueTask DisposeAsync()
		{
			Interlocked.Exchange(ref _disposed, 1);
			return ValueTask.CompletedTask;
		}
	}
}
