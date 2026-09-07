using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;

namespace ScreenPowerPro.ViewModels;

/// <summary>
/// Kayıtlı projeleri listeleyen, yöneten, düzenleyiciye açan ve dosya konumunu gösteren
/// Kütüphane ekranı ViewModel'ı. Electron mimarisindeki Library.tsx'in tam C# karşılığıdır.
/// </summary>
public partial class LibraryViewModel : ObservableObject
{
    private readonly ProjectService _projectService;

    [ObservableProperty]
    private ObservableCollection<ProjectInfo> _projects = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjects))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    public bool HasProjects => Projects.Count > 0;
    public bool IsEmpty => !IsLoading && Projects.Count == 0;

    // Sayfa yönlendirme olayları
    public event Action<string>? NavigateToEditor;
    public event Action? NavigateToDashboard;

    public LibraryViewModel(ProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>
    /// Disk üzerindeki kayıtlı tüm projeleri tarayarak listeyi yeniler.
    /// </summary>
    [RelayCommand]
    public async Task RefreshProjectsAsync()
    {
        IsLoading = true;
        try
        {
            var list = await Task.Run(() => _projectService.GetRecentProjects());
            Projects.Clear();
            foreach (var p in list)
            {
                Projects.Add(p);
            }
            OnPropertyChanged(nameof(HasProjects));
            OnPropertyChanged(nameof(IsEmpty));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Seçili projeyi editör sayfasına yönlendirir.
    /// </summary>
    [RelayCommand]
    public void OpenProject(ProjectInfo? project)
    {
        if (project == null || string.IsNullOrEmpty(project.FolderPath)) return;
        NavigateToEditor?.Invoke(project.FolderPath);
    }

    /// <summary>
    /// Proje klasörünü Windows Dosya Gezgini'nde açar.
    /// </summary>
    [RelayCommand]
    public void RevealInExplorer(ProjectInfo? project)
    {
        if (project == null || string.IsNullOrEmpty(project.FolderPath)) return;

        try
        {
            if (Directory.Exists(project.FolderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{project.FolderPath}\"",
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    /// <summary>
    /// Seçilen projeyi kalıcı olarak siler ve listeyi günceller.
    /// </summary>
    [RelayCommand]
    public void DeleteProject(ProjectInfo? project)
    {
        if (project == null || string.IsNullOrEmpty(project.FolderPath)) return;

        bool success = _projectService.DeleteProject(project.FolderPath);
        if (success)
        {
            Projects.Remove(project);
            OnPropertyChanged(nameof(HasProjects));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// Yeni kayıt başlatmak üzere Dashboard ana sayfasına döner.
    /// </summary>
    [RelayCommand]
    public void GoToDashboard()
    {
        NavigateToDashboard?.Invoke();
    }
}
