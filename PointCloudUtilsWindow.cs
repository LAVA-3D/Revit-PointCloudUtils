using Autodesk.Revit.DB;
using PointCloudUtils.Commands;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace PointCloudUtils
{
    /// <summary>
    /// Modeless WPF window for toggling point cloud visibility.
    /// </summary>
    public class PointCloudUtilsWindow : Window
    {
        // ── Controls ──────────────────────────────────────────────────────
        private CheckBox _toggleAllCheckBox = null!;
        private StackPanel _pointCloudList = null!;
        private TextBlock _statusText = null!;

        // ── State ─────────────────────────────────────────────────────────
        private List<PointCloudInfo> _pointClouds = new List<PointCloudInfo>();
        private bool _suppress; // Suppress checkbox events during refresh

        // ─────────────────────────────────────────────────────────────────

        public PointCloudUtilsWindow()
        {
            Title = "Point Cloud Visibility";
            Width = 280;
            MinWidth = 200;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 100;
            Top = 100;

            BuildUI();
        }

        // ── UI construction (code-behind, no XAML) ────────────────────────

        private void BuildUI()
        {
            var root = new StackPanel { Margin = new Thickness(8) };

            // ── Toggle All checkbox ───────────────────────────────────────
            _toggleAllCheckBox = new CheckBox
            {
                Content = "Toggle All",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
                IsChecked = true
            };
            _toggleAllCheckBox.Click += OnToggleAllClick;
            root.Children.Add(_toggleAllCheckBox);

            // ── Separator ─────────────────────────────────────────────────
            root.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 8) });

            // ── Scrollable point cloud list ──────────────────────────────
            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 400,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            _pointCloudList = new StackPanel();
            scrollViewer.Content = _pointCloudList;
            root.Children.Add(scrollViewer);

            // ── Status text (shown when no point clouds) ─────────────────
            _statusText = new TextBlock
            {
                Text = "No point clouds found",
                Foreground = System.Windows.Media.Brushes.Gray,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 4, 0, 0),
            };
            _statusText.Visibility = System.Windows.Visibility.Collapsed;
            root.Children.Add(_statusText);

            // ── Refresh button ────────────────────────────────────────────
            var refreshButton = new Button
            {
                Content = "Refresh",
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            refreshButton.Click += (_, __) => RefreshPointClouds();
            root.Children.Add(refreshButton);

            Content = root;
        }

        // ── Public methods (called from App) ──────────────────────────────

        /// <summary>
        /// Triggers a refresh of the point cloud list via ExternalEvent.
        /// </summary>
        public void RefreshPointClouds()
        {
            App.RefreshHandler.OnDone = OnPointCloudsRefreshed;
            App.RefreshEvent.Raise();
        }

        /// <summary>
        /// Clears the point cloud list (called when document closes).
        /// </summary>
        public void ClearPointClouds()
        {
            Dispatcher.InvokeAsync(() =>
            {
                _pointClouds.Clear();
                RebuildCheckboxList();
            });
        }

        // ── Callbacks from handlers ───────────────────────────────────────

        /// <summary>
        /// Called by RefreshHandler when point cloud query completes.
        /// </summary>
        private void OnPointCloudsRefreshed(List<PointCloudInfo> pointClouds)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _pointClouds = pointClouds ?? new List<PointCloudInfo>();
                RebuildCheckboxList();
            });
        }

        /// <summary>
        /// Called by ToggleHandler when visibility change completes.
        /// </summary>
        private void OnToggleComplete()
        {
            // Refresh to get updated visibility states
            RefreshPointClouds();
        }

        // ── UI rebuild ────────────────────────────────────────────────────

        private void RebuildCheckboxList()
        {
            _suppress = true;

            _pointCloudList.Children.Clear();

            if (_pointClouds.Count == 0)
            {
                _statusText.Visibility = System.Windows.Visibility.Visible;
                _toggleAllCheckBox.IsEnabled = false;
                _toggleAllCheckBox.IsChecked = false;
            }
            else
            {
                _statusText.Visibility = System.Windows.Visibility.Collapsed;
                _toggleAllCheckBox.IsEnabled = true;

                foreach (var pc in _pointClouds)
                {
                    var cb = new CheckBox
                    {
                        Content = pc.Name,
                        IsChecked = pc.IsVisible,
                        Tag = pc,
                        Margin = new Thickness(0, 2, 0, 2),
                        ToolTip = $"Element ID: {pc.Id.Value}"
                    };
                    cb.Click += OnPointCloudCheckboxClick;
                    _pointCloudList.Children.Add(cb);
                }

                // Update Toggle All state
                UpdateToggleAllState();
            }

            _suppress = false;
        }

        /// <summary>
        /// Updates the Toggle All checkbox based on individual checkbox states.
        /// </summary>
        private void UpdateToggleAllState()
        {
            if (_pointClouds.Count == 0)
            {
                _toggleAllCheckBox.IsChecked = false;
                return;
            }

            int visibleCount = 0;
            foreach (var pc in _pointClouds)
            {
                if (pc.IsVisible) visibleCount++;
            }

            if (visibleCount == _pointClouds.Count)
            {
                // All visible
                _toggleAllCheckBox.IsChecked = true;
            }
            else if (visibleCount == 0)
            {
                // All hidden
                _toggleAllCheckBox.IsChecked = false;
            }
            else
            {
                // Mixed — show as unchecked (clicking will show all)
                _toggleAllCheckBox.IsChecked = false;
            }
        }

        // ── Event handlers ────────────────────────────────────────────────

        private void OnToggleAllClick(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;
            if (_pointClouds.Count == 0) return;

            bool showAll = _toggleAllCheckBox.IsChecked == true;

            // Collect all point cloud IDs
            var ids = new List<ElementId>();
            foreach (var pc in _pointClouds)
            {
                ids.Add(pc.Id);
            }

            // Toggle visibility
            App.ToggleHandler.ElementIds = ids;
            App.ToggleHandler.Show = showAll;
            App.ToggleHandler.OnDone = OnToggleComplete;
            App.ToggleEvent.Raise();
        }

        private void OnPointCloudCheckboxClick(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;
            if (!(sender is CheckBox cb)) return;
            if (!(cb.Tag is PointCloudInfo pc)) return;

            bool show = cb.IsChecked == true;

            // Toggle this single point cloud
            App.ToggleHandler.ElementIds = new List<ElementId> { pc.Id };
            App.ToggleHandler.Show = show;
            App.ToggleHandler.OnDone = OnToggleComplete;
            App.ToggleEvent.Raise();
        }
    }
}
