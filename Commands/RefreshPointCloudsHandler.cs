using Autodesk.Revit.DB;
using Autodesk.Revit.DB.PointClouds;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;

namespace PointCloudUtils.Commands
{
    /// <summary>
    /// Queries the document for all point clouds and their visibility state.
    /// Set OnDone callback before calling ExternalEvent.Raise().
    /// </summary>
    public class RefreshPointCloudsHandler : IExternalEventHandler
    {
        /// <summary>
        /// Callback invoked with the list of point clouds.
        /// Runs on the Revit thread — use Dispatcher.InvokeAsync for UI updates.
        /// </summary>
        public Action<List<PointCloudInfo>>? OnDone { get; set; }

        public void Execute(UIApplication app)
        {
            var results = new List<PointCloudInfo>();

            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    OnDone?.Invoke(results);
                    return;
                }

                var doc = uidoc.Document;
                var view = doc.ActiveView;

                if (view == null)
                {
                    OnDone?.Invoke(results);
                    return;
                }

                // Find all PointCloudInstance elements in the document
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(PointCloudInstance));

                foreach (Element elem in collector)
                {
                    if (!(elem is PointCloudInstance pc)) continue;

                    // Get display name — prefer the file name, fall back to element name
                    string name = GetPointCloudName(pc);

                    // Check visibility in the active view
                    // Use IsHidden method which checks if element is hidden by the Hide Element command
                    bool isHidden = pc.IsHidden(view);

                    results.Add(new PointCloudInfo
                    {
                        Id = pc.Id,
                        Name = name,
                        IsVisible = !isHidden
                    });
                }

                // Sort alphabetically by name
                results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // Handler must never throw — return empty list on error
            }
            finally
            {
                OnDone?.Invoke(results);
                OnDone = null;
            }
        }

        /// <summary>
        /// Gets a friendly display name for the point cloud.
        /// </summary>
        private static string GetPointCloudName(PointCloudInstance pc)
        {
            // The Name property typically contains the file name or a descriptive name
            string name = pc.Name;
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            // Last resort: use element ID
            return $"Point Cloud {pc.Id.Value}";
        }

        public string GetName() => "Refresh Point Clouds";
    }
}
