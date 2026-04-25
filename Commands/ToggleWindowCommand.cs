using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace PointCloudUtils.Commands
{
    /// <summary>
    /// Toggles the Point Cloud Visibility window open/closed.
    /// Can be assigned a keyboard shortcut in Revit.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ToggleWindowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                App.ToggleWindow(commandData.Application);
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
