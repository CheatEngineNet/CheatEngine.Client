using System.Runtime.CompilerServices;

using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Core.Tests.TestSupport;

internal static class InertCoreLifetime
{
	internal static CoreLifetime Create()
	{
		return (CoreLifetime) RuntimeHelpers.GetUninitializedObject(typeof(CoreLifetime));
	}
}
