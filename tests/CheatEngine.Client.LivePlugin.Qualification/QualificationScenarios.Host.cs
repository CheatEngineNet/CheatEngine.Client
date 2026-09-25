using System.Globalization;

using CheatEngine.Client.Lua;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Tables;

using LivePlugin.Qualification.Harness;

namespace LivePlugin.Qualification;

/// <summary>
///     The worker admission (Q19), table file (Q34) and Lua marshalling (CRIT-07) scenarios.
/// </summary>
internal static partial class QualificationScenarios
{
	/// <summary>The table files the harness saves and loads below its table root carry this name prefix.</summary>
	internal const string TableFilePrefix = NamePrefix + "table";

	private static readonly Lock WorkerGate = new();
	private static WorkerProbe? _worker;

	/// <summary>
	///     Q19, in two steps so that Cheat Engine's main thread stays free while the worker waits for it: <c>start</c>
	///     starts a worker that calls <c>IProcessClient.TryRefresh</c> (the Client marshals it to the main thread) and then
	///     calls <c>Register</c> of a generated probe module directly, which CheatEngine.SDK must refuse off the main thread
	///     (<c>InvalidState</c>, <c>NotStarted</c>, nothing published). <c>result</c> reports <c>"pending":true</c> until the
	///     worker ended, then what both calls did; a probe that was registered after all is released there, on the main
	///     thread.
	/// </summary>
	internal static string WorkerAdmission(string action)
	{
		const string Function = "worker_admission";
		return action switch
		{
			"start" => RunMutating(Function, static (observation, active, _) => StartWorker(observation, active)),
			"result" => Guarded(Function, static observation => ReadWorker(observation)),
			_ => Guarded(Function, static observation =>
				observation.Boolean("ok", false).String("refusal", "UnknownAction").Complete())
		};
	}

	/// <summary>
	///     Saves the current table (Q34) as <paramref name="fileName" /> below the table root the runner configured, or,
	///     with <paramref name="outsideRoot" /> set, in the root's parent folder, where the Client must refuse it before
	///     any Cheat Engine call.
	/// </summary>
	internal static string TableSave(string fileName, long outsideRoot)
	{
		return RunMutating("table_save", (observation, active, _) =>
		{
			if (!TryTableFile(observation, fileName, outsideRoot != 0, out TrustedTableFile file))
			{
				return observation.Complete();
			}

			bool saved = active.Client.Tables.TrySaveTable(new TableSaveRequest(file), out CheatEngineFailure failure);
			observation.Boolean("outsideRoot", outsideRoot != 0).Boolean("saved", saved)
				.Boolean("fileExists", File.Exists(file.FullPath));
			if (!saved)
			{
				observation.Failure("failure", failure);
			}

			return observation.Boolean("ok", true).Complete();
		});
	}

	/// <summary>
	///     Loads <paramref name="fileName" /> from the table root (Q34). A load that reached Cheat Engine ends the validity
	///     of every record id handed out before, which <c>table_probe</c> then checks.
	/// </summary>
	internal static string TableLoad(string fileName)
	{
		return RunMutating("table_load", (observation, active, _) =>
		{
			if (!TryTableFile(observation, fileName, false, out TrustedTableFile file))
			{
				return observation.Complete();
			}

			bool loaded = active.Client.Tables.TryLoadTrustedTable(new TableLoadRequest(file), out CheatEngineFailure failure);
			observation.Boolean("loaded", loaded);
			if (!loaded)
			{
				observation.Failure("failure", failure);
			}

			return observation.Boolean("ok", true).Complete();
		});
	}

	/// <summary>
	///     CRIT-07: returns the integer CheatEngine.SDK marshalled. The driver calls it with integers and with floats around
	///     2^53, which the SDK must refuse with a Lua error instead of rounding.
	/// </summary>
	internal static string IntegerEcho(long value)
	{
		return Guarded("integer_echo", observation => observation.Boolean("ok", true)
			.String("value", value.ToString(CultureInfo.InvariantCulture)).Complete());
	}

	/// <summary>CRIT-07: returns the target address CheatEngine.SDK marshalled, with the same 2^53 rule as an integer.</summary>
	internal static string AddressEcho(nuint address)
	{
		return Guarded("address_echo", observation => observation.Boolean("ok", true)
			.String("value", ((ulong) address).ToString(CultureInfo.InvariantCulture)).Complete());
	}

	private static string StartWorker(QualificationObservation observation, QualificationSession.ActiveClient active)
	{
		lock (WorkerGate)
		{
			if (_worker is { Task.IsCompleted: false })
			{
				return observation.Boolean("ok", false).String("refusal", "WorkerRunning").Complete();
			}

			WorkerProbe probe = new(Environment.CurrentManagedThreadId);
			probe.Start(active);
			_worker = probe;
		}

		return observation.Boolean("started", true).Boolean("ok", true).Complete();
	}

	private static string ReadWorker(QualificationObservation observation)
	{
		WorkerProbe? probe;
		lock (WorkerGate)
		{
			probe = _worker;
		}

		if (probe is null)
		{
			return observation.Boolean("ok", false).String("refusal", "NoWorker").Complete();
		}

		if (!probe.Task.IsCompleted)
		{
			return observation.Boolean("pending", true).Boolean("ok", true).Complete();
		}

		return probe.Describe(observation.Boolean("pending", false)).Boolean("ok", true).Complete();
	}

	private static bool TryTableFile(QualificationObservation observation, string fileName, bool outsideRoot,
		out TrustedTableFile file)
	{
		file = default;
		if (QualificationSession.Inputs.TableRoot is not { } root)
		{
			observation.Boolean("ok", false).String("refusal", "NoTableRoot");
			return false;
		}

		if (!fileName.StartsWith(TableFilePrefix, StringComparison.Ordinal) ||
			!fileName.EndsWith(".CT", StringComparison.Ordinal) ||
			fileName.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			observation.Boolean("ok", false).String("refusal", "FileNameNotHarness");
			return false;
		}

		string? directory = outsideRoot ? Path.GetDirectoryName(root) : root;
		if (directory is null)
		{
			observation.Boolean("ok", false).String("refusal", "NoParentFolder");
			return false;
		}

		file = new TrustedTableFile(Path.Combine(directory, fileName));
		return true;
	}

	/// <summary>The Q19 worker: its two calls and the thread they ran on, written by the worker and read on the main thread.</summary>
	private sealed class WorkerProbe(int mainThreadId)
	{
		private readonly QualificationWorkerProbeModule _module = new();
		private CheatEngineFailure _refreshFailure;
		private CheatEngineFailure _registerFailure;
		private string? _registerException;
		private bool _registered;
		private bool _refreshed;
		private int _workerThreadId;

		internal Task Task
		{
			get;
			private set;
		} = Task.CompletedTask;

		internal void Start(QualificationSession.ActiveClient active)
		{
			Task = Task.Run(() =>
			{
				_workerThreadId = Environment.CurrentManagedThreadId;
				_refreshed = active.Client.Processes.TryRefresh(out ProcessSnapshot _, out _refreshFailure);
				try
				{
					_module.Register();
					_registered = true;
				}
				catch (CheatEngineClientException exception)
				{
					// A refused registration throws the exception of its failure's kind: InvalidState from a worker.
					_registerFailure = exception.Failure;
				}
				catch (Exception exception) when (exception is not OutOfMemoryException)
				{
					_registerException = exception.GetType().Name;
				}
			});
		}

		internal QualificationObservation Describe(QualificationObservation observation)
		{
			observation.Boolean("offMainThread", _workerThreadId != 0 && _workerThreadId != mainThreadId)
				.BeginObject("marshalledCall").Boolean("succeeded", _refreshed);
			if (!_refreshed)
			{
				observation.Failure("failure", _refreshFailure);
			}

			observation.EndObject().BeginObject("directRegister").Boolean("registered", _registered)
				.String("exception", _registerException);
			if (!_registered && _registerException is null)
			{
				observation.Failure("failure", _registerFailure);
			}

			if (_registered)
			{
				// Unexpected: the probe published its global from a worker. It is released here, on the main thread.
				LuaModuleReleaseOutcome released = _module.Unregister();
				observation.String("unexpectedRegistrationRelease", released.Kind.ToString());
				_registered = false;
			}

			return observation.EndObject();
		}
	}
}
