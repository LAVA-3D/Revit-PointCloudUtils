using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PointCloudUtils.Commands;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;

namespace PointCloudUtils
{
    public class App : IExternalApplication
    {
        // ── Modeless window singleton ─────────────────────────────────────
        public static PointCloudUtilsWindow? Window;

        // ── ExternalEvent handlers ────────────────────────────────────────
        public static readonly RefreshPointCloudsHandler RefreshHandler = new RefreshPointCloudsHandler();
        public static readonly ToggleVisibilityHandler ToggleHandler = new ToggleVisibilityHandler();

        public static ExternalEvent RefreshEvent = null!;
        public static ExternalEvent ToggleEvent = null!;

        // ── Cached UIApplication for event handlers ───────────────────────
        public static UIApplication? UiApp;

        // ─────────────────────────────────────────────────────────────────

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                // ── Create ExternalEvents ────────────────────────────────
                RefreshEvent = ExternalEvent.Create(RefreshHandler);
                ToggleEvent = ExternalEvent.Create(ToggleHandler);

                // ── Subscribe to Revit events ────────────────────────────
                app.ControlledApplication.DocumentOpened += (_, e) =>
                {
                    Window?.RefreshPointClouds();
                };

                app.ControlledApplication.DocumentClosed += (_, e) =>
                {
                    Window?.ClearPointClouds();
                };

                app.ViewActivated += (_, e) =>
                {
                    // Refresh when view changes — visibility may differ per view
                    if (e.PreviousActiveView?.Id != e.CurrentActiveView?.Id)
                    {
                        Window?.RefreshPointClouds();
                    }
                };

                // ── Build ribbon UI ──────────────────────────────────────
                RibbonPanel panel = app.CreateRibbonPanel("Point Cloud");
                string dll = Assembly.GetExecutingAssembly().Location;

                var toggleData = new PushButtonData(
                    "TogglePointCloudVisibility",
                    "Point Cloud\nVisibility",
                    dll,
                    "PointCloudUtils.Commands.ToggleWindowCommand")
                {
                    ToolTip = "Show or hide the Point Cloud Visibility panel.\n\nQuickly toggle visibility of individual point clouds without opening the Visibility/Graphics dialog.",
                    Image = MakePanelIcon(16),
                    LargeImage = MakePanelIcon(32),
                };
                panel.AddItem(toggleData);

                return Result.Succeeded;
            }
            catch
            {
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        // ── Window management ─────────────────────────────────────────────

        public static void ToggleWindow(UIApplication uiapp)
        {
            UiApp = uiapp;

            if (Window == null)
            {
                Window = new PointCloudUtilsWindow();

                // X button hides instead of destroying — preserves position and state
                Window.Closing += (_, e) =>
                {
                    e.Cancel = true;
                    Window.Hide();
                };

                Window.Show();

                // Pin above Revit's main window (set after Show so the HWND exists)
                new WindowInteropHelper(Window).Owner =
                    Process.GetCurrentProcess().MainWindowHandle;

                // Initial load of point clouds
                Window.RefreshPointClouds();
            }
            else if (!Window.IsVisible)
            {
                Window.RefreshPointClouds();
                Window.Show();
            }
            else
            {
                Window.Hide();
            }
        }

        // ── Icon generation (point cloud icon) ────────────────────────────

        /// <summary>
        /// Generates a point cloud icon programmatically using GDI+.
        /// Shows scattered dots to represent a point cloud.
        /// </summary>
        private static BitmapSource MakePanelIcon(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(70, 130, 180)); // Steel blue background
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Draw scattered points to represent a point cloud
                float dotSize = Math.Max(2f, size / 8f);
                using (var brush = new SolidBrush(Color.White))
                {
                    // Create a grid of points with some variation
                    int margin = size / 6;
                    int spacing = (size - 2 * margin) / 3;

                    for (int row = 0; row < 4; row++)
                    {
                        for (int col = 0; col < 4; col++)
                        {
                            float x = margin + col * spacing - dotSize / 2;
                            float y = margin + row * spacing - dotSize / 2;

                            // Add slight offset for organic look
                            if ((row + col) % 2 == 0)
                            {
                                x += spacing * 0.15f;
                                y += spacing * 0.1f;
                            }

                            g.FillEllipse(brush, x, y, dotSize, dotSize);
                        }
                    }
                }
            }

            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                ms.Position = 0;
                return new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad).Frames[0];
            }
        }
    }
}
