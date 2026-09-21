namespace CheatEngine.Client.Timers;

/// <summary>Handles one copied timer tick synchronously without blocking the host callback thread.</summary>
public delegate void TimerHandler(TimerTick tick);
