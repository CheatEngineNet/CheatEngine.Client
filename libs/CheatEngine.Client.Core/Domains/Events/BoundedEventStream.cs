using System.Runtime.CompilerServices;

using CheatEngine.Client.Events;

namespace CheatEngine.Client.Core.Domains.Events;

/// <summary>
///     Provides a bounded handoff from a Cheat Engine callback to one asynchronous observation consumer. Callback
///     admission does not wait for reader consumption, but it does take a short lock and is not lock-free.
///     Reader continuations always run asynchronously.
/// </summary>
/// <typeparam name="T">The copied event type.</typeparam>
internal sealed class BoundedEventStream<T> : IAsyncEnumerable<T>, IDisposable
{
	private readonly T[] _buffer;
	private readonly object _gate = new();
	private readonly EventStreamOverflowPolicy _overflowPolicy;
	private Enumerator? _activeReader;
	private bool _completed;
	private Exception? _completionError;
	private int _count;
	private bool _disposed;
	private bool _isAdmissionOpen = true;
	private long _lostCount;
	private PendingRead? _pendingRead;
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

	/// <summary>
	///     Returns the sole active asynchronous enumerator. A second concurrent reader is rejected rather than becoming
	///     an unbounded competing subscription.
	/// </summary>
	public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			if (_activeReader is not null)
			{
				throw new InvalidOperationException(
					"A bounded Client event stream supports only one active observation reader.");
			}

			Enumerator enumerator = new(this, cancellationToken);
			_activeReader = enumerator;
			return enumerator;
		}
	}

	/// <summary>Closes admission and discards buffered events during deterministic subscription teardown.</summary>
	public void Dispose()
	{
		PendingRead? pendingRead;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_isAdmissionOpen = false;
			_completed = true;
			_activeReader = null;
			DiscardBufferedEvents();
			pendingRead = DetachPendingRead();
		}

		CompletePendingRead(pendingRead, ReadResult.End);
	}

	/// <summary>
	///     Attempts to publish a copied callback event without waiting for an asynchronous consumer. A
	///     <see langword="false" /> result means admission has closed or the selected overflow policy terminated the
	///     subscription.
	/// </summary>
	internal bool TryPublish(T value)
	{
		Exception? completionError = null;
		PendingRead? pendingRead;
		lock (_gate)
		{
			if (!_isAdmissionOpen)
			{
				return false;
			}

			pendingRead = DetachPendingRead();
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
						completionError = new InvalidOperationException(
							"The bounded callback stream overflowed and the subscription was closed.");
						pendingRead = CompleteCore(completionError, true);
						break;

					default:
						throw new InvalidOperationException(
							"The bounded callback stream has an unknown overflow policy.");
				}
			}
		}

		if (completionError is null)
		{
			CompletePendingRead(pendingRead, new ReadResult(value));
			return true;
		}

		FailPendingRead(pendingRead, completionError);
		return false;
	}

	/// <summary>Closes admission and lets readers drain the copied events already accepted by the stream.</summary>
	internal void Complete()
	{
		PendingRead? pendingRead;
		lock (_gate)
		{
			pendingRead = CompleteCore(null, false);
		}

		CompletePendingRead(pendingRead, ReadResult.End);
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
		PendingRead? pendingRead;
		lock (_gate)
		{
			pendingRead = CompleteCore(error, true);
		}

		FailPendingRead(pendingRead, error);
	}

	private ValueTask<ReadResult> ReadAsync(Enumerator reader, CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ValueTask.FromCanceled<ReadResult>(cancellationToken);
		}

		PendingRead? pendingRead;
		lock (_gate)
		{
			if (!ReferenceEquals(_activeReader, reader))
			{
				return ValueTask.FromResult(ReadResult.End);
			}

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

			pendingRead = new PendingRead(this, reader, cancellationToken);
			_pendingRead = pendingRead;
		}

		pendingRead.RegisterCancellation();
		return new ValueTask<ReadResult>(pendingRead.Task);
	}

	private void CancelPendingRead(PendingRead pendingRead, CancellationToken cancellationToken)
	{
		lock (_gate)
		{
			if (!ReferenceEquals(_pendingRead, pendingRead) || !pendingRead.TryBeginCompletion())
			{
				return;
			}

			_pendingRead = null;
			if (ReferenceEquals(_activeReader, pendingRead.Reader))
			{
				_activeReader = null;
			}
		}

		pendingRead.Cancel(cancellationToken);
	}

	private void ReleaseReader(Enumerator reader)
	{
		PendingRead? pendingRead = null;
		lock (_gate)
		{
			if (!ReferenceEquals(_activeReader, reader))
			{
				return;
			}

			_activeReader = null;
			if (ReferenceEquals(_pendingRead?.Reader, reader))
			{
				pendingRead = DetachPendingRead();
			}
		}

		CompletePendingRead(pendingRead, ReadResult.End);
	}

	private PendingRead? DetachPendingRead()
	{
		PendingRead? pendingRead = _pendingRead;
		_pendingRead = null;
		return pendingRead is not null && pendingRead.TryBeginCompletion() ? pendingRead : null;
	}

	private PendingRead? CompleteCore(Exception? error, bool clearBufferedEvents)
	{
		if (_completed)
		{
			return null;
		}

		_isAdmissionOpen = false;
		_completed = true;
		_completionError = error;

		if (clearBufferedEvents)
		{
			DiscardBufferedEvents();
		}

		return DetachPendingRead();
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

	private void DiscardBufferedEvents()
	{
		_lostCount += _count;
		ClearBuffer();
	}

	private int NextIndex(int index)
	{
		return index == _buffer.Length - 1 ? 0 : index + 1;
	}

	private static void CompletePendingRead(PendingRead? pendingRead, ReadResult result)
	{
		pendingRead?.Complete(result);
	}

	private static void FailPendingRead(PendingRead? pendingRead, Exception error)
	{
		pendingRead?.Fail(error);
	}

	private readonly record struct ReadResult(bool HasValue, T? Value)
	{
		internal ReadResult(T value)
			: this(true, value)
		{
		}

		internal static ReadResult End => new(false, default);
	}

	private sealed class PendingRead(
		BoundedEventStream<T> owner,
		Enumerator reader,
		CancellationToken cancellationToken)
	{
		private readonly CancellationToken _cancellationToken = cancellationToken;
		private readonly BoundedEventStream<T> _owner = owner;

		private readonly TaskCompletionSource<ReadResult> _source = new(
			TaskCreationOptions.RunContinuationsAsynchronously);

		private int _completionStarted;
		private CancellationTokenRegistration _registration;

		internal Enumerator Reader => reader;

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
			_registration.Unregister();
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
		private int _completed;
		private int _disposed;
		private int _moveNextInProgress;

		public T Current
		{
			get;
			private set;
		} = default!;

		public async ValueTask<bool> MoveNextAsync()
		{
			if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _completed) != 0)
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
				ReadResult result = await _owner.ReadAsync(this, _cancellationToken).ConfigureAwait(false);
				if (!result.HasValue)
				{
					Current = default!;
					CompleteReader();
					return false;
				}

				Current = result.Value!;
				return true;
			}
			catch
			{
				CompleteReader();
				throw;
			}
			finally
			{
				Volatile.Write(ref _moveNextInProgress, 0);
			}
		}

		public ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				Current = default!;
				CompleteReader();
			}

			return ValueTask.CompletedTask;
		}

		private void CompleteReader()
		{
			if (Interlocked.Exchange(ref _completed, 1) == 0)
			{
				_owner.ReleaseReader(this);
			}
		}
	}
}
