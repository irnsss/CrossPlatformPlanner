using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrossPlatformPlanner.Models;
using CrossPlatformPlanner.Services;

namespace CrossPlatformPlanner.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private const int MinSupportedYear = 1;
    private const int MaxSupportedYear = 9999;

    private static readonly DisciplineHabitSeed[] DefaultHabitTemplates =
    [
        new("💵", "4 часа фокуса"),
        new("🏋️", "зал"),
        new("📖", "10 страниц книги"),
        new("US", "английский"),
        new("🧘", "медитация"),
        new("🙏", "благодарность"),
        new("🔥", "учеба 2 часа"),
        new("🥗", "правильное питание"),
        new("🎤", "вокал"),
        new("👠", "танцы"),
        new("🧠", "нейрокачка"),
        new("✅", "проставить галочки")
    ];

    private readonly List<DisciplineHabitSeed> habitTemplates = [];
    private readonly HashSet<CompletionEntry> completedEntries = [];
    private bool suspendPersistence;

    public ObservableCollection<MonthPlanViewModel> Months { get; } = [];
    public ObservableCollection<MonthSummaryViewModel> MonthSummaries { get; } = [];
    public ObservableCollection<string> IconOptions { get; } =
    [
        "⭐", "💵", "🏋️", "📖", "US", "🧘", "🙏", "🔥", "🥗", "🎤",
        "👠", "🧠", "✅", "💼", "🎯", "🕒", "✍️", "💪", "🚶", "💧",
        "☀️", "🌙", "🧩", "📚", "🧹", "💊", "🎨", "🎧", "🏃", "🛌"
    ];

    [ObservableProperty]
    private int currentYear = DateTime.Today.Year;

    [ObservableProperty]
    private string newHabitIcon = "⭐";

    [ObservableProperty]
    private string newHabitTitle = "";

    [ObservableProperty]
    private string dataTransferStatus = "";

    [ObservableProperty]
    private MonthPlanViewModel selectedMonth = null!;

    [ObservableProperty]
    private bool isSettingsOpen;

    [ObservableProperty]
    private PlannerThemeMode selectedTheme = PlannerThemeMode.System;

    public MainWindowViewModel()
    {
        habitTemplates.AddRange(DefaultHabitTemplates);
        LoadLocalData();
        ApplyTheme(SelectedTheme);
    }

    public bool IsSystemThemeSelected => SelectedTheme == PlannerThemeMode.System;
    public bool IsLightThemeSelected => SelectedTheme == PlannerThemeMode.Light;
    public bool IsDarkThemeSelected => SelectedTheme == PlannerThemeMode.Dark;
    public bool CanMoveToPreviousYear => CurrentYear > MinSupportedYear;
    public bool CanMoveToNextYear => CurrentYear < MaxSupportedYear;
    public int YearCompleted => Months.Sum(month => month.CompletedCount);
    public int YearTarget => Months.Sum(month => month.TargetCount);
    public double YearProgress => YearTarget == 0 ? 0 : Math.Round(YearCompleted * 100.0 / YearTarget, 1);
    public string YearProgressText => $"{YearProgress:0.#}%";
    public string YearScoreText => $"{YearCompleted} / {YearTarget}";
    public string BestMonthName => MonthSummaries.Count == 0
        ? "нет данных"
        : MonthSummaries.OrderByDescending(month => month.Progress).First().Name;

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [RelayCommand]
    private void UseSystemTheme() => SelectedTheme = PlannerThemeMode.System;

    [RelayCommand]
    private void UseLightTheme() => SelectedTheme = PlannerThemeMode.Light;

    [RelayCommand]
    private void UseDarkTheme() => SelectedTheme = PlannerThemeMode.Dark;

    partial void OnSelectedThemeChanged(PlannerThemeMode value)
    {
        ApplyTheme(value);
        OnPropertyChanged(nameof(IsSystemThemeSelected));
        OnPropertyChanged(nameof(IsLightThemeSelected));
        OnPropertyChanged(nameof(IsDarkThemeSelected));

        if (!suspendPersistence)
        {
            SaveLocalData();
        }
    }

    partial void OnCurrentYearChanged(int value)
    {
        OnPropertyChanged(nameof(CanMoveToPreviousYear));
        OnPropertyChanged(nameof(CanMoveToNextYear));
    }

    [RelayCommand]
    private void PreviousYear()
    {
        if (!CanMoveToPreviousYear)
        {
            return;
        }

        CurrentYear--;
        RebuildYear(Math.Clamp(SelectedMonth?.Month ?? DateTime.Today.Month, 1, 12));
        SaveLocalData();
    }

    [RelayCommand]
    private void NextYear()
    {
        if (!CanMoveToNextYear)
        {
            return;
        }

        CurrentYear++;
        RebuildYear(Math.Clamp(SelectedMonth?.Month ?? DateTime.Today.Month, 1, 12));
        SaveLocalData();
    }

    [RelayCommand]
    private void AddHabit()
    {
        var title = NewHabitTitle.Trim();
        if (title.Length == 0)
        {
            return;
        }

        var icon = string.IsNullOrWhiteSpace(NewHabitIcon) ? "•" : NewHabitIcon.Trim();
        if (habitTemplates.Any(habit => string.Equals(habit.Title, title, StringComparison.CurrentCultureIgnoreCase)))
        {
            NewHabitTitle = "";
            DataTransferStatus = "Привычка с таким названием уже есть.";
            return;
        }

        var seed = new DisciplineHabitSeed(icon, title);
        habitTemplates.Add(seed);

        foreach (var month in Months)
        {
            month.AddHabit(seed);
        }

        NewHabitTitle = "";
        DataTransferStatus = "Привычка добавлена.";
        SaveLocalData();
        NotifyProgressChanged();
    }

    public void ExportToStream(Stream stream)
    {
        PlannerDataStore.SaveToStream(stream, CreateSnapshot());
        DataTransferStatus = "Файл с данными сохранен.";
    }

    public void ImportFromStream(Stream stream)
    {
        var snapshot = PlannerDataStore.LoadFromStream(stream)
            ?? throw new InvalidDataException("Файл импорта пустой или поврежден.");

        ApplySnapshot(snapshot);
        SaveLocalData();
        DataTransferStatus = "Данные загружены из файла.";
    }

    private void LoadLocalData()
    {
        try
        {
            var snapshot = PlannerDataStore.LoadLocal();
            if (snapshot is null)
            {
                RebuildYear(DateTime.Today.Month);
                SaveLocalData();
                return;
            }

            ApplySnapshot(snapshot);
        }
        catch
        {
            habitTemplates.Clear();
            habitTemplates.AddRange(DefaultHabitTemplates);
            completedEntries.Clear();
            RebuildYear(DateTime.Today.Month);
            DataTransferStatus = "Не удалось прочитать локальные данные. Загружен шаблон.";
        }
    }

    private PlannerDataSnapshot CreateSnapshot()
    {
        var completions = completedEntries
            .OrderBy(entry => entry.Date)
            .ThenBy(entry => entry.HabitTitle, StringComparer.CurrentCultureIgnoreCase)
            .Select(entry => new HabitCompletionSnapshot(entry.HabitTitle, entry.Date.ToString("yyyy-MM-dd")))
            .ToList();

        return new PlannerDataSnapshot
        {
            Version = 2,
            CurrentYear = CurrentYear,
            SelectedMonth = SelectedMonth?.Month ?? DateTime.Today.Month,
            ThemeMode = SelectedTheme.ToString(),
            Habits = habitTemplates.ToList(),
            Completions = completions
        };
    }

    private void ApplySnapshot(PlannerDataSnapshot snapshot)
    {
        suspendPersistence = true;
        try
        {
            habitTemplates.Clear();
            habitTemplates.AddRange(NormalizeHabits(snapshot.Habits));

            completedEntries.Clear();
            var knownHabitTitles = habitTemplates.ToDictionary(
                habit => habit.Title,
                habit => habit.Title,
                StringComparer.CurrentCultureIgnoreCase);
            foreach (var completion in snapshot.Completions ?? Array.Empty<HabitCompletionSnapshot>())
            {
                if (completion is null)
                {
                    continue;
                }

                var habitTitle = completion.HabitTitle?.Trim();
                if (string.IsNullOrWhiteSpace(habitTitle) ||
                    !knownHabitTitles.TryGetValue(habitTitle, out var normalizedHabitTitle))
                {
                    continue;
                }

                if (TryParseSnapshotDate(completion.Date, out var date))
                {
                    completedEntries.Add(new CompletionEntry(normalizedHabitTitle, date));
                }
            }

            SelectedTheme = ParseThemeMode(snapshot.ThemeMode);
            CurrentYear = NormalizeYear(snapshot.CurrentYear);
            RebuildYear(NormalizeMonth(snapshot.SelectedMonth));
        }
        finally
        {
            suspendPersistence = false;
        }
    }

    private void SelectMonth(MonthPlanViewModel? month)
    {
        if (month is null)
        {
            return;
        }

        SelectedMonth?.ClearHighlight();
        SelectedMonth = month;
        SelectedMonth.ClearHighlight();
        SaveLocalData();
    }

    internal bool HasCompletion(string habitTitle, DateTime date)
    {
        return completedEntries.Contains(new CompletionEntry(habitTitle, DateOnly.FromDateTime(date)));
    }

    internal void SetCompletion(string habitTitle, DateTime date, bool isCompleted)
    {
        var entry = new CompletionEntry(habitTitle, DateOnly.FromDateTime(date));

        if (isCompleted)
        {
            completedEntries.Add(entry);
        }
        else
        {
            completedEntries.Remove(entry);
        }

        SaveLocalData();
    }

    internal void RemoveHabit(string icon, string title)
    {
        var template = habitTemplates.FirstOrDefault(habit => habit.Icon == icon && habit.Title == title);
        if (template is null)
        {
            return;
        }

        habitTemplates.Remove(template);
        completedEntries.RemoveWhere(entry => string.Equals(entry.HabitTitle, title, StringComparison.CurrentCultureIgnoreCase));

        foreach (var month in Months)
        {
            month.RemoveHabit(icon, title);
        }

        DataTransferStatus = "Привычка удалена.";
        SaveLocalData();
        NotifySelectedMonthChanged();
        NotifyProgressChanged();
    }

    internal void NotifyProgressChanged()
    {
        foreach (var summary in MonthSummaries)
        {
            summary.NotifyProgressChanged();
        }

        OnPropertyChanged(nameof(YearCompleted));
        OnPropertyChanged(nameof(YearTarget));
        OnPropertyChanged(nameof(YearProgress));
        OnPropertyChanged(nameof(YearProgressText));
        OnPropertyChanged(nameof(YearScoreText));
        OnPropertyChanged(nameof(BestMonthName));
    }

    private void RebuildYear(int monthToSelect)
    {
        Months.Clear();
        MonthSummaries.Clear();

        foreach (var monthNumber in Enumerable.Range(1, 12))
        {
            var month = new MonthPlanViewModel(CurrentYear, monthNumber, habitTemplates, this);
            Months.Add(month);
            MonthSummaries.Add(new MonthSummaryViewModel(month, new RelayCommand<MonthPlanViewModel>(SelectMonth)));
        }

        SelectedMonth = Months[Math.Clamp(monthToSelect, 1, 12) - 1];
        NotifySelectedMonthChanged();
        NotifyProgressChanged();
    }

    private void SaveLocalData()
    {
        if (suspendPersistence)
        {
            return;
        }

        try
        {
            PlannerDataStore.SaveLocal(CreateSnapshot());
        }
        catch
        {
            DataTransferStatus = "Не удалось сохранить локальные данные.";
        }
    }

    private void NotifySelectedMonthChanged()
    {
        OnPropertyChanged(nameof(SelectedMonth));
    }

    private static PlannerThemeMode ParseThemeMode(string? value)
    {
        return Enum.TryParse<PlannerThemeMode>(value, true, out var parsed)
            ? parsed
            : PlannerThemeMode.System;
    }

    private static void ApplyTheme(PlannerThemeMode theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = theme switch
        {
            PlannerThemeMode.Light => ThemeVariant.Light,
            PlannerThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private static IReadOnlyList<DisciplineHabitSeed> NormalizeHabits(IEnumerable<DisciplineHabitSeed>? habits)
    {
        if (habits is null)
        {
            return DefaultHabitTemplates;
        }

        var normalizedHabits = new List<DisciplineHabitSeed>();
        var knownTitles = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var habit in habits)
        {
            if (habit is null)
            {
                continue;
            }

            var title = habit.Title?.Trim();
            if (string.IsNullOrWhiteSpace(title) || !knownTitles.Add(title))
            {
                continue;
            }

            var icon = string.IsNullOrWhiteSpace(habit.Icon) ? "•" : habit.Icon.Trim();
            normalizedHabits.Add(new DisciplineHabitSeed(icon, title));
        }

        return normalizedHabits.Count == 0 ? DefaultHabitTemplates : normalizedHabits;
    }

    private static int NormalizeYear(int year)
    {
        return year == 0
            ? DateTime.Today.Year
            : Math.Clamp(year, MinSupportedYear, MaxSupportedYear);
    }

    private static int NormalizeMonth(int month)
    {
        return Math.Clamp(month == 0 ? DateTime.Today.Month : month, 1, 12);
    }

    private static bool TryParseSnapshotDate(string? value, out DateOnly date)
    {
        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private readonly record struct CompletionEntry(string HabitTitle, DateOnly Date);
}

public sealed partial class MonthSummaryViewModel : ViewModelBase
{
    public MonthSummaryViewModel(MonthPlanViewModel month, ICommand selectCommand)
    {
        Month = month;
        SelectCommand = selectCommand;
    }

    public MonthPlanViewModel Month { get; }
    public ICommand SelectCommand { get; }
    public string Name => Month.Name;
    public int Completed => Month.CompletedCount;
    public int Target => Month.TargetCount;
    public double Progress => Month.Progress;
    public string ProgressText => Month.ProgressText;
    public string ScoreText => Month.ScoreText;

    public void NotifyProgressChanged()
    {
        OnPropertyChanged(nameof(Completed));
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ScoreText));
    }
}

public sealed partial class MonthPlanViewModel : ViewModelBase
{
    private static readonly string[] MonthNames =
    {
        "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
        "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"
    };

    private static readonly string[] WeekdayNames = { "вс", "пн", "вт", "ср", "чт", "пт", "сб" };
    private readonly MainWindowViewModel owner;

    public MonthPlanViewModel(
        int year,
        int month,
        IReadOnlyCollection<DisciplineHabitSeed> habits,
        MainWindowViewModel owner)
    {
        this.owner = owner;
        Year = year;
        Month = month;
        Name = MonthNames[month - 1];
        ShortTitle = $"{Name.ToLowerInvariant()} {year}";

        Days = new ObservableCollection<DayHeaderViewModel>(
            Enumerable.Range(1, DateTime.DaysInMonth(year, month))
                .Select(day =>
                {
                    var date = new DateTime(year, month, day);
                    return new DayHeaderViewModel(day, WeekdayNames[(int)date.DayOfWeek], date, this);
                }));

        Habits = new ObservableCollection<HabitRowViewModel>(
            habits.Select(habit => new HabitRowViewModel(habit, Days, this)));
    }

    public int Year { get; }
    public int Month { get; }
    public string Name { get; }
    public string ShortTitle { get; }
    public ObservableCollection<DayHeaderViewModel> Days { get; }
    public ObservableCollection<HabitRowViewModel> Habits { get; }
    public int? HighlightedDay { get; private set; }
    public string? HighlightedHabitTitle { get; private set; }
    public int CompletedCount => Habits.Sum(habit => habit.CompletedCount);
    public int TargetCount => Habits.Count * Days.Count;
    public double Progress => TargetCount == 0 ? 0 : Math.Round(CompletedCount * 100.0 / TargetCount, 1);
    public string ProgressText => $"{Progress:0.#}%";
    public string ScoreText => $"{CompletedCount} / {TargetCount}";

    public void AddHabit(DisciplineHabitSeed seed)
    {
        Habits.Add(new HabitRowViewModel(seed, Days, this));
        NotifyProgressChanged();
        OnPropertyChanged(nameof(Habits));
    }

    public void RemoveHabit(string icon, string title)
    {
        var habit = Habits.FirstOrDefault(item => item.Icon == icon && item.Title == title);
        if (habit is null)
        {
            return;
        }

        Habits.Remove(habit);
        NotifyProgressChanged();
        OnPropertyChanged(nameof(Habits));
    }

    public void RemoveHabit(HabitRowViewModel habit)
    {
        owner.RemoveHabit(habit.Icon, habit.Title);
    }

    public bool GetSavedCompletion(string habitTitle, DateTime date)
    {
        return owner.HasCompletion(habitTitle, date);
    }

    public void SetSavedCompletion(string habitTitle, DateTime date, bool isCompleted)
    {
        owner.SetCompletion(habitTitle, date, isCompleted);
    }

    public void NotifyProgressChanged()
    {
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(TargetCount));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ScoreText));
        owner.NotifyProgressChanged();
    }

    public void SetHighlight(int day, string habitTitle)
    {
        var highlightedDay = day == 0 ? (int?)null : day;
        var highlightedHabitTitle = string.IsNullOrWhiteSpace(habitTitle) ? null : habitTitle;
        if (HighlightedDay == highlightedDay && HighlightedHabitTitle == highlightedHabitTitle)
        {
            return;
        }

        HighlightedDay = highlightedDay;
        HighlightedHabitTitle = highlightedHabitTitle;

        foreach (var dayHeader in Days)
        {
            dayHeader.NotifyHighlightChanged();
        }

        foreach (var habit in Habits)
        {
            habit.NotifyHighlightChanged();
        }
    }

    public void ClearHighlight()
    {
        SetHighlight(0, "");
    }
}

public sealed class DayHeaderViewModel : ViewModelBase
{
    private readonly MonthPlanViewModel month;

    public DayHeaderViewModel(int day, string weekday, DateTime date, MonthPlanViewModel month)
    {
        Day = day;
        Weekday = weekday;
        Date = date;
        this.month = month;
    }

    public int Day { get; }
    public string Weekday { get; }
    public DateTime Date { get; }
    public bool IsPast => Date.Date < DateTime.Today;
    public bool IsColumnHighlighted => month.HighlightedDay == Day;

    public void MarkHighlighted()
    {
        month.SetHighlight(Day, month.HighlightedHabitTitle ?? "");
    }

    public void NotifyHighlightChanged()
    {
        OnPropertyChanged(nameof(IsColumnHighlighted));
    }
}

public sealed partial class HabitRowViewModel : ViewModelBase
{
    private readonly MonthPlanViewModel month;

    public HabitRowViewModel(
        DisciplineHabitSeed habit,
        IReadOnlyCollection<DayHeaderViewModel> days,
        MonthPlanViewModel month)
    {
        this.month = month;
        Icon = habit.Icon;
        Title = habit.Title;
        RemoveCommand = new RelayCommand(() => month.RemoveHabit(this));
        Completions = new ObservableCollection<DayCompletionViewModel>(
            days.Select(day => new DayCompletionViewModel(
                day.Day,
                day.Date,
                month.GetSavedCompletion(habit.Title, day.Date),
                this,
                month)));
    }

    public string Icon { get; }
    public string Title { get; }
    public ICommand RemoveCommand { get; }
    public ObservableCollection<DayCompletionViewModel> Completions { get; }
    public bool IsHighlighted => month.HighlightedHabitTitle == Title;
    public int CompletedCount => Completions.Count(day => day.IsCompleted);
    public double Progress => Completions.Count == 0 ? 0 : Math.Round(CompletedCount * 100.0 / Completions.Count, 1);
    public string ProgressText => $"{Progress:0.#}%";

    internal void NotifyCompletionChanged()
    {
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
        month.NotifyProgressChanged();
    }

    public void MarkHighlighted()
    {
        month.SetHighlight(month.HighlightedDay ?? 0, Title);
    }

    public void NotifyHighlightChanged()
    {
        OnPropertyChanged(nameof(IsHighlighted));

        foreach (var completion in Completions)
        {
            completion.NotifyHighlightChanged();
        }
    }
}

public sealed class DayCompletionViewModel : ViewModelBase
{
    private readonly HabitRowViewModel habit;
    private readonly MonthPlanViewModel month;
    private bool isCompleted;

    public DayCompletionViewModel(int day, DateTime date, bool isCompleted, HabitRowViewModel habit, MonthPlanViewModel month)
    {
        Day = day;
        Date = date;
        this.isCompleted = isCompleted;
        this.habit = habit;
        this.month = month;
    }

    public int Day { get; }
    public DateTime Date { get; }
    public bool IsEditable =>
        Date.Year == DateTime.Today.Year &&
        Date.Month == DateTime.Today.Month &&
        Date.Date >= DateTime.Today;
    public bool IsPast => !IsEditable;
    public bool IsRowHighlighted => month.HighlightedHabitTitle == habit.Title;
    public bool IsColumnHighlighted => month.HighlightedDay == Day;
    public bool IsCompleted
    {
        get => isCompleted;
        set
        {
            if (!IsEditable || isCompleted == value)
            {
                OnPropertyChanged(nameof(IsCompleted));
                return;
            }

            isCompleted = value;
            month.SetSavedCompletion(habit.Title, Date, value);
            OnPropertyChanged(nameof(IsCompleted));
            habit.NotifyCompletionChanged();
        }
    }

    public void MarkHighlighted()
    {
        month.SetHighlight(Day, habit.Title);
    }

    public void NotifyHighlightChanged()
    {
        OnPropertyChanged(nameof(IsRowHighlighted));
        OnPropertyChanged(nameof(IsColumnHighlighted));
    }
}
