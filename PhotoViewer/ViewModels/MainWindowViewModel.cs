﻿using Kchary.PhotoViewer.Model;
using Kchary.PhotoViewer.Views;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Kchary.PhotoViewer.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        #region ViewModels

        /// <summary>
        /// Explorer view model
        /// </summary>
        public ExplorerViewModel ExplorerViewModel { get; }

        /// <summary>
        /// Exif info view model
        /// </summary>
        public ExifInfoViewModel ExifInfoViewModel { get; }

        #endregion ViewModels

        #region UI binding parameters

        private string selectFolderPath;

        /// <summary>
        /// older path being displayed
        /// </summary>
        public string SelectFolderPath
        {
            get => selectFolderPath;
            private set => SetProperty(ref selectFolderPath, value);
        }

        /// <summary>
        /// Media list displayed in ListBox
        /// </summary>
        public ObservableCollection<MediaInfo> MediaInfoList { get; } = new();

        private MediaInfo selectedMedia;

        /// <summary>
        /// Media selected in ListBox
        /// </summary>
        public MediaInfo SelectedMedia
        {
            get => selectedMedia;
            set => SetProperty(ref selectedMedia, value);
        }

        private BitmapSource pictureImageSource;

        /// <summary>
        /// Image of enlarged media
        /// </summary>
        public BitmapSource PictureImageSource
        {
            get => pictureImageSource;
            private set => SetProperty(ref pictureImageSource, value);
        }

        /// <summary>
        /// Menu item list displayed by ContextMenu
        /// </summary>
        public ObservableCollection<ContextMenuInfo> ContextMenuCollection { get; } = new();

        private bool isShowContextMenu;

        public bool IsShowContextMenu
        {
            get => isShowContextMenu;
            set => SetProperty(ref isShowContextMenu, value);
        }

        private bool isEnableImageEditButton;

        public bool IsEnableImageEditButton
        {
            get => isEnableImageEditButton;
            set => SetProperty(ref isEnableImageEditButton, value);
        }

        #endregion UI binding parameters

        #region Command

        public ICommand BluetoothButtonCommand { get; }
        public ICommand OpenFolderButtonCommand { get; }
        public ICommand ReloadButtonCommand { get; }
        public ICommand SettingButtonCommand { get; }
        public ICommand ImageEditButtonCommand { get; }

        #endregion Command

        /// <summary>
        /// Media information read thread
        /// </summary>
        private BackgroundWorker loadContentsBackgroundWorker;

        /// <summary>
        /// Reload flag of media list
        /// </summary>
        private bool isReloadContents;

        /// <summary>
        /// Application configuration manager
        /// </summary>
        private static readonly AppConfigManager AppConfigManager = AppConfigManager.GetInstance();

        /// <summary>
        /// Default picture path
        /// </summary>
        private static readonly string DefaultPicturePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures);

        public MainWindowViewModel()
        {
            // Set command.
            BluetoothButtonCommand  = new DelegateCommand(BluetoothButtonClicked);
            OpenFolderButtonCommand = new DelegateCommand(OpenFolderButtonClicked);
            ReloadButtonCommand     = new DelegateCommand(ReloadButtonClicked);
            SettingButtonCommand    = new DelegateCommand(SettingButtonClicked);
            ImageEditButtonCommand  = new DelegateCommand(ImageEditButtonClicked);

            // Read configure file.
            LoadConfigFile();

            // Set view model of explorer view.
            ExplorerViewModel = new ExplorerViewModel();
            ExplorerViewModel.ChangeSelectItemEvent += ExplorerViewModel_ChangeSelectItemEvent;
            UpdateExplorerTree();

            // Set view model of exif info view.
            ExifInfoViewModel = new ExifInfoViewModel();
        }

        /// <summary>
        /// Load the initial display folder and setting file.
        /// </summary>
        public void InitViewFolder()
        {
            // Read setting information.
            var linkageAppList = AppConfigManager.ConfigData.LinkageAppList;
            if (linkageAppList != null && linkageAppList.Any())
            {
                for (var i = 0; i < linkageAppList.Count; i++)
                {
                    var linkageApp = linkageAppList[i];

                    if (!File.Exists(linkageApp.AppPath))
                    {
                        AppConfigManager.ConfigData.LinkageAppList.Remove(linkageApp);
                        continue;
                    }

                    // Load application icon.
                    var appIcon = Icon.ExtractAssociatedIcon(linkageApp.AppPath);
                    if (appIcon != null)
                    {
                        var iconBitmapSource = Imaging.CreateBitmapSourceFromHIcon(appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                        // Set context menu.
                        var contextMenu = new ContextMenuInfo { DisplayName = linkageApp.AppName, ContextIcon = iconBitmapSource };
                        ContextMenuCollection.Add(contextMenu);
                    }

                    IsShowContextMenu = true;
                }
            }

            // Load image folder.
            var picturePath = DefaultPicturePath;
            if (!string.IsNullOrEmpty(AppConfigManager.ConfigData.PreviousFolderPath) && Directory.Exists(AppConfigManager.ConfigData.PreviousFolderPath))
            {
                picturePath = AppConfigManager.ConfigData.PreviousFolderPath;
            }
            ChangeContents(picturePath);
        }

        /// <summary>
        /// Event when context menu is clicked.
        /// </summary>
        /// <param name="appName">Application name</param>
        public void ExecuteContextMenu(string appName)
        {
            var linkageAppList = AppConfigManager.ConfigData.LinkageAppList;
            if (linkageAppList.All(x => x.AppName != appName))
            {
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var appPath = linkageAppList.Find(x => x.AppName == appName)?.AppPath;
                if (!string.IsNullOrEmpty(appPath))
                {
                    Process.Start(appPath, SelectedMedia.FilePath);
                }
                else
                {
                    App.ShowErrorMessageBox("Linkage app path is not found.", "Process start error");
                }
            }
            catch (Exception ex)
            {
                App.LogException(ex);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// Load selected image and convert for display.
        /// </summary>
        /// <param name="mediaInfo">Selected media info</param>
        public async Task<bool> LoadMediaAsync(MediaInfo mediaInfo)
        {
            if (!File.Exists(mediaInfo.FilePath))
            {
                App.ShowErrorMessageBox("File not exist.", "File access error");
            }

            IsEnableImageEditButton = false;

            return mediaInfo.ContentMediaType switch
            {
                MediaInfo.MediaType.Picture => await LoadPictureImageAsync(mediaInfo),
                _ => false,
            };
        }

        /// <summary>
        /// Stop running threads and tasks.
        /// </summary>
        /// <returns>Run thread: False, Not run thread: True</returns>
        public bool StopThreadAndTask()
        {
            // Cancel notification if content loading thread is running
            if (loadContentsBackgroundWorker is not {IsBusy: true})
            {
                return true;
            }

            loadContentsBackgroundWorker.CancelAsync();
            return false;

        }

        /// <summary>
        /// Read configuration file.
        /// </summary>
        private static void LoadConfigFile()
        {
            AppConfigManager.Import();
        }

        private void BluetoothButtonClicked()
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                Process.Start("fsquirt.exe", "-send");
            }
            catch (Exception ex)
            {
                App.LogException(ex);
                App.ShowErrorMessageBox("Not support Bluetooth transmission.", "Bluetooth transmission error");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// Open selected folder in Explorer.
        /// </summary>
        private void OpenFolderButtonClicked()
        {
            if (string.IsNullOrEmpty(SelectFolderPath))
            {
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var selectPath = (File.GetAttributes(SelectFolderPath) & FileAttributes.Directory) == FileAttributes.Directory ? SelectFolderPath : Path.GetDirectoryName(SelectFolderPath);

                const string Explorer = "EXPLORER.EXE";
                if (!string.IsNullOrEmpty(selectPath))
                {
                    Process.Start(Explorer, selectPath);
                }
                else
                {
                    App.ShowErrorMessageBox("Select path is not found.", "Process start error");
                }
            }
            catch (Exception ex)
            {
                App.LogException(ex);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// Update ListBox display.
        /// </summary>
        private void ReloadButtonClicked()
        {
            // Clear picture image view source and exif info data.
            PictureImageSource = null;
            ExifInfoViewModel.ExifDataList.Clear();

            // Update image edit button status.
            IsEnableImageEditButton = false;

            // If the file path is displayed, change it to the directory path and read it.
            if ((File.GetAttributes(SelectFolderPath) & FileAttributes.Directory) != FileAttributes.Directory)
            {
                SelectFolderPath = Path.GetDirectoryName(SelectFolderPath);
            }
            UpdateContents();
        }

        /// <summary>
        /// Open the setting screen.
        /// </summary>
        private void SettingButtonClicked()
        {
            var vm = new SettingViewModel();
            vm.ReloadContextMenuEvent += ReloadContextMenu;

            var settingDialog = new SettingView
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };
            settingDialog.ShowDialog();
        }

        /// <summary>
        /// Open image editing tool.
        /// </summary>
        private void ImageEditButtonClicked()
        {
            if (SelectedMedia == null)
            {
                return;
            }

            var vm = new ImageEditToolViewModel();
            vm.SetEditFileData(SelectedMedia.FilePath);

            var imageEditToolDialog = new ImageEditToolView
            {
                DataContext = vm,
                Owner = Application.Current.MainWindow
            };
            imageEditToolDialog.ShowDialog();
        }

        /// <summary>
        /// Reread context menu
        /// </summary>
        /// <param name="sender">SettingViewModel</param>
        /// <param name="e">Argument</param>
        private void ReloadContextMenu(object sender, EventArgs e)
        {
            // Reset context menu.
            ContextMenuCollection.Clear();
            IsShowContextMenu = false;

            // Reload the information related to the linked application from the setting information.
            var linkageAppList = AppConfigManager.ConfigData.LinkageAppList;
            if (linkageAppList == null || !linkageAppList.Any())
            {
                return;
            }

            foreach (var linkageApp in linkageAppList)
            {
                // Load application icon.
                var appIcon = Icon.ExtractAssociatedIcon(linkageApp.AppPath);
                if (appIcon != null)
                {
                    var iconBitmapSource = Imaging.CreateBitmapSourceFromHIcon(appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                    // Set context menu.
                    var contextMenu = new ContextMenuInfo { DisplayName = linkageApp.AppName, ContextIcon = iconBitmapSource };
                    ContextMenuCollection.Add(contextMenu);
                }

                IsShowContextMenu = true;
            }
        }

        /// <summary>
        /// Event when folder selection is changed in Explorer View.
        /// </summary>
        /// <param name="sender">ExplorerViewModel</param>
        /// <param name="e">Argument</param>
        private void ExplorerViewModel_ChangeSelectItemEvent(object sender, EventArgs e)
        {
            SelectedMedia = null;
            PictureImageSource = null;
            ExifInfoViewModel.ExifDataList.Clear();
            IsEnableImageEditButton = false;

            var selectedExplorerItem = ExplorerViewModel.SelectedItem;
            ChangeContents(selectedExplorerItem.ExplorerItemPath);
        }

        /// <summary>
        /// Update explorer tree view.
        /// </summary>
        private void UpdateExplorerTree()
        {
            ExplorerViewModel.CreateDriveTreeItem();

            var previousFolderPath = DefaultPicturePath;
            if (!string.IsNullOrEmpty(AppConfigManager.ConfigData.PreviousFolderPath) && Directory.Exists(AppConfigManager.ConfigData.PreviousFolderPath))
            {
                previousFolderPath = AppConfigManager.ConfigData.PreviousFolderPath;
            }
            ExplorerViewModel.ExpandPreviousPath(previousFolderPath);
        }

        /// <summary>
        /// Change the folder displayed in the media list.
        /// </summary>
        /// <param name="folderPath">Folder path to display media list</param>
        private void ChangeContents(string folderPath)
        {
            if (!Directory.Exists(folderPath) || SelectFolderPath == folderPath)
            {
                return;
            }

            // Update folder path to update list.
            SelectFolderPath = folderPath;
            UpdateContents();

            AppConfigManager.ConfigData.PreviousFolderPath = SelectFolderPath;
        }

        /// <summary>
        /// Refresh display of content list.
        /// </summary>
        private void UpdateContents()
        {
            if (loadContentsBackgroundWorker != null && loadContentsBackgroundWorker.IsBusy)
            {
                loadContentsBackgroundWorker.CancelAsync();
                isReloadContents = true;
                return;
            }

            LoadContentsList();
        }

        /// <summary>
        /// Load content list.
        /// </summary>
        private void LoadContentsList()
        {
            // Clear display list before loading.
            MediaInfoList.Clear();

            var backgroundWorker = new BackgroundWorker
            {
                WorkerSupportsCancellation = true
            };
            backgroundWorker.DoWork += LoadContentsWorker_DoWork;
            backgroundWorker.RunWorkerCompleted += LoadContentsWorker_RunWorkerCompleted;

            loadContentsBackgroundWorker = backgroundWorker;
            loadContentsBackgroundWorker.RunWorkerAsync();
        }

        /// <summary>
        /// Process that operates in another thread.
        /// </summary>
        /// <param name="sender">BackgroundWorker</param>
        /// <param name="e">Argument</param>
        private void LoadContentsWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                LoadContentsWorker(sender, e);
            }
            catch (Exception ex)
            {
                App.LogException(ex);

                const string MediaReadErrorMessage = "Failed to load media file.";
                const string MediaReadErrorTitle = "File read error";
                App.ShowErrorMessageBox(MediaReadErrorMessage, MediaReadErrorTitle);
            }

            App.RunGc();
        }

        /// <summary>
        /// Event when processing is completed in the content loading thread.
        /// </summary>
        /// <param name="sender">LoadContentsWorker</param>
        /// <param name="e">Argument</param>
        private void LoadContentsWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                StopContentsWorker();

                if (isReloadContents)
                {
                    // Reload after loading the asynchronously loaded content list.
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        UpdateContents();
                        isReloadContents = false;
                    }), DispatcherPriority.Normal);
                }
            }
            else
            {
                StopContentsWorker();
                if (SelectedMedia == null && MediaInfoList.Any())
                {
                    SelectedMedia = MediaInfoList[0];
                }
            }
        }

        /// <summary>
        /// Actual processing of reading the content list
        /// </summary>
        /// <param name="sender">BackgroundWorker</param>
        /// <param name="e">Argument</param>
        private void LoadContentsWorker(object sender, DoWorkEventArgs e)
        {
            var queue = new LinkedList<MediaInfo>();
            var tick = Environment.TickCount;
            var count = 0;

            // Get all supported files in selected folder.
            foreach (var supportExtension in MediaChecker.GetSupportExtentions())
            {
                // If the file path is displayed, change it to the directory path and read it.
                var folderPath = SelectFolderPath;
                if ((File.GetAttributes(folderPath) & FileAttributes.Directory) != FileAttributes.Directory)
                {
                    folderPath = Path.GetDirectoryName(folderPath);
                }

                if (string.IsNullOrEmpty(folderPath))
                {
                    continue;
                }

                // Read all support image file.
                foreach (var supportFile in Directory.EnumerateFiles(folderPath, $"*{supportExtension}").OrderBy(Path.GetFileName))
                {
                    if (sender is BackgroundWorker {CancellationPending: true})
                    {
                        e.Cancel = true;
                        return;
                    }

                    var mediaInfo = new MediaInfo
                    {
                        FilePath = supportFile
                    };
                    mediaInfo.FileName = Path.GetFileName(mediaInfo.FilePath);

                    if (!mediaInfo.CreateThumbnailImage())
                    {
                        continue;
                    }

                    queue.AddLast(mediaInfo);
                    count++;

                    if (!queue.Any())
                    {
                        continue;
                    }

                    var duration = Environment.TickCount - tick;
                    if ((count > 100 || duration <= 500) && duration <= 1000)
                    {
                        continue;
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (sender is BackgroundWorker {CancellationPending: true})
                        {
                            e.Cancel = true;
                            return;
                        }

                        MediaInfoList.AddRange(queue);

                        // If the image is not displayed at the time of loading, the image at the top of the list is displayed.
                        if (!MediaInfoList.Any() || SelectedMedia != null)
                        {
                            return;
                        }

                        var firstImageData = MediaInfoList.First();
                        if (!MediaChecker.CheckRawFileExtension(Path.GetExtension(firstImageData.FilePath)?.ToLower()))
                        {
                            SelectedMedia = firstImageData;
                        }
                    });

                    queue.Clear();
                    tick = Environment.TickCount;
                }
            }

            if (queue.Any())
            {
                Application.Current.Dispatcher.Invoke(() => { MediaInfoList.AddRange(queue); });
            }
        }

        /// <summary>
        /// Stop content loading thread.
        /// </summary>
        private void StopContentsWorker()
        {
            loadContentsBackgroundWorker?.Dispose();
        }

        /// <summary>
        /// Load the image to be enlarged.
        /// </summary>
        /// <param name="mediaInfo">Selected media information</param>
        /// <returns>Successful reading: True、Failure: False</returns>
        private async Task<bool> LoadPictureImageAsync(MediaInfo mediaInfo)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                // Create display image task.
                var loadPictureTask = Task.Run(() => { PictureImageSource = ImageController.CreatePictureViewImage(mediaInfo.FilePath); });

                // Set exif information task.
                var setExifInfoTask = Task.Run(() => { ExifInfoViewModel.SetExif(mediaInfo.FilePath); });

                // Do task
                await Task.WhenAll(loadPictureTask, setExifInfoTask);

                // Update image edit button status.
                IsEnableImageEditButton = !MediaChecker.CheckRawFileExtension(Path.GetExtension(mediaInfo.FilePath)?.ToLower());

                // Update select path.
                SelectFolderPath = mediaInfo.FilePath;

                // Memory release of Writable Bitmap.
                App.RunGc();

                return true;
            }
            catch (Exception ex)
            {
                App.LogException(ex);
                App.ShowErrorMessageBox("File access error occurred", "File access error");

                return false;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
    }
}