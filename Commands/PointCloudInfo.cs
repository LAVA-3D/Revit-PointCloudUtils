using Autodesk.Revit.DB;

namespace PointCloudUtils.Commands
{
    /// <summary>
    /// Simple data class representing a point cloud and its visibility state.
    /// Used to pass data between the Revit thread and the WPF window.
    /// </summary>
    public class PointCloudInfo
    {
        /// <summary>Element ID of the PointCloudInstance.</summary>
        public ElementId Id { get; set; } = ElementId.InvalidElementId;

        /// <summary>Display name (file name or element name).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>True if visible in the active view, false if hidden.</summary>
        public bool IsVisible { get; set; } = true;

        public override string ToString() => Name;
    }
}
