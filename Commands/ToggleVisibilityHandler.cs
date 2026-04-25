using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace PointCloudUtils.Commands
{
    /// <summary>
    /// Shows or hides specified point clouds in the active view.
    /// Set properties before calling ExternalEvent.Raise().
    /// </summary>
    public class ToggleVisibilityHandler : IExternalEventHandler
    {
        /// <summary>Element IDs of point clouds to show or hide.</summary>
        public List<ElementId> ElementIds { get; set; } = new List<ElementId>();

        /// <summary>True to show (unhide), false to hide.</summary>
        public bool Show { get; set; } = true;

        /// <summary>Callback invoked when operation completes.</summary>
        public Action? OnDone { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null) return;

                var doc = uidoc.Document;
                var view = doc.ActiveView;

                if (view == null) return;
                if (ElementIds == null || ElementIds.Count == 0) return;

                // Filter to only valid element IDs that exist in the document
                var validIds = new List<ElementId>();
                foreach (var id in ElementIds)
                {
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        var elem = doc.GetElement(id);
                        if (elem != null)
                        {
                            validIds.Add(id);
                        }
                    }
                }

                if (validIds.Count == 0) return;

                // Convert to ICollection for Revit API
                ICollection<ElementId> ids = validIds;

                using (var tx = new Transaction(doc, Show ? "Show Point Clouds" : "Hide Point Clouds"))
                {
                    tx.Start();

                    if (Show)
                    {
                        view.UnhideElements(ids);
                    }
                    else
                    {
                        view.HideElements(ids);
                    }

                    tx.Commit();
                }
            }
            catch
            {
                // Handler must never throw
            }
            finally
            {
                OnDone?.Invoke();
                OnDone = null;
                ElementIds = new List<ElementId>();
            }
        }

        public string GetName() => "Toggle Point Cloud Visibility";
    }
}
