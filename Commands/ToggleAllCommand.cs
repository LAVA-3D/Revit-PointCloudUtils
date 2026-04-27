using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.PointClouds;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace PointCloudUtils.Commands
{
    /// <summary>
    /// Toggles the visibility of all point clouds in the active view.
    /// If every point cloud is currently visible → hides them all.
    /// If any point cloud is hidden (or none exist yet) → shows them all.
    ///
    /// Registered as a Type="Command" in the .addin manifest so Revit exposes it
    /// in Manage ▸ Keyboard Shortcuts, where users can bind any key they like.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ToggleAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                if (uidoc == null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }

                var doc  = uidoc.Document;
                var view = doc.ActiveView;

                if (view == null)
                {
                    message = "No active view.";
                    return Result.Failed;
                }

                // ── Collect all PointCloudInstance elements ───────────────
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(PointCloudInstance));

                var ids       = new List<ElementId>();
                bool allVisible = true;

                foreach (Element elem in collector)
                {
                    if (!(elem is PointCloudInstance pc)) continue;
                    ids.Add(pc.Id);
                    if (pc.IsHidden(view))
                        allVisible = false;
                }

                if (ids.Count == 0)
                    return Result.Succeeded; // Nothing to toggle

                // All visible → hide all; any hidden → show all
                bool showAll = !allVisible;

                // ── Apply visibility change ───────────────────────────────
                using (var tx = new Transaction(doc,
                    showAll ? "Show All Point Clouds" : "Hide All Point Clouds"))
                {
                    tx.Start();

                    if (showAll)
                        view.UnhideElements(ids);
                    else
                        view.HideElements(ids);

                    tx.Commit();
                }

                // ── Sync the floating window if it is open ────────────────
                // RefreshPointClouds() calls ExternalEvent.Raise() which is
                // thread-safe and will fire after this command returns.
                App.Window?.RefreshPointClouds();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
