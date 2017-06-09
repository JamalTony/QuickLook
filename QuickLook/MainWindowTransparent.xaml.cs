﻿using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using QuickLook.Helpers;
using QuickLook.Helpers.BlurLibrary;
using QuickLook.Plugin;

namespace QuickLook
{
    /// <summary>
    ///     Interaction logic for MainWindowTransparent.xaml
    /// </summary>
    public partial class MainWindowTransparent : Window
    {
        internal MainWindowTransparent()
        {
            // this object should be initialized before loading UI components, because many of which are binding to it.
            ContextObject = new ContextObject();

            InitializeComponent();

            SourceInitialized += (sender, e) =>
            {
                if (AllowsTransparency)
                    BlurWindow.EnableWindowBlur(this);
            };

            buttonCloseWindow.MouseLeftButtonUp += (sender, e) =>
                ViewWindowManager.GetInstance().ClosePreview();

            buttonOpenWith.Click += (sender, e) =>
                ViewWindowManager.GetInstance().RunAndClosePreview();
        }

        public string PreviewPath { get; private set; }
        public IViewer Plugin { get; private set; }

        public ContextObject ContextObject { get; private set; }

        internal void RunAndHide()
        {
            if (string.IsNullOrEmpty(PreviewPath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(PreviewPath)
                {
                    WorkingDirectory = Path.GetDirectoryName(PreviewPath)
                });
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            BeginHide();
        }

        private void ResizeAndCenter(Size size)
        {
            if (!IsLoaded)
            {
                // if the window is not loaded yet, just leave the problem to WPF
                Width = size.Width;
                Height = size.Height;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;

                return;
            }

            // System.Windows.Forms does not consider DPI, so we need to do it maunally

            var screen = WindowHelper.GetCurrentWindowRect();

            var newLeft = screen.Left + (screen.Width - size.Width) / 2;
            var newTop = screen.Top + (screen.Height - size.Height) / 2;

            this.MoveWindow(newLeft, newTop, size.Width, size.Height);
        }

        internal void UnloadPlugin()
        {
            // the focused element will not processed by GC: https://stackoverflow.com/questions/30848939/memory-leak-due-to-window-efectivevalues-retention
            FocusManager.SetFocusedElement(this, null);
            Keyboard.DefaultRestoreFocusMode =
                RestoreFocusMode.None; // WPF will put the focused item into a "_restoreFocus" list ... omg
            Keyboard.ClearFocus();

            ContextObject.Reset();

            Plugin?.Cleanup();
            Plugin = null;

            ProcessHelper.PerformAggressiveGC();
        }

        internal void BeginShow(IViewer matchedPlugin, string path, Action<ExceptionDispatchInfo> exceptionHandler)
        {
            PreviewPath = path;
            Plugin = matchedPlugin;

            ContextObject.ViewerWindow = this;

            // get window size before showing it
            Plugin.Prepare(path, ContextObject);

            SetOpenWithButtonAndPath();

            // revert UI changes
            ContextObject.IsBusy = true;

            var newHeight = ContextObject.PreferredSize.Height + titlebar.Height + windowBorder.BorderThickness.Top +
                            windowBorder.BorderThickness.Bottom;
            var newWidth = ContextObject.PreferredSize.Width + windowBorder.BorderThickness.Left +
                           windowBorder.BorderThickness.Right;

            ResizeAndCenter(new Size(newWidth, newHeight));

            Show();

            //WindowHelper.SetActivate(new WindowInteropHelper(this), ContextObject.CanFocus);

            // load plugin, do not block UI
            Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        Plugin.View(path, ContextObject);
                    }
                    catch (Exception e)
                    {
                        exceptionHandler(ExceptionDispatchInfo.Capture(e));
                    }
                }),
                DispatcherPriority.Input);
        }

        private void SetOpenWithButtonAndPath()
        {
            var isExe = FileHelper.GetAssocApplication(PreviewPath, out string appFriendlyName);

            buttonOpenWith.Content = isExe == null
                ? Directory.Exists(PreviewPath)
                    ? $"Browse “{Path.GetFileName(PreviewPath)}”"
                    : "Open..."
                : isExe == true
                    ? $"Run “{appFriendlyName}”"
                    : $"Open with “{appFriendlyName}”";
        }

        internal void BeginHide()
        {
            UnloadPlugin();

            // if the this window is hidden in Max state, new show() will results in failure:
            // "Cannot show Window when ShowActivated is false and WindowState is set to Maximized"
            WindowState = WindowState.Normal;

            Hide();

            ProcessHelper.PerformAggressiveGC();
        }
    }
}