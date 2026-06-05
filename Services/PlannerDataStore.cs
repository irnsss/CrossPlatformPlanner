using System;
using System.IO;
using System.Text.Json;
using CrossPlatformPlanner.Models;

namespace CrossPlatformPlanner.Services;

public static class PlannerDataStore
{
    private const string LocalFileName = "planner-data.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string LocalDataPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CrossPlatformPlanner",
            LocalFileName);

    public static PlannerDataSnapshot? LoadLocal()
    {
        if (!File.Exists(LocalDataPath))
        {
            return null;
        }

        using var stream = File.OpenRead(LocalDataPath);
        return LoadFromStream(stream);
    }

    public static void SaveLocal(PlannerDataSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(LocalDataPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(LocalDataPath);
        SaveToStream(stream, snapshot);
    }

    public static PlannerDataSnapshot? LoadFromPath(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return LoadFromStream(stream);
    }

    public static void SaveToPath(string path, PlannerDataSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        SaveToStream(stream, snapshot);
    }

    public static PlannerDataSnapshot? LoadFromStream(Stream stream)
    {
        return JsonSerializer.Deserialize<PlannerDataSnapshot>(stream, JsonOptions);
    }

    public static void SaveToStream(Stream stream, PlannerDataSnapshot snapshot)
    {
        JsonSerializer.Serialize(stream, snapshot, JsonOptions);
    }
}
