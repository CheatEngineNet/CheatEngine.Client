using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;

using CheatEngine.Client.Hosting;
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
	private static uint s_pluginId;

	internal static void RecordEnabled(uint pluginId, long epoch, int allowedTableRootCount)
	{
		Volatile.Write(ref s_allowedTableRootCount, allowedTableRootCount);
		Volatile.Write(ref s_epoch, epoch);
		Volatile.Write(ref s_pluginId, pluginId);
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
