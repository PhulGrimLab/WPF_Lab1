using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WPF.Ctrl.TextBox
{
    /// <summary>
    /// Interaction logic for CtrlTextBox.xaml
    /// </summary>
    public partial class CtrlTextBox : UserControl
    {
        private const int MaxLogLines = 400;
        private const int FlushIntervalMs = 200;
        private const int MaxBatchSize = 50;

        private readonly object _pendingLock = new object();
        private readonly Queue<string> _pendingLogs = new Queue<string>();
        private readonly List<string> _logItems = new List<string>();
        private readonly DispatcherTimer _flushTimer;

        private ScrollViewer? _scrollViewer;
        private ItemsControl? _logItemsControl;
        private bool _autoScroll = true;

        public CtrlTextBox()
        {
            InitializeComponent();

            Loaded += TextBoxView_Loaded;

            _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(FlushIntervalMs)
            };
            _flushTimer.Tick += FlushTimer_Tick;
        }

        private void TextBoxView_Loaded(object? sender, RoutedEventArgs e)
        {
            _scrollViewer = FindRequiredName<ScrollViewer>(this, "PART_ScrollViewer");
            _logItemsControl = FindRequiredName<ItemsControl>(this, "PART_LogItemsControl");
        }

        private void ScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_scrollViewer == null) return;
            _autoScroll = (_scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - 20);
        }

        public void SetClearAll()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    lock (_pendingLock)
                    {
                        _pendingLogs.Clear();
                    }

                    _logItems.Clear();

                    if (_logItemsControl != null)
                    {
                        _logItemsControl.ItemsSource = null;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"LogMessage 오류: {ex.Message}");
                }
            }));
        }

        private void WriteLog(string message, bool timeStemp)
        {
            string logEntry;

            if (timeStemp)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                logEntry = $"[{timestamp}] {message}";
            }
            else
            {
                logEntry = message;
            }

            lock (_pendingLock)
            {
                _pendingLogs.Enqueue(logEntry);
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_flushTimer.IsEnabled)
                {
                    _flushTimer.Start();
                }
            }), DispatcherPriority.Background);
        }

        private void FlushTimer_Tick(object? sender, EventArgs e)
        {
            // 기존 로직
        }

        public void AddLog(string message)
        {
            WriteLog(message, true);
        }

        public void AddLogNoTime(string message)
        {
            WriteLog(message, false);
        }

        public void LogByte(string msg)
        {
            byte[] byteArray = Encoding.UTF8.GetBytes(msg);

            string outMsg = string.Empty;

            foreach (byte b in byteArray)
            {
                outMsg += $"{b:X2} ";
            }

            AddLog(outMsg);
        }

        private static T FindRequiredName<T>(FrameworkElement root, string name) where T : class
        {
            return root.FindName(name) as T
                ?? throw new InvalidOperationException($"필수 컨트롤 '{name}'을(를) 찾을 수 없습니다.");
        }
    }

}
