using System.Collections.Generic;

namespace CrossPlatformPlanner.Models;

public sealed record DisciplineHabitSeed(string Icon, string Title);

public sealed record DisciplineDay(int Day, string Weekday);

public sealed record HabitCompletionSnapshot(string HabitTitle, string Date);

public enum PlannerThemeMode
{
    System,
    Light,
    Dark
}

public sealed record PlannerDataSnapshot
{
    public int Version { get; init; } = 1;
    public int CurrentYear { get; init; }
    public int SelectedMonth { get; init; }
    public string ThemeMode { get; init; } = PlannerThemeMode.System.ToString();
    public IReadOnlyList<DisciplineHabitSeed> Habits { get; init; } = [];
    public IReadOnlyList<HabitCompletionSnapshot> Completions { get; init; } = [];
}
