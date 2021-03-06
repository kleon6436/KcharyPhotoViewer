﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Kchary.PhotoViewer.Model;
using Kchary.PhotoViewer.ViewModels;

namespace Kchary.PhotoViewer.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// Native method class.
        /// </summary>
        public static class NativeMethods
        {
            [DllImport("user32.dll")]
            internal static extern bool SetWindowPlacement(IntPtr hWnd, [In] WindowPlacement lpwndpl);

            [DllImport("user32.dll")]
            internal static extern bool GetWindowPlacement(IntPtr hWnd, out WindowPlacement lpwndpl);

            [StructLayout(LayoutKind.Sequential)]
            public struct WindowPlacement
            {
                public int length;
                public int flags;
                public Sw showCmd;
                public Point minPosition;
                public Point maxPosition;
                public Rect normalPosition;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct Point
            {
                public int X;
                public int Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct Rect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            public enum Sw
            {
                Hide = 0,
                ShowNormal = 1,
                ShowMinimized = 2,
                Show = 5,
                Restore = 9,
            }
        }

        private const int MinSplTime = 1000;

        /// <summary>
        /// Application configuration manager
        /// </summary>
        private static readonly AppConfigManager AppConfigManager = AppConfigManager.GetInstance();

        public MainWindow()
        {
            // SplashScreenの表示
            var splashScreen = new SplashScreenView();
            splashScreen.Show();

            // SplashScreen表示中にViewModelの読み込み
            var timer = new Stopwatch();
            timer.Start();

            var vm = new MainWindowViewModel();
            vm.InitViewFolder();
            DataContext = vm;

            timer.Stop();

            // 一定時間待機後、SplashScreenを閉じる
            if (MinSplTime - timer.ElapsedMilliseconds > 0)
            {
                Thread.Sleep(MinSplTime);
            }
            splashScreen.Close();

            InitializeComponent();
        }

        /// <summary>
        /// ウィンドウオープン処理
        /// </summary>
        /// <param name="sender">Window</param>
        /// <param name="e">引数情報</param>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm || vm.SelectedMedia != null || !vm.MediaInfoList.Any())
            {
                return;
            }

            var firstImageData = vm.MediaInfoList.First();
            vm.SelectedMedia = firstImageData;
        }

        /// <summary>
        /// ウィンドウクローズ処理
        /// </summary>
        /// <param name="sender">Window</param>
        /// <param name="e">引数情報</param>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && !vm.StopThreadAndTask())
            {
                // 少し待ってからクローズ
                Thread.Sleep(200);
            }

            // ウィンドウ情報を保存
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.GetWindowPlacement(hwnd, out var placement);

            AppConfigManager.ConfigData.PlaceData = placement;
            AppConfigManager.Export();
        }

        /// <summary>
        /// ウィンドウサイズ切り替え時の処理
        /// </summary>
        /// <param name="sender">Window</param>
        /// <param name="e">引数情報</param>
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // ListBoxのアイテム選択時は、そのアイテムまでスクロールする
            var selectedItem = MediaListBox.SelectedItem;
            if (selectedItem != null)
            {
                MediaListBox.ScrollIntoView(selectedItem);
            }

            // Run GC.
            App.RunGc();
        }

        /// <summary>
        /// リストボックスで選択されたアイテムが変更されたとき
        /// </summary>
        /// <param name="sender">mediaListBox</param>
        /// <param name="e">引数情報</param>
        private async void MediaListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox listBox)
            {
                return;
            }

            if (listBox.SelectedItem is not MediaInfo mediaInfo)
            {
                return;
            }

            if (DataContext is MainWindowViewModel vm)
            {
                await vm.LoadMediaAsync(mediaInfo);
            }
        }

        /// <summary>
        /// コンテキストメニューがクリックされたとき
        /// </summary>
        /// <param name="sender">MenuItem</param>
        /// <param name="e">引数情報</param>
        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem)
            {
                return;
            }

            if (DataContext is MainWindowViewModel vm)
            {
                vm.ExecuteContextMenu(Convert.ToString(menuItem.Header));
            }
        }

        /// <summary>
        /// ウィンドウ表示時
        /// </summary>
        /// <param name="e">引数情報</param>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var windowPlacement = AppConfigManager.ConfigData.PlaceData;
            windowPlacement.showCmd = (windowPlacement.showCmd == NativeMethods.Sw.ShowMinimized) ? NativeMethods.Sw.ShowNormal : windowPlacement.showCmd;

            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPlacement(hwnd, windowPlacement);
        }
    }
}