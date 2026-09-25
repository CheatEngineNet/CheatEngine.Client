using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CheatEngine.Client.Hosting;

/// <summary>Adds the opt-in logging provider that writes to CheatEngine.SDK's host log.</summary>
/// <remarks>
///     <para>
///         The provider writes each admitted entry to <c>CheatEngine.SDK.Hosting.Diagnostics.HostLog</c>, whose default
///         sink is the Windows debug output of the Cheat Engine process. Nothing is added unless a plugin calls
///         <c>builder.Logging.AddCheatEngineHostLog()</c> in <see cref="CheatEngineClientPlugin.Configure" />.
///     </para>
///     <para>
///         Levels map to the four host log levels: <see cref="LogLevel.Trace" /> and <see cref="LogLevel.Debug" /> to
///         <c>Trace</c>, <see cref="LogLevel.Information" /> to <c>Information</c>, <see cref="LogLevel.Warning" /> to
///         <c>Warning</c>, and <see cref="LogLevel.Error" /> and <see cref="LogLevel.Critical" /> to <c>Error</c>;
///         <see cref="LogLevel.None" /> is never written. An entry is written only when the logging filters admit it and
///         <c>HostLog.IsEnabled</c> accepts its host level; <c>HostLog.MinimumLevel</c> is <c>Information</c> by
///         default.
///     </para>
///     <para>
///         By default an entry carries only the logger category, the event id, the message template and the exception
///         type name (audit Q46); <see cref="CheatEngineHostLogOptions.IncludeFormattedMessages" /> writes the formatted
///         message and the exception instead. The host log, its sink and its minimum level belong to CheatEngine.SDK and
///         are shared by every plugin that loads the same SDK assemblies. A sink that routes host log entries back into
///         a logger that uses this provider is contained: the host log drops the re-entrant entry instead of recursing.
///     </para>
/// </remarks>
public static class CheatEngineHostLogExtensions
{
	/// <summary>Adds the provider that writes message templates to CheatEngine.SDK's host log.</summary>
	/// <param name="builder">The logging builder, for example <see cref="CheatEnginePluginBuilder.Logging" />.</param>
	/// <returns><paramref name="builder" />.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="builder" /> is <see langword="null" />.</exception>
	/// <remarks>The provider is added once: a later call, with or without options, adds nothing.</remarks>
	public static ILoggingBuilder AddCheatEngineHostLog(this ILoggingBuilder builder)
	{
		return builder.AddCheatEngineHostLog(static _ =>
		{
		});
	}

	/// <summary>Adds the provider that writes to CheatEngine.SDK's host log, with explicit options.</summary>
	/// <param name="builder">The logging builder, for example <see cref="CheatEnginePluginBuilder.Logging" />.</param>
	/// <param name="configure">Sets the options of the provider; it runs once, during this call.</param>
	/// <returns><paramref name="builder" />.</returns>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="builder" /> or <paramref name="configure" /> is <see langword="null" />.
	/// </exception>
	/// <remarks>
	///     The provider is added once: when it is already registered, this call runs <paramref name="configure" /> but
	///     keeps the options of the first registration.
	/// </remarks>
	public static ILoggingBuilder AddCheatEngineHostLog(this ILoggingBuilder builder,
		Action<CheatEngineHostLogOptions> configure)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(configure);

		CheatEngineHostLogOptions options = new();
		configure(options);
		builder.Services.TryAddEnumerable(
			ServiceDescriptor.Singleton<ILoggerProvider>(new CheatEngineHostLogProvider(options.IncludeFormattedMessages)));
		return builder;
	}
}
