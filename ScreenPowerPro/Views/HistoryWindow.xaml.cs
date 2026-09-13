using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScreenPowerPro.Models;
using ScreenPowerPro.ViewModels;
using System;
using System.Linq;

namespace ScreenPowerPro.Views
{
    public sealed partial class HistoryWindow : Window
    {
        public DashboardViewModel ViewModel { get; }

        public HistoryWindow(DashboardViewModel viewModel)
        {
            this.InitializeComponent();
            ViewModel = viewModel;

            // Setup Window
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            
            AppWindow.Resize(new Windows.Graphics.SizeInt32(640, 520));
            // Center the window
            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var workArea = displayArea.WorkArea;
                var x = (workArea.Width - 640) / 2;
                var y = (workArea.Height - 520) / 2;
                AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
            }

            // Initial load
            ViewModel.RefreshRecentProjects();
            RefreshHistoryProjectsList();
        }

        private void RefreshHistoryProjectsList()
        {
            var projects = ViewModel.RecentProjects;
            if (projects == null || !projects.Any())
            {
                TbHistoryEmpty.Visibility = Visibility.Visible;
                HistoryProjectsList.ItemsSource = null;
            }
            else
            {
                TbHistoryEmpty.Visibility = Visibility.Collapsed;
                HistoryProjectsList.ItemsSource = projects;
            }
        }

        private void OnTabProjectHistoryClicked(object sender, RoutedEventArgs e)
        {
            BtnTabProjectHistory.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 30, 58, 138));
            BtnTabProjectHistory.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 59, 130, 246));
            BtnTabProjectHistory.BorderThickness = new Thickness(1);

            BtnTabSharingHistory.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 26, 27, 36));
            BtnTabSharingHistory.BorderThickness = new Thickness(0);

            TbHistoryEmpty.Text = "Kayıtlı proje bulunamadı.";
            RefreshHistoryProjectsList();
        }

        private void OnTabSharingHistoryClicked(object sender, RoutedEventArgs e)
        {
            BtnTabSharingHistory.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 30, 58, 138));
            BtnTabSharingHistory.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 59, 130, 246));
            BtnTabSharingHistory.BorderThickness = new Thickness(1);

            BtnTabProjectHistory.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 26, 27, 36));
            BtnTabProjectHistory.BorderThickness = new Thickness(0);

            TbHistoryEmpty.Text = "Paylaşılan proje bulunamadı.";
            TbHistoryEmpty.Visibility = Visibility.Visible;
            HistoryProjectsList.ItemsSource = null;
        }

        private void OnOpenProjectFromHistory(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProjectInfo project)
            {
                this.Close();
                MainWindow.CurrentInstance?.NavigateToEditor(project.FolderPath);
            }
        }

        private async void OnRenameProjectClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProjectInfo project)
            {
                var textBox = new TextBox
                {
                    Text = project.Name,
                    Width = 300,
                    AcceptsReturn = false
                };

                var dialog = new ContentDialog
                {
                    Title = "Projeyi Yeniden Adlandır",
                    Content = textBox,
                    PrimaryButtonText = "Kaydet",
                    CloseButtonText = "İptal",
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    string newName = textBox.Text.Trim();
                    if (!string.IsNullOrEmpty(newName) && newName != project.Name)
                    {
                        ViewModel.RenameProject(project.FolderPath, newName);
                        RefreshHistoryProjectsList();
                    }
                }
            }
        }

        private async void OnDeleteProject(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProjectInfo project)
            {
                var dialog = new ContentDialog
                {
                    Title = "Projeyi Sil",
                    Content = $"'{project.Name}' adlı projeyi ve tüm dosyalarını silmek istediğinizden emin misiniz? Bu işlem geri alınamaz.",
                    PrimaryButtonText = "Sil",
                    CloseButtonText = "İptal",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    ViewModel.DeleteProject(project.FolderPath);
                    RefreshHistoryProjectsList();
                }
            }
        }

        private async void OnOpenProjectFolder(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProjectInfo project)
            {
                try
                {
                    var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(project.FolderPath);
                    await Windows.System.Launcher.LaunchFolderAsync(folder);
                }
                catch { }
            }
        }
    }
}
