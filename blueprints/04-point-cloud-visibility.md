# 04 — Point Cloud Visibility Manager

> A modeless window for quickly toggling point cloud visibility in Revit.

---

## Problem Statement

Toggling point cloud visibility in Revit requires too many steps:

1. Type `VV` to open Visibility/Graphics dialog
2. Navigate to the "Point Clouds" tab
3. Find the point cloud and uncheck/check it
4. Click OK to apply

This workflow breaks concentration and slows down tasks like clash detection, scan-to-BIM modeling, or comparing different scan regions.

---

## Solution

A floating, always-on-top WPF window that:

- Lists all point clouds loaded in the current project
- Shows a checkbox for each point cloud (checked = visible, unchecked = hidden)
- Updates instantly with one click — no dialogs, no OK buttons
- Includes a "Toggle All" checkbox to show/hide all point clouds at once

### User Interface Mockup

```
┌─────────────────────────────────────┐
│  Point Cloud Visibility        [_] │
├─────────────────────────────────────┤
│  ☑ Toggle All                       │
├─────────────────────────────────────┤
│  ☑ Scan_Building_Exterior.rcp       │
│  ☐ Scan_Interior_Level1.rcp         │  ← Currently hidden
│  ☑ Scan_Interior_Level2.rcp         │
│  ☑ Scan_MEP_Routing.rcp             │
└─────────────────────────────────────┘
```

### Workflow Comparison

| Action | Current (VV Dialog) | With This Addin |
|--------|---------------------|-----------------|
| Hide one point cloud | 4+ clicks | 1 click |
| Hide all point clouds | 4+ clicks per cloud | 1 click |
| See the result | Must close dialog | Instant |
| Toggle while working | Loses focus | Window stays open |

---

## Technical Design

### Revit API Concepts

| Concept | API | Notes |
|---------|-----|-------|
| Find point clouds | `FilteredElementCollector` + `OfClass(typeof(PointCloudInstance))` | Returns all point cloud instances in the document |
| Check if hidden | `View.IsElementHiddenInView(ElementId)` | Returns true if element is hidden in the specified view |
| Hide elements | `View.HideElements(ICollection<ElementId>)` | Hides elements in the view (requires transaction) |
| Unhide elements | `View.UnhideElements(ICollection<ElementId>)` | Shows elements in the view (requires transaction) |
| Get element name | `Element.Name` or `PointCloudInstance.GetPointCloudFileName()` | Display name for the UI |

### Threading Model

The modeless WPF window runs on its own thread. All Revit API calls must go through `ExternalEvent` + `IExternalEventHandler`:

```
  WPF Window (UI thread)              Revit API thread
  ──────────────────────              ─────────────────
  User clicks checkbox
       │
       ▼
  Set handler properties:
  - ElementIds to toggle
  - Show or Hide flag
       │
       ▼
  ExternalEvent.Raise()  ──────►  Handler.Execute(UIApplication)
                                       │
                                       ▼
                                  using (Transaction tx)
                                  {
                                      View.HideElements() or
                                      View.UnhideElements()
                                  }
                                       │
                                       ▼
                                  OnDone callback  ──────►  Refresh UI checkboxes
```

### Handlers

#### 1. RefreshPointCloudsHandler

Queries the document for all point clouds and their visibility state.

**Input properties:**
- `OnDone: Action<List<PointCloudInfo>>` — callback with results

**Output (via callback):**
- List of `PointCloudInfo` objects containing:
  - `ElementId Id`
  - `string Name`
  - `bool IsVisible`

#### 2. ToggleVisibilityHandler

Shows or hides specified point clouds in the active view.

**Input properties:**
- `ElementIds: List<ElementId>` — point clouds to toggle
- `Show: bool` — true to show, false to hide
- `OnDone: Action` — callback when complete

**Behavior:**
- Opens a transaction
- Calls `View.HideElements()` or `View.UnhideElements()`
- Commits transaction
- Invokes `OnDone` callback

---

## File Structure

```
PointCloudUtils/
├── Commands/
│   ├── PointCloudInfo.cs            → Simple data class
│   ├── RefreshPointCloudsHandler.cs → IExternalEventHandler to query point clouds
│   ├── ToggleVisibilityHandler.cs   → IExternalEventHandler to hide/show
│   └── ToggleWindowCommand.cs       → IExternalCommand to toggle window
├── App.cs                           → IExternalApplication entry point
├── PointCloudUtilsWindow.cs         → Modeless WPF window (code-behind)
├── PointCloudUtils.csproj           → Build configuration
├── PointCloudUtils.addin            → Revit manifest
├── config.json                      → User settings (future use)
├── NuGet.config                     → NuGet sources
├── installer.iss                    → Inno Setup installer
└── .github/workflows/build.yml      → CI/CD pipeline
```

---

## Window Behavior

### Initialization

1. Window created on first toggle (not at Revit startup)
2. Pinned to Revit's main window via `WindowInteropHelper`
3. `Closing` event cancelled — window hides instead of destroying

### Refresh Triggers

The point cloud list refreshes when:

| Trigger | How |
|---------|-----|
| Window first shown | `LoadFor(docTitle)` called from `App.ToggleWindow()` |
| Document opened | `DocumentOpened` event → `Window.LoadFor()` |
| View activated | `ViewActivated` event → refresh visibility states |
| After toggle operation | `OnDone` callback → refresh checkboxes |

### Toggle All Logic

The "Toggle All" checkbox has three states conceptually:

| State | Meaning | Click Action |
|-------|---------|--------------|
| Checked | All point clouds visible | Hide all |
| Unchecked | All point clouds hidden | Show all |
| Indeterminate | Mixed visibility | Show all |

Implementation: Use a standard `CheckBox` with `IsThreeState="False"`. When clicked:
- If any point cloud is hidden → show all
- If all point clouds are visible → hide all

---

## Edge Cases

| Scenario | Behavior |
|----------|----------|
| No point clouds in project | Show message: "No point clouds found" |
| No active view | Disable checkboxes, show message |
| View doesn't support hiding | Gracefully handle exception |
| Point cloud deleted while window open | Refresh on next toggle attempt |
| Document closed | Clear list, disable controls |

---

## Future Enhancements

These are out of scope for the initial implementation but could be added later:

1. **Filter by name** — Search box to filter the point cloud list
2. **Per-view memory** — Remember visibility settings per view
3. **Keyboard shortcuts** — Number keys 1-9 to toggle first 9 point clouds
4. **Color coding** — Show point cloud color swatch next to name
5. **Isolation mode** — "Solo" button to hide all except selected
6. **Scan region visibility** — If using scan regions, toggle those too

---

## Implementation Checklist

- [ ] Create project scaffold (csproj, addin, gitignore, NuGet.config)
- [ ] Implement `App.cs` with ribbon button and ExternalEvents
- [ ] Implement `PointCloudInfo.cs` data class
- [ ] Implement `RefreshPointCloudsHandler.cs`
- [ ] Implement `ToggleVisibilityHandler.cs`
- [ ] Implement `ToggleWindowCommand.cs`
- [ ] Implement `PointCloudUtilsWindow.cs` with:
  - [ ] Point cloud list with checkboxes
  - [ ] Toggle All checkbox
  - [ ] Refresh on view change
- [ ] Add `config.json` (placeholder for future settings)
- [ ] Create `installer.iss`
- [ ] Create `.github/workflows/build.yml`
- [ ] Test manually in Revit 2026
- [ ] Create initial release

---

**Previous**: [03 — CI/CD & Installer](03-ci-and-installer.md)
