using System.Reflection;

namespace CheatEngine.Client.Core.Tests.TestSupport;

/// <summary>Builds values that no public constructor or factory can create, as memory tampering would.</summary>
internal static class TamperedValues
{
	/// <summary>Writes one auto-property backing field of a copy of <paramref name="value" />.</summary>
	internal static T WithBackingField<T>(T value, string property, object fieldValue)
		where T : struct
	{
		object boxed = value;
		FieldInfo field =
			typeof(T).GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic) ??
			throw new InvalidOperationException($"{typeof(T).Name}.{property} has no backing field.");
		field.SetValue(boxed, fieldValue);
		return (T) boxed;
	}
}
