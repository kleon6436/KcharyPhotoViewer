﻿using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Prism.Mvvm;
using Prism.Commands;
using PhotoViewer.Model;
using PhotoViewer.Views;
using System.Windows;

namespace PhotoViewer.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        #region ViewModels
        public ExplorerViewModel ExplorerViewModel { get; set; }
        public ExifInfoViewModel ExifInfoViewModel { get; set; }
        #endregion

        #region UI binding parameters
        private string selectFolderPath;
        /// <summary>
        /// 表示中のフォルダパス
        /// </summary>
        public string SelectFolderPath
        {
            get { return selectFolderPath; }
            set { SetProperty(ref selectFolderPath, value); }
        }

        private ObservableCollection<MediaInfo> mediaInfoList = new ObservableCollection<MediaInfo>();
        /// <summary>
        /// ListBoxに表示するメディアリスト
        /// </summary>
        public ObservableCollection<MediaInfo> MediaInfoList
        {
            get { return mediaInfoList; }
            set { SetProperty(ref mediaInfoList, value); }
        }

        private MediaInfo selectedMedia;
        /// <summary>
        /// ListBoxで選択されているメディア
        /// </summary>
        public MediaInfo SelectedMedia
        {
            get { return selectedMedia; }
            set { SetProperty(ref selectedMedia, value); }
        }

        private BitmapSource pictureImageSource;
        /// <summary>
        /// 拡大表示しているメディアの画像
        /// </summary>
        public BitmapSource PictureImageSource
        {
            get { return pictureImageSource; }
            set { SetProperty(ref pictureImageSource, value); }
        }

        private ObservableCollection<ContextMenuInfo> contextMenuCollection = new ObservableCollection<ContextMenuInfo>();
        /// <summary>
        /// ContextMenuで表示するメニューアイテムリスト
        /// </summary>
        public ObservableCollection<ContextMenuInfo> ContextMenuCollection
        {
            get { return contextMenuCollection; }
            set { SetProperty(ref contextMenuCollection, value); }
        }

        private bool isShowContextMenu;
        public bool IsShowContextMenu
        {
            get { return isShowContextMenu; }
            set { SetProperty(ref isShowContextMenu, value); }
        }
        #endregion

        #region Command
        public ICommand OpenFolderButtonCommand { get; private set; }
        public ICommand ReloadButtonCommand { get; private set; }
        public ICommand SettingButtonCommand { get; private set; }
        #endregion

        // メディア情報の読み込みスレッド
        private BackgroundWorker LoadContentsBackgroundWorker;
        // メディアリストのリロードフラグ
        private bool IsReloadContents;

        public MainWindowViewModel()
        {
            // 初期値設定
            MediaInfoList.Clear();
            ContextMenuCollection.Clear();
            PictureImageSource = null;
            IsShowContextMenu = false;

            // コマンドの設定
            OpenFolderButtonCommand = new DelegateCommand(OpenFolderButtonClicked);
            ReloadButtonCommand = new DelegateCommand(ReloadButtonClicked);
            SettingButtonCommand = new DelegateCommand(SettingButtonClicked);

            // エクスプローラー部のViewModel設定
            ExplorerViewModel = new ExplorerViewModel();
            ExplorerViewModel.ChangeSelectItemEvent += ExplorerViewModel_ChangeSelectItemEvent;
            UpdateExplorerTree();

            // Exif表示部のViewModel設定
            ExifInfoViewModel = new ExifInfoViewModel();

            // 設定情報の読み込み
            AppConfigManager appConfigManager = AppConfigManager.GetInstance();
            appConfigManager.ImportLinkageAppXml();

            if (appConfigManager.LinkageApp != null)
            {
                // アプリアイコンを読み込み
                Icon appIcon = Icon.ExtractAssociatedIcon(appConfigManager.LinkageApp.AppPath);
                var iconBitmapSource = Imaging.CreateBitmapSourceFromHIcon(appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                // コンテキストメニューの設定
                var contextMenu = new ContextMenuInfo(appConfigManager.LinkageApp.AppName, iconBitmapSource);
                ContextMenuCollection.Add(contextMenu);
                IsShowContextMenu = true;
            }

            string defaultPicturePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures);
            ChangeContents(defaultPicturePath);
        }

        /// <summary>
        /// エクスプローラーで選択されているフォルダを開く
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
                Process.Start("EXPLORER.EXE", SelectFolderPath);
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
        /// ListBoxの表示を更新する
        /// </summary>
        private void ReloadButtonClicked()
        {
            UpdateContents();
        }

        /// <summary>
        /// 設定画面を開く
        /// </summary>
        private void SettingButtonClicked()
        {
            var vm = new SettingViewModel();
            vm.ReloadContextMenuEvent += ReloadContextMenu;

            var settingDialog = new SettingView();
            settingDialog.DataContext = vm;
            settingDialog.Owner = App.Current.MainWindow;
            settingDialog.ShowDialog();
        }

        /// <summary>
        /// コンテキストメニューを読み直す
        /// </summary>
        /// <param name="sender">SettingViewModel</param>
        /// <param name="e">引数情報</param>
        private void ReloadContextMenu(object sender, EventArgs e)
        {
            // 設定情報から連携アプリ関連の情報を再読み込み
            AppConfigManager appConfigManager = AppConfigManager.GetInstance();
            appConfigManager.ImportLinkageAppXml();

            // 現在のコンテキストメニューをリセット
            ContextMenuCollection.Clear();
            IsShowContextMenu = false;

            if (appConfigManager.LinkageApp != null)
            { 
                // アプリアイコンを読み込み
                Icon appIcon = Icon.ExtractAssociatedIcon(appConfigManager.LinkageApp.AppPath);
                var iconBitmapSource = Imaging.CreateBitmapSourceFromHIcon(appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                // コンテキストメニューの設定
                var contextMenu = new ContextMenuInfo(appConfigManager.LinkageApp.AppName, iconBitmapSource);
                ContextMenuCollection.Add(contextMenu);
                IsShowContextMenu = true;
            }
        }

        /// <summary>
        /// コンテキストメニューがクリックされたとき
        /// </summary>
        /// <param name="appName">アプリ名</param>
        public void ExecuteContextMenu(string appName)
        {
            AppConfigManager appConfigManager = AppConfigManager.GetInstance();
            if (appName != appConfigManager.LinkageApp.AppName)
            {
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                Process.Start(appConfigManager.LinkageApp.AppPath, SelectedMedia.FilePath);
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
        /// ExplorerViewでフォルダ選択変更があった場合
        /// </summary>
        /// <param name="sender">ExplorerViewModel</param>
        /// <param name="e">引数情報</param>
        private void ExplorerViewModel_ChangeSelectItemEvent(object sender, EventArgs e)
        {
            var selectedExplorerItem = ExplorerViewModel.SelectedItem;
            ChangeContents(selectedExplorerItem.ExplorerItemPath);
        }

        /// <summary>
        /// エクスプローラーのツリー表示を更新
        /// </summary>
        private void UpdateExplorerTree()
        {
            ExplorerViewModel.CreateDriveTreeItem();
        }

        /// <summary>
        /// メディアリストに表示するフォルダを変更
        /// </summary>
        /// <param name="folderPath">メディアリストを表示するフォルダパス</param>
        private void ChangeContents(string folderPath)
        {
            if (!Directory.Exists(folderPath) || SelectFolderPath == folderPath)
            {
                return;
            }

            // フォルダパスを更新して、リスト更新
            SelectFolderPath = folderPath;
            UpdateContents();
        }

        /// <summary>
        /// コンテンツリストの表示を更新
        /// </summary>
        private void UpdateContents()
        {
            if (LoadContentsBackgroundWorker != null && LoadContentsBackgroundWorker.IsBusy)
            {
                // すでにスレッド動作中の場合、一旦スレッド処理をキャンセルし、キャンセル後に再実行する
                LoadContentsBackgroundWorker.CancelAsync();
                IsReloadContents = true;
                return;
            }

            LoadContentsList();
        }

        /// <summary>
        /// コンテンツリストの読み込み
        /// </summary>
        private void LoadContentsList()
        {
            // 読み込み前に表示リストをクリア
            MediaInfoList.Clear();

            // 時間がかかるため、別スレッドでメディアリストを読み込む
            var backgroundWorker = new BackgroundWorker();
            backgroundWorker.WorkerSupportsCancellation = true;
            backgroundWorker.DoWork += LoadContentsWorker_DoWork;
            backgroundWorker.RunWorkerCompleted += LoadContentsWorker_RunWorkerCompleted;

            // 読み込みスレッド開始
            LoadContentsBackgroundWorker = backgroundWorker;
            LoadContentsBackgroundWorker.RunWorkerAsync();
        }

        /// <summary>
        /// 別スレッドで動作する処理
        /// </summary>
        /// <param name="sender">BackgroundWorker</param>
        /// <param name="e">引数情報</param>
        private void LoadContentsWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                LoadContentsWorker(sender, e);
            }
            catch (Exception ex)
            {
                App.LogException(ex);
                App.ShowErrorMessageBox("メディアの読み込みに失敗しました。", "読み込みエラー");
            }
        }

        /// <summary>
        /// コンテンツ読み込みスレッドでの処理完了時
        /// </summary>
        /// <param name="sender">LoadContentsWorker</param>
        /// <param name="e">引数情報</param>
        private void LoadContentsWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                StopContentsWorker();

                if (IsReloadContents)
                {
                    // 非同期で読み込んでいるコンテンツリストの読み込み完了後にリロード
                    App.Current.Dispatcher.BeginInvoke((Action)(() => 
                    { 
                        UpdateContents();
                        IsReloadContents = false;
                    }), DispatcherPriority.Normal);
                }
            }
            else
            {
                StopContentsWorker();
            }
        }

        /// <summary>
        /// コンテンツリストの読み込みの実処理
        /// </summary>
        /// <param name="sender">BackgroundWorker</param>
        /// <param name="e">引数情報</param>
        private void LoadContentsWorker(object sender, DoWorkEventArgs e)
        {
            List<string> filePaths = new List<string>();
            int tick = Environment.TickCount;

            // 選択されたフォルダ内のサポートされるファイルを全て取得
            foreach (string supportExtension in MediaChecker.GetSupportExtentions())
            {
                var worker = sender as BackgroundWorker;
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }

                filePaths.AddRange(Directory.GetFiles(SelectFolderPath, "*" + supportExtension).ToList());
            }

            
            var readyFiles = new Queue<MediaInfo>();
            foreach (var filePath in filePaths)
            {
                var worker = sender as BackgroundWorker;
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }

                var mediaInfo = new MediaInfo();
                mediaInfo.FilePath = filePath;
                mediaInfo.FileName = Path.GetFileName(mediaInfo.FilePath);
                try
                {
                    mediaInfo.CreateThumbnailImage();
                }
                catch (Exception ex)
                {
                    // サムネイル画像が作成できないものは、ログ出力して読み込みスキップ
                    App.LogException(ex);
                    continue;
                }

                int count = 0;
                readyFiles.Enqueue(mediaInfo);
                count++;

                int duration = Environment.TickCount - tick;
                if ((count <= 100 && duration > 500) || duration > 1000)
                {
                    var readyList = readyFiles.ToArray();
                    readyFiles.Clear();
                    App.Current.Dispatcher.BeginInvoke((Action)(() => { MediaInfoList.AddRange(readyList); }));
                }
            }

            if (readyFiles.Count > 0)
            {
                App.Current.Dispatcher.Invoke((Action)(() => { foreach (var readyFile in readyFiles) MediaInfoList.Add(readyFile); }));
            }
        }

        /// <summary>
        /// コンテンツ読み込みスレッドの完全停止
        /// </summary>
        private void StopContentsWorker()
        {
            if (LoadContentsBackgroundWorker != null)
            {
                LoadContentsBackgroundWorker.Dispose();
            }
        }

        /// <summary>
        /// 選択された画像を読み込み、表示用に変換する
        /// </summary>
        /// <param name="mediaInfo">選択されたメディア情報</param>
        public bool LoadMedia(MediaInfo mediaInfo)
        {
            if (!File.Exists(mediaInfo.FilePath))
            {
                App.ShowErrorMessageBox("ファイルが存在しません。", "ファイルアクセスエラー");
            }
            
            // Viewに設定されているものをクリア
            PictureImageSource = null;

            switch (mediaInfo.ContentMediaType)
            {
                case MediaInfo.MediaType.PICTURE:
                    return LoadPictureImage(mediaInfo);

                case MediaInfo.MediaType.MOVIE:
                default:
                    return false;
            }
        }

        /// <summary>
        /// 拡大表示する画像を読み込む
        /// </summary>
        /// <param name="mediaInfo">選択されたメディア情報</param>
        /// <returns>読み込み成功: True、失敗: False</returns>
        private bool LoadPictureImage(MediaInfo mediaInfo)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                // 表示する画像を作成
                PictureImageSource = ImageControl.CreatePictureViewImage(mediaInfo.FilePath);

                // Exif情報を設定
                ExifInfoViewModel.SetExif(mediaInfo.FilePath);
                return true;
            }
            catch (Exception ex)
            {
                App.LogException(ex);

                App.ShowErrorMessageBox("ファイルアクセスでエラーが発生しました。", "ファイルアクセスエラー");
                return false;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// 実行中のスレッドとタスクを停止する
        /// </summary>
        /// <returns>Thread実行中の場合: False、それ以外: True</returns>
        public bool StopThreadAndTask()
        {
            bool CanClose = true;

            // コンテンツ読み込みスレッドが動作中の場合、キャンセル通知
            if (LoadContentsBackgroundWorker != null && LoadContentsBackgroundWorker.IsBusy)
            {
                LoadContentsBackgroundWorker.CancelAsync();
                CanClose = false;
            }

            return CanClose;
        }
    }
}
