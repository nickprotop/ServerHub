# Multiple Dashboards Design Document

**Status:** Design Phase
**Version:** 1.0
**Date:** 2026-02-08

## Table of Contents

1. [Overview](#overview)
2. [User Stories](#user-stories)
3. [Configuration Format](#configuration-format)
4. [CLI Interface](#cli-interface)
5. [UI/UX Design](#uiux-design)
6. [Architecture](#architecture)
7. [Migration Strategy](#migration-strategy)
8. [Implementation Plan](#implementation-plan)
9. [Testing Strategy](#testing-strategy)

---

## Overview

### Problem Statement

ServerHub currently supports only a single dashboard view displaying all configured widgets. Users monitoring multiple environments (production, development, staging) or different service categories (databases, web servers, network) need to see different sets of widgets for different contexts.

### Solution

Introduce **multiple named dashboards** that allow users to:
- Organize widgets into logical groups
- Switch between dashboards interactively (keyboard shortcut)
- Load specific dashboards via CLI
- Manage dashboards via Config Editor UI (CRUD operations)
- Use same widgets across dashboards with different settings

### Design Goals

1. **Backward Compatibility**: Existing single-dashboard configs continue to work
2. **Zero Breaking Changes**: No forced migration, graceful upgrade path
3. **Intuitive UX**: Dashboard switching should be as easy as Tab for widget focus
4. **Flexible Organization**: Same widget can appear in multiple dashboards
5. **Setting Overrides**: Per-dashboard overrides for refresh intervals, max lines, etc.

---

## User Stories

### US-1: Multiple Dashboard Creation
**As a** system administrator
**I want to** create multiple dashboards for different environments
**So that** I can quickly switch between production, staging, and development views

**Acceptance Criteria:**
- Can define multiple dashboards in config.yaml
- Each dashboard has its own widget list and layout
- One dashboard is marked as default

### US-2: CLI Dashboard Selection
**As a** power user
**I want to** start ServerHub with a specific dashboard
**So that** I can go directly to the view I need

**Acceptance Criteria:**
- `serverhub --dashboard production` loads production dashboard
- `serverhub --list-dashboards` shows available dashboards
- Invalid dashboard name shows helpful error

### US-3: Interactive Dashboard Switching
**As a** operator
**I want to** switch between dashboards without restarting
**So that** I can monitor different systems quickly

**Acceptance Criteria:**
- F4 key opens dashboard picker
- Arrow keys navigate, Enter selects
- UI rebuilds with new dashboard
- Pause state is preserved

### US-4: Dashboard Management UI
**As a** configurator
**I want to** create, edit, and delete dashboards via UI
**So that** I don't have to manually edit YAML

**Acceptance Criteria:**
- F2 Config Editor has Dashboards tab
- Can create new dashboard (with template option)
- Can edit dashboard name and settings
- Can delete dashboard (with confirmation)
- Can set default dashboard

### US-5: Legacy Config Support
**As an** existing user
**I want to** upgrade without breaking my config
**So that** my dashboards continue to work

**Acceptance Criteria:**
- Old configs load without errors
- Legacy config treated as "default" dashboard
- Optional migration command available
- Migration creates backup before modifying

---

## Configuration Format

### Multi-Dashboard Config

```yaml
# Global settings (apply to all dashboards unless overridden)
default_refresh: 5
max_lines_per_widget: 20
show_truncation_indicator: true

# Storage configuration (shared across all dashboards)
storage:
  enabled: true
  database_path: ~/.config/serverhub/serverhub.db
  retention_days: 30

# Responsive breakpoints (shared)
breakpoints:
  double: 100
  triple: 160
  quad: 220

# Default dashboard (required for multi-dashboard configs)
default_dashboard: production

# Dashboard definitions
dashboards:
  production:
    # Dashboard-specific overrides (optional)
    default_refresh: 10
    max_lines_per_widget: 15

    widgets:
      alerts:
        path: alerts.sh
        refresh: 30
        location: bundled
      cpu:
        path: cpu.sh
        refresh: 2
        location: bundled
      memory:
        path: memory.sh
        refresh: 2
        location: bundled
      disk:
        path: disk.sh
        refresh: 10
        location: bundled

    layout:
      order:
        - alerts
        - cpu
        - memory
        - disk

  development:
    # Uses global settings (no overrides)

    widgets:
      cpu:
        path: cpu.sh
        refresh: 1  # Faster for dev
        location: bundled
      docker:
        path: docker.sh
        refresh: 5
        location: bundled
      logs:
        path: logs.sh
        refresh: 3
        location: bundled
      processes:
        path: processes.sh
        refresh: 2
        location: bundled

    layout:
      order: [cpu, docker, logs, processes]

  database:
    widgets:
      disk:
        path: disk.sh
        refresh: 30
        location: bundled
      processes:
        path: processes.sh
        refresh: 5
        location: bundled
      db-monitor:
        path: db-monitor.sh
        refresh: 10
        location: custom
        sha256: abc123...

    layout:
      order: [disk, processes, db-monitor]
```

### Legacy Config (Backward Compatible)

```yaml
# Old format - still supported
default_refresh: 5
max_lines_per_widget: 20

storage:
  enabled: true

widgets:
  cpu:
    path: cpu.sh
    refresh: 2
  memory:
    path: memory.sh
    refresh: 2

layout:
  order: [cpu, memory]
```

**Treatment:** Automatically treated as single dashboard named "default". No migration required for basic usage.

### Config Validation Rules

**Multi-Dashboard Configs:**
- MUST have `dashboards` dictionary
- MUST have `default_dashboard` field pointing to valid dashboard
- CANNOT have `widgets` or `layout` at root level
- Each dashboard MUST have `widgets` dictionary
- Dashboard names must be alphanumeric + hyphens/underscores

**Legacy Configs:**
- MUST have `widgets` at root level
- MUST NOT have `dashboards` field
- Treated as "default" dashboard internally

---

## CLI Interface

### New Command-Line Options

```bash
serverhub [config] [--dashboard NAME] [--list-dashboards] [--migrate] [OPTIONS]
```

#### --dashboard / -d

Load specific dashboard:
```bash
serverhub --dashboard production
serverhub -d dev
serverhub ~/config.yaml --dashboard staging
```

Default: Uses `default_dashboard` from config

#### --list-dashboards

List available dashboards and exit:
```bash
$ serverhub --list-dashboards

Available dashboards:

  production (default)
    Widgets: 4 (alerts, cpu, memory, disk)
    Layout: order (4 widgets)
    Refresh: 10s (overridden from global 5s)

  development
    Widgets: 4 (cpu, docker, logs, processes)
    Layout: order (4 widgets)
    Refresh: 5s (uses global setting)

  database
    Widgets: 3 (disk, processes, db-monitor)
    Layout: order (3 widgets)
```

#### --migrate

Migrate legacy config to multi-dashboard format:
```bash
$ serverhub --migrate

Migrating configuration...

✓ Backup created: ~/.config/serverhub/config.yaml.backup-20260208-143022
✓ Config migrated to multi-dashboard format
✓ Created "default" dashboard with 14 widgets

Migration complete. Run 'serverhub' to start.
```

Custom config:
```bash
serverhub ~/my-config.yaml --migrate
```

---

## UI/UX Design

### Dashboard Picker (F4)

**Trigger:** F4 key or Command Palette → "Switch Dashboard"

**Visual Design:**
```
┌─ Switch Dashboard ──────────────────────────────┐
│                                                  │
│  ▸ production (current)                          │
│    4 widgets • Last loaded: 2 minutes ago        │
│                                                  │
│    development                                   │
│    4 widgets • Fast refresh (1-5s intervals)     │
│                                                  │
│    database                                      │
│    3 widgets • 1 custom widget                   │
│                                                  │
│  ─────────────────────────────────────────────   │
│  ↑↓ Navigate  Enter Select  ESC Cancel          │
└──────────────────────────────────────────────────┘
```

**Behavior:**
- Modal dialog, center-aligned
- ↑/↓ keys navigate
- Enter confirms selection
- ESC cancels
- Shows metadata: widget count, current status, settings info
- Only shown if multiple dashboards exist

### Status Bar Indicator

**Single Dashboard (legacy):**
```
ServerHub • Paused • 14 widgets • Last refresh: 5s ago
```

**Multi-Dashboard:**
```
ServerHub • production • Paused • 4 widgets • Last refresh: 5s ago
            ^^^^^^^^^^
         Dashboard name (cyan, clickable in future)
```

### Config Editor (F2) - Dashboard Management

**New Tab: Dashboards**

Tabs: **[Dashboards]** [Widgets] [Reorder]

#### Dashboard List View

```
┌─ Configuration ──────────────────────────────────┐
│ [Dashboards] [Widgets] [Reorder]                │
│                                                   │
│  Dashboards (3):                                 │
│                                                   │
│  ▸ production (default) [4 widgets]              │
│    development [4 widgets]                       │
│    database [3 widgets]                          │
│                                                   │
│  ────────────────────────────────────────────    │
│  N New  E Edit  D Delete  S Set Default         │
│  ESC Close                                       │
└───────────────────────────────────────────────────┘
```

**Operations:**
- **N (New):** Create dashboard
- **E (Edit):** Edit selected dashboard
- **D (Delete):** Delete selected dashboard (with confirmation)
- **S (Set Default):** Mark as default dashboard
- **↑/↓:** Navigate list
- **Enter:** Switch to dashboard (closes config editor)
- **ESC:** Close

#### Create Dashboard Dialog (N)

```
┌─ New Dashboard ──────────────────────────────────┐
│                                                   │
│  Name: [________________]                         │
│        (alphanumeric, hyphens, underscores)       │
│                                                   │
│  Copy widgets from:                               │
│    ( ) Empty                                      │
│    (*) production                                 │
│    ( ) development                                │
│    ( ) database                                   │
│                                                   │
│  Override Settings:                               │
│  [ ] Default refresh: [__] seconds                │
│  [ ] Max lines: [__]                              │
│                                                   │
│  [Create] [Cancel]                                │
└───────────────────────────────────────────────────┘
```

**Validation:**
- Name must be unique
- Name must be valid identifier (no spaces, special chars)
- If copying, source dashboard must exist

#### Edit Dashboard Dialog (E)

```
┌─ Edit Dashboard: production ─────────────────────┐
│                                                   │
│  Name: [production________]                       │
│        (rename, must be unique)                   │
│                                                   │
│  Override Settings:                               │
│  [✓] Default refresh: [10] seconds                │
│  [ ] Max lines per widget: [__]                   │
│  [ ] Show truncation indicator                    │
│                                                   │
│  Widget Count: 4                                  │
│  Layout: order (4 widgets)                        │
│                                                   │
│  [Save] [Cancel]                                  │
└───────────────────────────────────────────────────┘
```

**Features:**
- Rename (validates uniqueness)
- Toggle setting overrides
- Enable/disable specific overrides
- Shows read-only dashboard stats

#### Delete Dashboard Dialog (D)

```
┌─ Delete Dashboard ───────────────────────────────┐
│                                                   │
│  Delete dashboard "development"?                  │
│                                                   │
│  This will permanently remove:                    │
│    • 4 widget configurations                      │
│    • Layout settings                              │
│                                                   │
│  Storage data will NOT be deleted.                │
│  (Historical data remains in database)            │
│                                                   │
│  [Delete] [Cancel]                                │
└───────────────────────────────────────────────────┘
```

**Restrictions:**
- Cannot delete last dashboard
- Cannot delete default dashboard (set another first)
- Requires confirmation

#### Widget Tab Enhancement

**Multi-Dashboard Mode:**

```
┌─ Configuration ──────────────────────────────────┐
│ [Dashboards] [Widgets] [Reorder]                │
│                                                   │
│  Dashboard: [production ▼]                        │
│                                                   │
│  Widgets (4):                                    │
│  ✓ alerts (alerts.sh) - 30s refresh             │
│  ✓ cpu (cpu.sh) - 2s refresh                    │
│  ✓ memory (memory.sh) - 2s refresh              │
│  ✓ disk (disk.sh) - 10s refresh                 │
│                                                   │
│  A Add  E Edit  D Delete  T Toggle  C Copy      │
│  ESC Close                                       │
└───────────────────────────────────────────────────┘
```

**Dashboard Dropdown:**
- Select which dashboard to edit
- Changes reflected in that dashboard only
- **C (Copy Widget):** Copy selected widget to another dashboard

**Copy Widget Dialog (C):**
```
┌─ Copy Widget ────────────────────────────────────┐
│                                                   │
│  Copy "cpu" to:                                   │
│                                                   │
│  [ ] development                                  │
│  [✓] database                                     │
│                                                   │
│  Keep settings:                                   │
│  (*) Copy all settings                            │
│  ( ) Use target dashboard defaults                │
│                                                   │
│  [Copy] [Cancel]                                  │
└───────────────────────────────────────────────────┘
```

---

## Architecture

### New Models

#### DashboardConfig (src/Models/DashboardConfig.cs)

```csharp
namespace ServerHub.Models;

public class DashboardConfig
{
    [YamlMember(Alias = "widgets")]
    public Dictionary<string, WidgetConfig> Widgets { get; set; } = new();

    [YamlMember(Alias = "layout")]
    public LayoutConfig? Layout { get; set; }

    // Dashboard-specific overrides (null = use global)
    [YamlMember(Alias = "default_refresh")]
    public int? DefaultRefresh { get; set; }

    [YamlMember(Alias = "max_lines_per_widget")]
    public int? MaxLinesPerWidget { get; set; }

    [YamlMember(Alias = "show_truncation_indicator")]
    public bool? ShowTruncationIndicator { get; set; }

    // Effective value methods
    public int GetEffectiveDefaultRefresh(int globalDefault) =>
        DefaultRefresh ?? globalDefault;

    public int GetEffectiveMaxLinesPerWidget(int globalMax) =>
        MaxLinesPerWidget ?? globalMax;

    public bool GetEffectiveShowTruncationIndicator(bool globalSetting) =>
        ShowTruncationIndicator ?? globalSetting;
}
```

#### DashboardContext (src/Models/DashboardContext.cs)

Runtime context passed to services:

```csharp
namespace ServerHub.Models;

public class DashboardContext
{
    public required string DashboardName { get; set; }
    public required DashboardConfig Dashboard { get; set; }
    public required ServerHubConfig GlobalConfig { get; set; }

    public int EffectiveDefaultRefresh =>
        Dashboard.GetEffectiveDefaultRefresh(GlobalConfig.DefaultRefresh);

    public int EffectiveMaxLinesPerWidget =>
        Dashboard.GetEffectiveMaxLinesPerWidget(GlobalConfig.MaxLinesPerWidget);

    public bool EffectiveShowTruncationIndicator =>
        Dashboard.GetEffectiveShowTruncationIndicator(GlobalConfig.ShowTruncationIndicator);
}
```

### Modified Models

#### ServerHubConfig (src/Models/ServerHubConfig.cs)

Add new fields (keep existing for backward compatibility):

```csharp
// NEW: Multi-dashboard support
[YamlMember(Alias = "dashboards")]
public Dictionary<string, DashboardConfig>? Dashboards { get; set; }

[YamlMember(Alias = "default_dashboard")]
public string? DefaultDashboard { get; set; }

// Helper methods
public bool IsMultiDashboard() =>
    Dashboards != null && Dashboards.Count > 0;

public List<string> GetDashboardNames()
{
    if (IsMultiDashboard())
        return Dashboards!.Keys.OrderBy(k => k).ToList();
    return new List<string> { "default" };
}

public DashboardConfig? GetDashboard(string name)
{
    if (IsMultiDashboard())
        return Dashboards!.GetValueOrDefault(name);

    // Legacy: wrap root widgets as "default"
    if (name == "default" && Widgets != null)
        return new DashboardConfig
        {
            Widgets = Widgets,
            Layout = Layout
        };

    return null;
}

public string GetDefaultDashboardName()
{
    if (IsMultiDashboard())
        return DefaultDashboard ?? Dashboards!.Keys.First();
    return "default";
}
```

### Service Updates

Services that currently take `ServerHubConfig` are updated to take `DashboardContext`:

**WidgetRefreshService:**
```csharp
public WidgetRefreshService(
    ScriptExecutor executor,
    WidgetProtocolParser parser,
    DashboardContext context,  // Changed
    StorageService? storageService = null)
```

**LayoutEngine:**
```csharp
public List<WidgetPlacement> CalculateLayout(
    DashboardContext context,  // Changed
    int terminalWidth,
    int terminalHeight)
```

**WidgetRenderer:**
```csharp
public WidgetRenderer(DashboardContext context)  // Changed
```

### Dashboard Switching Flow

```
User presses F4
    ↓
DashboardPickerDialog.Show()
    ↓
User selects dashboard, presses Enter
    ↓
SwitchDashboard(newDashboardName)
    ↓
  1. Save pause state
  2. Cancel all refresh timers
  3. Clear widget data caches
  4. Update _currentDashboardContext
  5. Close main window
  6. RebuildDashboard()
       ├─ LayoutEngine calculates new placements
       ├─ CreateMainWindow() with new widgets
       ├─ FocusManager reinitializes
       └─ Trigger initial refreshes
  7. Restore pause state
  8. Restart refresh timers
  9. Update status bar
```

---

## Migration Strategy

### CRITICAL DECISION: Embedded Config Format

**File:** `config.production.yaml` (generates `DefaultConfig.g.cs` via `generate-default-config.sh`)

**Question:** Should we change `config.production.yaml` from legacy to multi-dashboard format?

**Current State:**
- `config.production.yaml` = Legacy format (14 widgets at root level)
- Embedded in binary as `DefaultConfig.g.cs` at build time
- Used ONLY for first-time users (no existing config)

**Scenario Analysis:**

**Existing Users Upgrade:**
1. Have `~/.config/serverhub/config.yaml` (legacy format)
2. ServerHub loads THEIR file, not embedded default
3. Auto-migration detects legacy format
4. Creates backup → migrates to multi-dashboard
5. **Embedded config is never seen**

**New Users Install:**
1. No config file exists
2. ServerHub creates from embedded `DefaultConfig.g.cs`
3. Gets whatever format we embed

**Edge Cases:**
- User deletes config → Recreated from embedded (our choice of format)
- User runs `--init-config` → Creates new file from embedded
- Uninstall/reinstall keeping config → Existing file auto-migrated

**Decision:** ✅ Change `config.production.yaml` to multi-dashboard format

**Rationale:**
- ✅ Existing users: SAFE - auto-migration handles them gracefully
- ✅ New users: Get modern format immediately, can create more dashboards
- ✅ Documentation: Shows current best practices, not outdated examples
- ✅ Edge cases: All scenarios work correctly
- ✅ No breaking changes or data loss

**Implementation Plan:**

1. **Create multi-dashboard `config.production.yaml`:**
   ```yaml
   default_dashboard: production

   dashboards:
     production:  # Essential monitoring
       widgets: [alerts, cpu, memory, disk, sysinfo, network]

     development:  # Dev/debugging focus
       widgets: [cpu, memory, docker, logs, processes]

     services:  # Infrastructure monitoring
       widgets: [services, docker, netstat, updates, ssl-certs]
   ```

2. **Backup:** Save current as `config.production.legacy.yaml`

3. **Build:** No changes to `generate-default-config.sh` (already uses `config.production.yaml`)

4. **Local dev:** Keep `config.yaml`, `config.development.yaml` as legacy for testing both formats

### Auto-Detection

**ConfigManager.LoadConfig():**
```csharp
public ServerHubConfig LoadConfig(string configPath)
{
    var config = _deserializer.Deserialize<ServerHubConfig>(yaml);

    // Auto-detect legacy format
    if (NeedsMigration(config))
    {
        // Only auto-migrate default config path
        if (configPath == GetDefaultConfigPath())
        {
            Console.WriteLine("Legacy config detected. Migrating...");
            var migrated = MigrateLegacyConfig(config);
            BackupAndMigrateConfig(configPath, migrated);
            config = migrated;
        }
        else
        {
            // Custom config: suggest explicit migration
            Console.WriteLine("Warning: Legacy config format.");
            Console.WriteLine($"Run: serverhub {configPath} --migrate");
        }
    }

    ValidateConfig(config);
    return config;
}
```

### Migration Logic

```csharp
private bool NeedsMigration(ServerHubConfig config)
{
    return config.Widgets != null
        && config.Widgets.Count > 0
        && (config.Dashboards == null || config.Dashboards.Count == 0);
}

private ServerHubConfig MigrateLegacyConfig(ServerHubConfig config)
{
    return new ServerHubConfig
    {
        // Preserve global settings
        DefaultRefresh = config.DefaultRefresh,
        MaxLinesPerWidget = config.MaxLinesPerWidget,
        ShowTruncationIndicator = config.ShowTruncationIndicator,
        Storage = config.Storage,
        Breakpoints = config.Breakpoints,

        // Create dashboards section
        DefaultDashboard = "default",
        Dashboards = new Dictionary<string, DashboardConfig>
        {
            ["default"] = new DashboardConfig
            {
                Widgets = config.Widgets ?? new(),
                Layout = config.Layout
            }
        }
    };
}

private void BackupAndMigrateConfig(string configPath, ServerHubConfig migratedConfig)
{
    var backupPath = $"{configPath}.backup-{DateTime.Now:yyyyMMdd-HHmmss}";
    File.Copy(configPath, backupPath);
    Console.WriteLine($"✓ Backup: {backupPath}");

    SaveConfig(migratedConfig, configPath);
    Console.WriteLine("✓ Migrated to multi-dashboard format");
}
```

### User Experience

**Scenario 1: Default config (auto-migrate)**
```bash
$ serverhub
Legacy config detected. Migrating...
✓ Backup: ~/.config/serverhub/config.yaml.backup-20260208-143022
✓ Migrated to multi-dashboard format
✓ Created "default" dashboard with 14 widgets

Starting dashboard...
```

**Scenario 2: Custom config (manual)**
```bash
$ serverhub ~/my-config.yaml
Warning: Legacy config format.
Run: serverhub ~/my-config.yaml --migrate

Continuing with legacy mode...
```

**Scenario 3: Explicit migration**
```bash
$ serverhub ~/my-config.yaml --migrate
Migrating configuration...

✓ Backup created: ~/my-config.yaml.backup-20260208-143022
✓ Config migrated to multi-dashboard format
✓ Created "default" dashboard

Run 'serverhub ~/my-config.yaml' to start.
```

---

## Implementation Plan

### Phase 1: Core Models
- Create `DashboardConfig.cs`
- Create `DashboardContext.cs`
- Modify `ServerHubConfig.cs` (add fields and helper methods)

### Phase 2: Config Management
- Update `ConfigManager.cs`:
  - Migration detection
  - Migration logic with backup
  - Enhanced validation for multi-dashboard

### Phase 3: CLI Arguments
- Add `--dashboard`, `--list-dashboards`, `--migrate` options
- Implement utility commands (list, migrate)
- Update command handler to pass dashboard name

### Phase 4: Dashboard Runtime
- Update `Program.cs`:
  - Add global variables for current dashboard
  - Update `RunDashboardAsync` signature
  - Implement `SwitchDashboard()` and `RebuildDashboard()`
- Update services to use `DashboardContext`

### Phase 5: Dashboard Picker UI
- Create `DashboardPickerDialog.cs`
- Add F4 keybinding
- Update status bar to show dashboard name

### Phase 6: Config Editor Enhancement
- Add Dashboards tab to `WidgetConfigDialog`
- Implement dashboard CRUD operations
- Add dashboard dropdown to Widget tab
- Implement copy widget across dashboards

### Phase 7: Config Hot-Reload
- Detect dashboard structure changes
- Handle current dashboard deletion
- Reload current dashboard on config change

### Phase 8: Testing & Documentation
- Unit tests for models
- Integration tests for switching
- Manual testing checklist
- Update README, CLAUDE.md

---

## Testing Strategy

### Unit Tests

**DashboardConfigTests:**
- Effective settings inheritance
- Override behavior
- Null handling

**ServerHubConfigTests:**
- `IsMultiDashboard()` detection
- `GetDashboard()` for multi/legacy
- `GetDashboardNames()` ordering
- `GetDefaultDashboardName()` resolution

**ConfigManagerTests:**
- Migration detection
- Migration correctness
- Backup creation
- Validation for multi-dashboard
- Validation for legacy

### Integration Tests

**DashboardSwitchingTests:**
- Switch dashboard → UI rebuilds
- Switch dashboard → timers restart
- Switch dashboard → pause state preserved
- Switch to invalid dashboard → error

**ConfigReloadTests:**
- Dashboard added → no reload
- Dashboard removed → handle gracefully
- Current dashboard modified → reload
- Dashboard structure changed → prompt

### Manual Testing Checklist

- [ ] Load legacy config → works without modification
- [ ] Migrate via `--migrate` → backup created
- [ ] Load multi-dashboard config → default dashboard loads
- [ ] `--dashboard dev` → loads correct dashboard
- [ ] `--list-dashboards` → shows all dashboards
- [ ] F4 → picker opens, selects dashboard
- [ ] F2 → Dashboards tab appears (multi-dashboard only)
- [ ] Create dashboard → saves to config
- [ ] Edit dashboard → updates config
- [ ] Delete dashboard → confirmation required
- [ ] Set default → updates config
- [ ] Widget tab dropdown → edits correct dashboard
- [ ] Copy widget → appears in target dashboard
- [ ] Storage data persists across switches
- [ ] Hot-reload config → current dashboard reloads
- [ ] Terminal resize during switch → no crash

---

## Open Questions

1. **Command Palette Integration:**
   - Add "Switch to <dashboard>" for each dashboard?
   - Keep just generic "Switch Dashboard" that opens picker?
   - **Decision:** Both - generic command + per-dashboard quick actions

2. **Dashboard Duplication:**
   - Allow "duplicate dashboard" feature in UI?
   - **Decision:** Phase 2 feature - implement after basic CRUD works

3. **Dashboard Import/Export:**
   - Allow exporting dashboard to separate YAML?
   - Allow importing dashboard from file?
   - **Decision:** Phase 2 feature - useful for sharing configurations

4. **Dashboard Templates:**
   - Pre-defined templates (production, development, database)?
   - Stored where (bundled assets, ~/.config/serverhub/templates/)?
   - **Decision:** Phase 2 feature - start with copy-from-existing

5. **Dashboard Filtering/Search:**
   - If user has 10+ dashboards, add search to picker?
   - **Decision:** Wait for user feedback - implement if needed

---

## Success Criteria

1. **Backward Compatibility:**
   - All existing configs load without errors
   - Legacy configs work unchanged (treated as "default" dashboard)

2. **Migration:**
   - Auto-migration creates backup
   - Migrated configs load successfully
   - All widgets preserved

3. **Multi-Dashboard Functionality:**
   - CLI loads correct dashboard
   - F4 picker switches dashboards
   - Status bar shows current dashboard
   - Dashboard CRUD operations work

4. **Storage:**
   - Widget data persists across dashboard switches
   - Same widget ID in multiple dashboards shares data

5. **Performance:**
   - Dashboard switch < 500ms
   - No memory leaks during repeated switches
   - Config validation < 100ms

6. **User Experience:**
   - Intuitive keyboard navigation
   - Clear visual feedback
   - Helpful error messages
   - No data loss scenarios

---

## Future Enhancements

### Phase 2 Features (post-MVP)
- Dashboard duplication
- Dashboard import/export
- Dashboard templates
- Dashboard grouping/favorites
- Fuzzy search in dashboard picker

### Phase 3 Features (advanced)
- Dashboard switching without UI rebuild (preserve state)
- Dashboard-specific storage databases
- Dashboard permissions/read-only mode
- Dashboard sharing/collaboration

---

## References

- ServerHub Architecture: `/home/nick/source/ServerHub/CLAUDE.md`
- Config Models: `/home/nick/source/ServerHub/src/Models/`
- UI Patterns: `/home/nick/source/ServerHub/src/UI/`
- SharpConsoleUI Documentation: https://github.com/nickprotop/ConsoleEx
