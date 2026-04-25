# 02 — Commands, Handlers & UI Patterns

> The runtime patterns for adding features to your Revit addin.

---

## Architecture Overview

Every user interaction follows the same flow:

```
  WPF Window (UI thread)              Revit API thread
  ──────────────────────              ─────────────────
  User clicks button
       │
       ▼
  Set handler properties
  (Axis, Name, OnDone, etc.)
       │
       ▼
  ExternalEvent.Raise()  ──────►  Revit calls Handler.Execute()
                                       │
                                       ▼
                                  Open Transaction
                                  Modify model / view
                                  Commit Transaction
                                       │
                                       ▼
                                  OnDone?.Invoke()  ──────►  UI updates
```

**Why?** A modeless WPF window runs on its own thread. The Revit API is single-threaded and can only be called on Revit's main thread. The `ExternalEvent` + `IExternalEventHandler` pattern bridges this gap safely.

---

## 1. IExternalCommand — Ribbon Commands

Commands registered in the `.addin` manifest as `Type="Command"`. Users can assign keyboard shortcuts to these in Revit.

### Transaction Modes

| Mode | When to use | Example |
|---|---|---|
| `TransactionMode.Manual` | Command modifies the model/view | Move, rotate, create elements |
| `TransactionMode.ReadOnly` | Command only reads or opens a window | Toggle window, show info dialog |

### Template Method Pattern

When you have multiple commands that share the same logic but differ by a parameter (e.g., axis, direction, element type), use a base class:

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace {Namespace}.Commands
{
    // Define enums for your parameterised behaviour
    public enum Axis { X, Y, Z }
    public enum Direction { Positive, Negative }

    /// <summary>
    /// Base class — subclasses just declare the parameters.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public abstract class MyCommandBase : IExternalCommand
    {
        protected abstract Axis Axis { get; }
        protected abstract Direction Direction { get; }

        public Result Execute(ExternalCommandData commandData, ref string message,
                              ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;

                // ── Validate preconditions ──────────────────────────────
                if (!(doc.ActiveView is View3D view3D))
                {
                    message = "Please activate a 3D view first.";
                    return Result.Failed;
                }

                // ── Compute what to change ──────────────────────────────
                double increment = Config.GetDoubleSetting("IncrementMm", 100) / 304.8;
                double sign = Direction == Direction.Positive ? 1.0 : -1.0;
                XYZ shift = Axis switch
                {
                    Axis.X => new XYZ(sign * increment, 0, 0),
                    Axis.Y => new XYZ(0, sign * increment, 0),
                    Axis.Z => new XYZ(0, 0, sign * increment),
                    _      => XYZ.Zero,
                };

                // ── Apply in a transaction ──────────────────────────────
                using (var tx = new Transaction(doc, $"My Command {Axis}"))
                {
                    tx.Start();
                    // ... apply your changes here ...
                    tx.Commit();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ── Concrete commands (one per parameter combination) ─────────────

    [Transaction(TransactionMode.Manual)]
    public class MyCommandXPos : MyCommandBase
    {
        protected override Axis Axis => Axis.X;
        protected override Direction Direction => Direction.Positive;
    }

    [Transaction(TransactionMode.Manual)]
    public class MyCommandXNeg : MyCommandBase
    {
        protected override Axis Axis => Axis.X;
        protected override Direction Direction => Direction.Negative;
    }

    // ... more variants ...
}
```

### Simple ReadOnly Command (Toggle Window)

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace {Namespace}.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public class ToggleWindowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message,
                              ElementSet elements)
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
```

### Registering commands in `.addin`

Each concrete command class gets its own `<AddIn Type="Command">` entry:

```xml
<AddIn Type="Command">
  <Name>My Command +X</Name>
  <Description>Does the thing in the +X direction</Description>
  <Assembly>...\{AddinName}.dll</Assembly>
  <FullClassName>{Namespace}.Commands.MyCommandXPos</FullClassName>
  <ClientId>{unique-GUID}</ClientId>
  <VendorId>{VendorId}</VendorId>
  <VendorDescription>{VendorDescription}</VendorDescription>
</AddIn>
```

---

## 2. ExternalEvent + IExternalEventHandler — Modeless Operations

This is the core pattern for calling the Revit API from a modeless window.

### Handler Template

```csharp
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace {Namespace}.Commands
{
    /// <summary>
    /// Performs {operation} on the active view.
    /// Set properties before calling ExternalEvent.Raise().
    /// </summary>
    public class MyHandler : IExternalEventHandler
    {
        // ── Properties set by the window before Raise() ──────────────
        public string? SomeParameter { get; set; }
        public Action? OnDone        { get; set; }    // optional callback

        public void Execute(UIApplication app)
        {
            try
            {
                // ── Get the active document / view ───────────────────
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null) return;
                var doc = uidoc.Document;
                var view = doc.ActiveView as View3D;
                if (view == null) return;

                // ── Do work in a transaction ─────────────────────────
                using var tx = new Transaction(doc, "My Operation");
                tx.Start();
                // ... modify model / view ...
                tx.Commit();

                // ── Notify the window ────────────────────────────────
                OnDone?.Invoke();
            }
            catch { }           // handler must never throw
            finally
            {
                OnDone = null;  // prevent accidental re-fire
            }
        }

        public string GetName() => "My Handler";
    }
}
```

### Registering in `App.cs`

```csharp
// Static handler + event pairs
public static readonly MyHandler MyHandler = new MyHandler();
public static ExternalEvent MyEvent = null!;

// In OnStartup():
MyEvent = ExternalEvent.Create(MyHandler);
```

### Calling from the window

```csharp
// Set handler state FIRST, then raise
App.MyHandler.SomeParameter = "value";
App.MyHandler.OnDone = () =>
{
    // This runs on the Revit thread — update local state here
    _someField = newValue;
};
App.MyEvent.Raise();
```

### The OnDone callback pattern

Handlers often need to tell the window "I'm done, here's the result." The pattern:

1. Window sets `handler.OnDone = () => { ... }` before `Raise()`.
2. Handler calls `OnDone?.Invoke()` at the end of `Execute()`.
3. Handler nulls out `OnDone` in a `finally` block to prevent re-invocation.
4. The callback runs on the Revit thread — safe for setting local state but **not** for WPF control updates. Use `Dispatcher.InvokeAsync()` if you need to update UI.

### SuppressHistoryRecord pattern

If your addin tracks changes via `DocumentChanged` and has its own undo system, you need to suppress double-recording when your own handlers make changes:

```csharp
// In App.cs:
public static bool SuppressHistoryRecord;

// In your handler:
App.SuppressHistoryRecord = true;
try
{
    using var tx = new Transaction(doc, "My Operation");
    tx.Start();
    // ... changes ...
    tx.Commit();
}
finally
{
    App.SuppressHistoryRecord = false;
}

// In your DocumentChanged handler:
if (SuppressHistoryRecord)
{
    // Our own transaction — skip auto-push
    return;
}
// External change — push to undo stack
```

---

## 3. Modeless WPF Window

A floating panel that stays open while the user works in Revit.

### Template

```csharp
using {Namespace}.Commands;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace {Namespace}
{
    public class {AddinName}Window : Window
    {
        // ── Controls ──────────────────────────────────────────────────
        private ComboBox _combo  = null!;
        private Button   _myBtn  = null!;

        // ── State ─────────────────────────────────────────────────────
        private List<MyEntry> _entries = new List<MyEntry>();
        private string?       _storePath;
        private bool          _suppress;    // suppress combo events during refresh

        // ─────────────────────────────────────────────────────────────

        public {AddinName}Window()
        {
            Title  = "{AddinName}";
            Width  = 265;
            SizeToContent = SizeToContent.Height;
            ResizeMode    = ResizeMode.NoResize;
            Topmost       = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 240; Top = 160;

            BuildUI();

            // Keyboard shortcuts
            KeyDown += OnKeyDown;
        }

        // ── Document loading ──────────────────────────────────────────

        /// <summary>
        /// Reloads data for the given document title.
        /// Called on first show and whenever the active document changes.
        /// </summary>
        public void LoadFor(string? docTitle)
        {
            _storePath = MyStore.GetPath(docTitle);
            _entries   = MyStore.Load(_storePath);
            RefreshCombo(_entries.Count > 0 ? 0 : -1);
        }

        // ── UI construction (code-behind, no XAML) ────────────────────

        private void BuildUI()
        {
            var root = new StackPanel { Margin = new Thickness(8) };

            // ComboBox for selecting entries
            _combo = new ComboBox
            {
                DisplayMemberPath = "Name",
                Margin = new Thickness(0, 0, 0, 4),
            };
            _combo.SelectionChanged += OnComboChanged;
            root.Children.Add(_combo);

            // Action button — triggers ExternalEvent
            _myBtn = MkBtn("Do Something", (_, __) =>
            {
                App.MyHandler.SomeParameter = "value";
                App.MyEvent.Raise();
            });
            root.Children.Add(_myBtn);

            Content = root;
        }

        // ── UI helpers ────────────────────────────────────────────────

        private static Button MkBtn(string text, RoutedEventHandler handler)
        {
            var b = new Button
            {
                Content = text,
                Padding = new Thickness(6, 3, 6, 3),
                Margin  = new Thickness(2),
            };
            b.Click += handler;
            return b;
        }

        // ── ComboBox refresh with suppression ─────────────────────────

        private void RefreshCombo(int selectIdx)
        {
            _suppress = true;              // suppress SelectionChanged
            _combo.ItemsSource = null;
            _combo.ItemsSource = _entries;
            if (_entries.Count > 0 && selectIdx >= 0)
                _combo.SelectedIndex = Math.Min(selectIdx, _entries.Count - 1);
            _suppress = false;
            SyncButtons();
        }

        private void OnComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress) return;         // ignore refresh-triggered events
            if (!(_combo.SelectedItem is MyEntry chosen)) return;

            // Set handler state, then raise
            App.MyHandler.SomeParameter = chosen.Name;
            App.MyEvent.Raise();
        }

        private void SyncButtons()
        {
            bool has = _combo.SelectedItem is MyEntry;
            _myBtn.IsEnabled = has;
        }

        // ── Keyboard shortcuts ────────────────────────────────────────

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (ctrl && e.Key == Key.Z)
            {
                // Ctrl+Z → your undo action
                e.Handled = true;
            }
        }

        // ── Prompt helper (for rename / add dialogs) ──────────────────

        /// <summary>
        /// Shows a simple input dialog owned by this window.
        /// Returns the entered text, or null if cancelled.
        /// </summary>
        private string? Prompt(string label, string defaultValue = "")
        {
            var win = new Window
            {
                Title  = "{AddinName}",
                Width  = 280,
                Height = 130,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner  = this,
            };

            string? result = null;
            var tb     = new TextBox { Text = defaultValue, Margin = new Thickness(0, 0, 0, 10) };
            var ok     = new Button  { Content = "OK",     Width = 70, IsDefault = true,
                                       Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button  { Content = "Cancel", Width = 70, IsCancel = true };

            ok.Click     += (_, __) => { result = tb.Text.Trim(); win.DialogResult = true; };
            cancel.Click += (_, __) => win.DialogResult = false;

            var row = new StackPanel { Orientation = Orientation.Horizontal,
                                       HorizontalAlignment = HorizontalAlignment.Right };
            row.Children.Add(ok);
            row.Children.Add(cancel);

            var stack = new StackPanel { Margin = new Thickness(12) };
            stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
            stack.Children.Add(tb);
            stack.Children.Add(row);

            win.Content = stack;
            tb.Loaded += (_, __) => { tb.Focus(); tb.SelectAll(); };
            win.ShowDialog();

            return result?.Length > 0 ? result : null;
        }
    }
}
```

### Key rules for modeless windows

| Rule | Why |
|---|---|
| **Never call Revit API directly** | Wrong thread. Always go through `ExternalEvent.Raise()`. |
| **Hide on close, don't destroy** | `Closing += (_, e) => { e.Cancel = true; Hide(); }` — preserves window position and state. |
| **Set `Topmost = true`** | Keeps the window floating above Revit. Lower it temporarily for pick operations (see AlignToFace pattern below). |
| **Pin to Revit's HWND** | `new WindowInteropHelper(this).Owner = Process.GetCurrentProcess().MainWindowHandle;` |
| **Use `Dispatcher.InvokeAsync` for cross-thread updates** | When Revit-thread events need to update WPF controls. |
| **Use a `_suppress` flag for ComboBox** | Prevents `SelectionChanged` from firing during programmatic `ItemsSource` refreshes. |

### Lowering Topmost for interactive picks

When a handler needs the user to click on the Revit viewport (e.g., pick an element or face), temporarily lower `Topmost`:

```csharp
// In your handler's Execute():
App.Window?.Dispatcher.Invoke(
    () => { if (App.Window != null) App.Window.Topmost = false; });

try
{
    var reference = uidoc.Selection.PickObject(
        Autodesk.Revit.UI.Selection.ObjectType.Face,
        "Click a face — Esc to cancel");
}
catch (Autodesk.Revit.Exceptions.OperationCanceledException)
{
    return;   // user pressed Escape
}
finally
{
    App.Window?.Dispatcher.Invoke(
        () => { if (App.Window != null) App.Window.Topmost = true; });
}
```

---

## 4. Data Persistence (No JSON Library)

### Why avoid `System.Text.Json`?

Revit loads addins into its own .NET hosting environment. Third-party JSON libraries can cause assembly version conflicts — especially across different Revit versions. A simple regex-based parser is zero-dependency and sufficient for flat configuration and data files.

### Data Model (POCO)

```csharp
using Autodesk.Revit.DB;
using System;

namespace {Namespace}.Commands
{
    /// <summary>
    /// A saved state with a user-defined name.
    /// All coordinates stored in Revit internal units (feet).
    /// </summary>
    public class MyEntry
    {
        public string   Name   { get; set; } = "New Entry";
        public double[] Min    { get; set; } = new double[3];
        public double[] Max    { get; set; } = new double[3];
        public double[] Origin { get; set; } = new double[3];

        // ── Factory: Revit object → entry ─────────────────────────────
        public static MyEntry FromRevitObject(string name, BoundingBoxXYZ box)
        {
            var e = new MyEntry { Name = name };
            e.UpdateFrom(box);
            return e;
        }

        /// <summary>Sync this entry with current Revit state (for autosave).</summary>
        public void UpdateFrom(BoundingBoxXYZ box)
        {
            Min    = V(box.Min);
            Max    = V(box.Max);
            Origin = V(box.Transform.Origin);
        }

        // ── Entry → Revit object ──────────────────────────────────────
        public BoundingBoxXYZ ToRevitObject()
        {
            Transform t = Transform.Identity;
            t.Origin = P(Origin);
            return new BoundingBoxXYZ { Transform = t, Min = P(Min), Max = P(Max) };
        }

        public MyEntry Clone(string newName)
        {
            var e = new MyEntry { Name = newName };
            e.UpdateFrom(ToRevitObject());
            return e;
        }

        // ── Helpers ───────────────────────────────────────────────────
        private static double[] V(XYZ v)      => new[] { v.X, v.Y, v.Z };
        private static XYZ      P(double[] a) => a?.Length >= 3
            ? new XYZ(a[0], a[1], a[2]) : XYZ.Zero;
    }
}
```

### JSON Store

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace {Namespace}.Commands
{
    /// <summary>
    /// Reads/writes {datafile}_{docTitle}.json in the addin folder.
    /// One file per Revit document title.
    /// </summary>
    internal static class MyStore
    {
        private static readonly string AddinDir =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        /// <summary>Per-document file path with sanitised title.</summary>
        public static string GetPath(string? docTitle)
        {
            string safe = Regex.Replace(docTitle ?? "untitled", @"[^\w\-]", "_").Trim('_');
            if (safe.Length == 0) safe = "untitled";
            return Path.Combine(AddinDir, $"mydata_{safe}.json");
        }

        // ── Load ──────────────────────────────────────────────────────

        public static List<MyEntry> Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new List<MyEntry>();
                return Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch { return new List<MyEntry>(); }   // fault-tolerant
        }

        private static List<MyEntry> Parse(string json)
        {
            var list = new List<MyEntry>();
            var arrMatch = Regex.Match(json, @"""entries""\s*:\s*\[");
            if (!arrMatch.Success) return list;

            // Walk the JSON character by character to extract { } blocks
            int pos = arrMatch.Index + arrMatch.Length;
            int depth = 0, start = -1;
            bool inStr = false;
            char prev = '\0';

            for (int i = pos; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && prev != '\\') inStr = !inStr;
                if (!inStr)
                {
                    if (c == '{') { if (depth++ == 0) start = i; }
                    else if (c == '}')
                    {
                        if (--depth == 0 && start >= 0)
                        {
                            var e = ParseEntry(json.Substring(start, i - start + 1));
                            if (e != null) list.Add(e);
                            start = -1;
                        }
                    }
                    else if (c == ']' && depth == 0) break;
                }
                prev = c;
            }
            return list;
        }

        private static MyEntry? ParseEntry(string block)
        {
            try
            {
                return new MyEntry
                {
                    Name   = PStr(block, "name") ?? "Entry",
                    Min    = PArr(block, "min"),
                    Max    = PArr(block, "max"),
                    Origin = PArr(block, "origin"),
                };
            }
            catch { return null; }
        }

        /// <summary>Extract a string value by key.</summary>
        private static string? PStr(string s, string key)
        {
            var m = Regex.Match(s, $@"""{key}""\s*:\s*""((?:[^""\\]|\\.)*)""");
            return m.Success
                ? m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\")
                : null;
        }

        /// <summary>Extract a numeric array by key.</summary>
        private static double[] PArr(string s, string key)
        {
            var m = Regex.Match(s, $@"""{key}""\s*:\s*\[([^\]]*)\]");
            if (!m.Success) return new double[3];
            var parts = m.Groups[1].Value.Split(',');
            var r = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                r[i] = double.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
            return r;
        }

        // ── Save ──────────────────────────────────────────────────────

        public static void Save(string path, List<MyEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"entries\": [");
            for (int i = 0; i < entries.Count; i++)
                Append(sb, entries[i], last: i == entries.Count - 1);
            sb.AppendLine("  ]");
            sb.Append("}");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void Append(StringBuilder sb, MyEntry e, bool last)
        {
            sb.AppendLine("    {");
            sb.AppendLine($"      \"name\": {JStr(e.Name)},");
            sb.AppendLine($"      \"min\": {JArr(e.Min)},");
            sb.AppendLine($"      \"max\": {JArr(e.Max)},");
            sb.AppendLine($"      \"origin\": {JArr(e.Origin)}");
            sb.AppendLine(last ? "    }" : "    },");
        }

        private static string JStr(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string JArr(double[] a)
        {
            if (a == null) return "[]";
            var parts = new string[a.Length];
            for (int i = 0; i < a.Length; i++)
                parts[i] = a[i].ToString("R", CultureInfo.InvariantCulture);
            return "[" + string.Join(", ", parts) + "]";
        }
    }
}
```

### Key design decisions

- **Per-document files**: `mydata_{sanitisedTitle}.json` — one file per Revit document. Sanitise the title with `Regex.Replace(title, @"[^\w\-]", "_")`.
- **Fault-tolerant loading**: Any parse error returns an empty list. The addin always starts.
- **Invariant culture**: Always use `CultureInfo.InvariantCulture` for numbers — avoids comma/period confusion across locales.
- **`"R"` format for doubles**: Full roundtrip precision — no floating-point loss across save/load cycles.

### Autosave on switch

When the user switches between saved entries, autosave the current state of the outgoing entry before applying the new one:

```csharp
// In your ApplyHandler.Execute():
if (ActiveEntry != null && AllEntries != null && StorePath != null)
{
    ActiveEntry.UpdateFrom(currentRevitState);      // capture current geometry
    MyStore.Save(StorePath, AllEntries);             // persist to disk
}
// Then apply the new entry...
```

This prevents data loss — any changes made since the last explicit save are captured automatically.

---

## 5. Undo/Redo History Pattern

A thread-safe, bounded dual-stack for tracking state changes independently of Revit's built-in undo system.

### Immutable Snapshot

```csharp
using Autodesk.Revit.DB;

namespace {Namespace}.Commands
{
    /// <summary>
    /// Immutable snapshot of the state you want to undo/redo.
    /// Captures the values at a point in time.
    /// </summary>
    public sealed class StateSnapshot
    {
        public XYZ       Min       { get; }
        public XYZ       Max       { get; }
        public Transform Transform { get; }

        public StateSnapshot(BoundingBoxXYZ box)
        {
            Min       = box.Min;
            Max       = box.Max;
            Transform = box.Transform;
        }

        public BoundingBoxXYZ ToRevitObject() => new BoundingBoxXYZ
        {
            Min       = Min,
            Max       = Max,
            Transform = Transform,
        };

        public bool IsAlmostEqualTo(StateSnapshot other)
        {
            if (other == null) return false;
            return Min.IsAlmostEqualTo(other.Min)
                && Max.IsAlmostEqualTo(other.Max)
                && Transform.AlmostEqual(other.Transform);
        }
    }
}
```

### History Manager

```csharp
using System;
using System.Collections.Generic;

namespace {Namespace}.Commands
{
    /// <summary>
    /// Bounded undo/redo history. Thread-safe (locked).
    ///
    /// Usage:
    ///   1. Before a change: RecordBeforeMove(snapshot of current state).
    ///   2. Undo: PopUndo() -> get previous, PushRedo(current) -> apply previous.
    ///   3. Redo: PopRedo() -> get next, PushUndo(current) -> apply next.
    ///   4. On document switch: Clear().
    /// </summary>
    public sealed class StateHistory
    {
        private readonly object               _lock = new object();
        private readonly List<StateSnapshot>  _undo = new List<StateSnapshot>();
        private readonly List<StateSnapshot>  _redo = new List<StateSnapshot>();

        public int MaxDepth { get; set; } = 20;

        public bool CanUndo { get { lock (_lock) return _undo.Count > 0; } }
        public bool CanRedo { get { lock (_lock) return _redo.Count > 0; } }

        /// <summary>
        /// Fired when CanUndo/CanRedo may have changed.
        /// May fire on Revit's thread — use Dispatcher.InvokeAsync in UI subscribers.
        /// </summary>
        public event Action? HistoryChanged;

        public void RecordBeforeMove(StateSnapshot snapshot)
        {
            lock (_lock)
            {
                _undo.Add(snapshot);
                TrimFront(_undo, MaxDepth);
                _redo.Clear();           // new move invalidates redo branch
            }
            HistoryChanged?.Invoke();
        }

        public StateSnapshot? PopUndo()
        {
            StateSnapshot? snap;
            lock (_lock)
            {
                if (_undo.Count == 0) return null;
                snap = _undo[_undo.Count - 1];
                _undo.RemoveAt(_undo.Count - 1);
            }
            HistoryChanged?.Invoke();
            return snap;
        }

        public StateSnapshot? PopRedo()
        {
            StateSnapshot? snap;
            lock (_lock)
            {
                if (_redo.Count == 0) return null;
                snap = _redo[_redo.Count - 1];
                _redo.RemoveAt(_redo.Count - 1);
            }
            HistoryChanged?.Invoke();
            return snap;
        }

        public void PushRedo(StateSnapshot snapshot)
        {
            lock (_lock) { _redo.Add(snapshot); TrimFront(_redo, MaxDepth); }
            HistoryChanged?.Invoke();
        }

        public void PushUndo(StateSnapshot snapshot)
        {
            lock (_lock) { _undo.Add(snapshot); TrimFront(_undo, MaxDepth); }
            HistoryChanged?.Invoke();
        }

        public void Clear()
        {
            lock (_lock) { _undo.Clear(); _redo.Clear(); }
            HistoryChanged?.Invoke();
        }

        private static void TrimFront<T>(List<T> list, int maxDepth)
        {
            while (list.Count > maxDepth) list.RemoveAt(0);
        }
    }
}
```

### Wiring to the window

```csharp
// In App.cs:
public static readonly StateHistory History = new StateHistory();

// In your window constructor:
App.History.HistoryChanged += () =>
    Dispatcher.InvokeAsync(SyncUndoRedo);

// In your window:
private void SyncUndoRedo()
{
    _undoBtn.IsEnabled = App.History.CanUndo;
    _redoBtn.IsEnabled = App.History.CanRedo;
}
```

---

## 6. Revit Event Subscriptions

Subscribe to these in `App.OnStartup()` to track document and view lifecycle:

```csharp
// ── Document opened: reload window data, clear history ───────────
app.ControlledApplication.DocumentOpened += (_, e) =>
{
    History.Clear();
    // Reset any view-specific tracking state
    ActiveViewId = null;
    LastKnownState = null;
    // Seed from the document's already-active view
    if (e.Document.ActiveView is View3D v)
    {
        ActiveViewId = v.Id;
        LastKnownState = /* capture initial state */;
    }
    Window?.LoadFor(e.Document.Title);
};

// ── View activated: detect when user switches views ──────────────
app.ViewActivated += (_, e) =>
{
    if (e.CurrentActiveView is View3D view3d &&
        e.PreviousActiveView?.Id != e.CurrentActiveView.Id)
    {
        History.Clear();
        ActiveViewId = view3d.Id;
        LastKnownState = /* capture state */;
        Window?.OnViewActivated();
    }
};

// ── Document changed: track external modifications ───────────────
app.ControlledApplication.DocumentChanged += OnDocumentChanged;
```

### DocumentChanged handler

Detects changes made outside your addin (e.g., user dragging handles, other tools):

```csharp
private static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
{
    try
    {
        if (ActiveViewId == null) return;
        if (!e.GetModifiedElementIds().Contains(ActiveViewId)) return;

        var document = e.GetDocument();
        if (!(document.GetElement(ActiveViewId) is View3D view)) return;

        var current = /* capture current state */;

        if (SuppressHistoryRecord)
        {
            // Our own transaction — just keep the cache up to date
            LastKnownState = current;
            return;
        }

        // External change — push pre-change state onto undo stack
        if (LastKnownState != null && /* state actually changed */)
            History.RecordBeforeMove(LastKnownState);

        LastKnownState = current;
    }
    catch { }
}
```

---

## 7. Checklist: Adding a New Feature

Follow these steps each time you add a new operation:

1. **Create handler class** in `Commands/{Feature}Handler.cs`
   - Implement `IExternalEventHandler`
   - Add properties that the window will set before `Raise()`
   - Implement `Execute(UIApplication app)` with null checks and transaction

2. **Add static fields in `App.cs`**
   ```csharp
   public static readonly MyNewHandler MyNewHandler = new MyNewHandler();
   public static ExternalEvent MyNewEvent = null!;
   ```

3. **Create ExternalEvent in `App.OnStartup()`**
   ```csharp
   MyNewEvent = ExternalEvent.Create(MyNewHandler);
   ```

4. **Add UI controls in your window**
   - Button, menu item, or other trigger
   - Wire click handler to set properties + `Raise()`

5. **(If using commands)** Add `IExternalCommand` class + `.addin` entry

6. **Test**: Build, deploy, toggle window, verify the feature works and errors are handled gracefully.

---

**Previous**: [01 — Project Scaffold](01-project-scaffold.md)
**Next**: [03 — CI/CD & Installer](03-ci-and-installer.md)
