using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CrossPlatformPlanner.ViewModels;

namespace CrossPlatformPlanner.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ExportData_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Скачать файл с данными планера",
            SuggestedFileName = $"discipline-planner-{DateTime.Today:yyyy-MM-dd}",
            DefaultExtension = "json",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("JSON")
                {
                    Patterns = ["*.json"]
                }
            ]
        });

        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            viewModel.ExportToStream(stream);
        }
        catch (Exception)
        {
            viewModel.DataTransferStatus = "Не удалось скачать файл.";
        }
    }

    private async void ImportData_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Загрузить файл с данными планера",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON")
                {
                    Patterns = ["*.json"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenReadAsync();
            viewModel.ImportFromStream(stream);
        }
        catch (Exception)
        {
            viewModel.DataTransferStatus = "Не удалось загрузить файл. Проверь, что выбран правильный файл.";
        }
    }

    private void DayCell_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: DayCompletionViewModel day })
        {
            day.MarkHighlighted();
        }
    }

    private void HabitRow_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: HabitRowViewModel habit })
        {
            habit.MarkHighlighted();
        }
    }

    private void DayHeader_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: DayHeaderViewModel day })
        {
            day.MarkHighlighted();
        }
    }

    private void PlannerTable_OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectedMonth.ClearHighlight();
        }
    }
}
