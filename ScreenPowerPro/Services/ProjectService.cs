using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

public class ProjectService
{
    private readonly SettingsService _settingsService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ProjectService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string CreateNewProjectDirectory(string? customName = null)
    {
        string baseDir = _settingsService.Current.ProjectSaveLocation;
        if (!Directory.Exists(baseDir))
        {
            Directory.CreateDirectory(baseDir);
        }

        string folderName = customName ?? $"Kayit_{DateTime.Now:yyyy_MM_dd_HHmmss}";
        string projectDir = Path.Combine(baseDir, folderName);

        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(Path.Combine(projectDir, "recording"));
        Directory.CreateDirectory(Path.Combine(projectDir, "bundle"));

        return projectDir;
    }

    public void SaveProject(string projectDir, ProjectManifest manifest)
    {
        string manifestPath = Path.Combine(projectDir, "project.myproj");
        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(manifestPath, json);
    }

    public ProjectManifest? LoadProject(string projectDir)
    {
        string manifestPath = Path.Combine(projectDir, "project.myproj");
        if (!File.Exists(manifestPath)) return null;

        string json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<ProjectManifest>(json, JsonOptions);
    }

    public void SaveMouseClicks(string projectDir, List<MouseClickEvent> clicks)
    {
        string path = Path.Combine(projectDir, "recording", "mouseclicks-0.json");
        string json = JsonSerializer.Serialize(clicks, JsonOptions);
        File.WriteAllText(path, json);
    }

    public List<MouseClickEvent> LoadMouseClicks(string projectDir)
    {
        string path = Path.Combine(projectDir, "recording", "mouseclicks-0.json");
        if (!File.Exists(path)) return new();
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<MouseClickEvent>>(json, JsonOptions) ?? new();
    }

    public void SaveMouseMoves(string projectDir, List<MouseMoveEvent> moves)
    {
        string path = Path.Combine(projectDir, "recording", "mousemoves-0.json");
        string json = JsonSerializer.Serialize(moves, JsonOptions);
        File.WriteAllText(path, json);
    }

    public void SaveKeystrokes(string projectDir, List<KeystrokeEvent> keystrokes)
    {
        string path = Path.Combine(projectDir, "recording", "keystrokes-0.json");
        string json = JsonSerializer.Serialize(keystrokes, JsonOptions);
        File.WriteAllText(path, json);
    }

    public List<ProjectInfo> GetRecentProjects()
    {
        var list = new List<ProjectInfo>();
        string baseDir = _settingsService.Current.ProjectSaveLocation;
        if (!Directory.Exists(baseDir)) return list;

        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            string manifestPath = Path.Combine(dir, "project.myproj");
            if (File.Exists(manifestPath))
            {
                try
                {
                    string json = File.ReadAllText(manifestPath);
                    var manifest = JsonSerializer.Deserialize<ProjectManifest>(json, JsonOptions);
                    if (manifest != null)
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        list.Add(new ProjectInfo
                        {
                            Name = manifest.ProjectName,
                            FolderPath = dir,
                            ManifestPath = manifestPath,
                            VideoPath = manifest.VideoPath,
                            CreatedAt = dirInfo.CreationTime,
                            DurationSeconds = manifest.Metadata.DurationSeconds,
                            ZoomCount = manifest.Timeline.ZoomEffects.Count
                        });
                    }
                }
                catch { }
            }
        }

        return list.OrderByDescending(p => p.CreatedAt).ToList();
    }
}

public class ProjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string VideoPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public double DurationSeconds { get; set; }
    public int ZoomCount { get; set; }
    public string ZoomBadgeText => $"{ZoomCount} Zoom";

    public string FormattedDuration => TimeSpan.FromSeconds(DurationSeconds).ToString(@"mm\:ss");
    public string FormattedDate => CreatedAt.ToString("dd.MM.yyyy HH:mm");
}
