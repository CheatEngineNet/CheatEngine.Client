using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;

using CheatEngine.Client;
using CheatEngine.Client.Allocations;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Hosting.Bootstrap;

namespace LivePlugin.Coexistence;

/// <summary>
///     Records copied identity facts from one fixture assembly. It observes the loader; it never creates or selects an
///     <see cref="AssemblyLoadContext" /> and does not coordinate Cheat Engine or Lua state.
/// </summary>
internal static class CoexistenceDiagnostics
{
	private static int s_allowedTableRootCount;
	private static long s_epoch;
	private static ITargetMemoryLease? s_retainedOwner;
	private static uint s_pluginId;
	private static ICheatEngineClient? s_activeClient;

	internal static void RecordEnabled(uint pluginId, ICheatEngineClient client, int allowedTableRootCount)
	{
		ArgumentNullException.ThrowIfNull(client);
		Volatile.Write(ref s_allowedTableRootCount, allowedTableRootCount);
		Volatile.Write(ref s_epoch, client.Epoch);
		Volatile.Write(ref s_pluginId, pluginId);
		Interlocked.Exchange(ref s_activeClient, client);
	}

	internal static void RecordDisabling()
	{
		// A fixture-only Lua callback must not retain an expired Client activation. The normal Client owner registry
		// remains responsible for target-change and activation cleanup; this releases a still-retained probe lease early.
		Interlocked.Exchange(ref s_activeClient, null);
		ITargetMemoryLease? owner = Interlocked.Exchange(ref s_retainedOwner, null);
		owner?.Dispose();
	}

	internal static string GetIdentity(string pluginLabel, Assembly pluginAssembly)
	{
		Assembly clientHostingAssembly = typeof(CheatEngineClientPlugin).Assembly;
		Assembly sdkHostingAssembly = typeof(PluginHost).Assembly;
		AssemblyLoadContext? pluginLoadContext = AssemblyLoadContext.GetLoadContext(pluginAssembly);
		AssemblyLoadContext? clientHostingLoadContext = AssemblyLoadContext.GetLoadContext(clientHostingAssembly);
		AssemblyLoadContext? sdkHostingLoadContext = AssemblyLoadContext.GetLoadContext(sdkHostingAssembly);

		return string.Create(
			CultureInfo.InvariantCulture,
			$"Plugin={pluginLabel}; PluginAssembly={pluginAssembly.FullName}; PluginMvid={pluginAssembly.ManifestModule.ModuleVersionId}; " +
			$"ClientHostingAssembly={clientHostingAssembly.FullName}; ClientHostingMvid={clientHostingAssembly.ManifestModule.ModuleVersionId}; " +
			$"SdkHostingAssembly={sdkHostingAssembly.FullName}; SdkHostingMvid={sdkHostingAssembly.ManifestModule.ModuleVersionId}; " +
			$"PluginALC={Describe(pluginLoadContext)}; ClientHostingALC={Describe(clientHostingLoadContext)}; " +
			$"SdkHostingALC={Describe(sdkHostingLoadContext)}; SameClientHostingALC={ReferenceEquals(pluginLoadContext, clientHostingLoadContext)}; " +
			$"SameSdkHostingALC={ReferenceEquals(pluginLoadContext, sdkHostingLoadContext)}; PluginId={Volatile.Read(ref s_pluginId)}; ClientEpoch={Volatile.Read(ref s_epoch)}; " +
			$"AllowedTableRootCount={Volatile.Read(ref s_allowedTableRootCount)}");
	}

	/// <summary>Refreshes and reports the current target without selecting or otherwise mutating it.</summary>
	internal static string ObserveTarget()
	{
		ICheatEngineClient? client = Volatile.Read(ref s_activeClient);
		if (client is null)
		{
			return "Target=Inactive";
		}

		return client.Processes.TryRefresh(out ProcessSnapshot snapshot, out CheatEngineFailure failure, client.Stopping)
			? string.Create(
				CultureInfo.InvariantCulture,
				$"Target=Selected; ProcessId={snapshot.Id.Value}; SelectionEpoch={snapshot.SelectionEpoch}; Architecture={snapshot.TargetArchitecture}")
			: DescribeFailure("Target", failure);
	}

	/// <summary>
	///     Retains one intentionally tiny allocation only when the exact Client/SDK tuple exposes a qualified allocation
	///     owner. The current released Client tuple reports capability unavailable; that outcome is an expected blocker,
	///     never a passing retained-owner result.
	/// </summary>
	internal static string RetainOwner()
	{
		ICheatEngineClient? client = Volatile.Read(ref s_activeClient);
		if (client is null)
		{
			return "Owner=Inactive";
		}

		ITargetMemoryLease? prior = Volatile.Read(ref s_retainedOwner);
		if (prior is not null)
		{
			return DescribeOwner("Owner=AlreadyRetained", prior);
		}

		if (!client.Allocations.TryAllocate(new TargetAllocationRequest(16), out ITargetMemoryLease? owner,
				out CheatEngineFailure failure, client.Stopping))
		{
			return DescribeFailure("Owner", failure);
		}

		ITargetMemoryLease? retainedOwner = Interlocked.CompareExchange(ref s_retainedOwner, owner, null);
		if (retainedOwner is null)
		{
			return DescribeOwner("Owner=Retained", owner);
		}

		// Generated Lua calls are normally serialized by the host. Keep the race deterministic if a future host invokes
		// this fixture concurrently: the extra owner is released, rather than left associated with an unknown target.
		owner.Dispose();
		return DescribeOwner("Owner=AlreadyRetained", retainedOwner);
	}

	/// <summary>Reports the retained owner's Client-visible lifecycle state without invoking Cheat Engine.</summary>
	internal static string GetOwnerState()
	{
		ITargetMemoryLease? owner = Volatile.Read(ref s_retainedOwner);
		return owner is null ? "Owner=None" : DescribeOwner("Owner=Retained", owner);
	}

	/// <summary>Releases the retained probe owner once, if one exists.</summary>
	internal static string ReleaseOwner()
	{
		ITargetMemoryLease? owner = Interlocked.Exchange(ref s_retainedOwner, null);
		if (owner is null)
		{
			return "Owner=None";
		}

		owner.Dispose();
		return "Owner=ReleasedByFixture";
	}

	private static string DescribeFailure(string prefix, CheatEngineFailure failure)
	{
		return string.Create(
			CultureInfo.InvariantCulture,
			$"{prefix}=Failure; Kind={failure.Kind}; Operation={failure.Operation}; Message={failure.Message}");
	}

	private static string DescribeOwner(string prefix, ITargetMemoryLease owner)
	{
		return string.Create(
			CultureInfo.InvariantCulture,
			$"{prefix}; Released={owner.IsReleased}; SelectionEpoch={owner.SelectionEpoch}; Size={owner.Size}");
	}

	private static string Describe(AssemblyLoadContext? loadContext)
	{
		if (loadContext is null)
		{
			return "<none>";
		}

		return string.Create(
			CultureInfo.InvariantCulture,
			$"Name={loadContext.Name ?? "<unnamed>"}, Collectible={loadContext.IsCollectible}");
	}
}
