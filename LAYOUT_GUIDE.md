# ServerHub Layout Guide

ServerHub provides three powerful ways to control widget layout, giving you complete flexibility from simple auto-layout to precise grid control.

## Table of Contents
1. [Responsive Auto-Layout](#responsive-auto-layout)
2. [Option 2: Column Spanning](#option-2-column-spanning)
3. [Option 3: Full Row Widgets](#option-3-full-row-widgets)
4. [Option 4: Explicit Row Layout](#option-4-explicit-row-layout)
5. [Combining All Options](#combining-all-options)

---

## Responsive Auto-Layout

The simplest approach. Widgets automatically flow into columns based on terminal width.

**Breakpoints (default):**
- `< 100 cols`: 1 column (only priority 1 widgets)
- `100-160 cols`: 2 columns
- `160-220 cols`: 3 columns
- `220+ cols`: 4 columns

**Example:**
```yaml
widgets:
  cpu:
    path: cpu.sh
    priority: 1
  memory:
    path: memory.sh
    priority: 1
  disk:
    path: disk.sh
    priority: 2

layout:
  order:
    - cpu
    - memory
    - disk
```

**Result (3 columns):**
```
┌─────────┬─────────┬─────────┐
│   CPU   │ Memory  │  Disk   │
└─────────┴─────────┴─────────┘
```

---

## Option 2: Column Spanning

Make widgets span multiple columns for more space.

**Configuration:**
```yaml
widgets:
  docker:
    path: docker.sh
    column_span: 2  # Spans 2 columns

  processes:
    path: processes.sh
    column_span: 3  # Spans 3 columns
```

**Result (4 column layout):**
```
┌─────┬─────┬─────┬─────┐
│ CPU │  Memory   │Disk │  <- Memory spans 2
├─────┴───────────┴─────┤
│        Docker         │  <- Docker spans 2
├───────────────────────┤
│      Processes        │  <- Processes spans 3
└───────────────────────┘
```

**See:** `config-column-span.yaml`

---

## Option 3: Full Row Widgets

Make a widget take the entire row width.

**Configuration:**
```yaml
widgets:
  docker:
    path: docker.sh
    full_row: true  # Takes entire row

  processes:
    path: processes.sh
    full_row: true
```

**Result:**
```
┌─────┬────────┬──────┐
│ CPU │ Memory │ Disk │
├──────────────────────┤
│       Docker         │  <- Full row
├──────────────────────┤
│      Processes       │  <- Full row
└──────────────────────┘
```

**Use case:** Widgets with long content (container names, log lines, etc.)

**See:** `config-simple.yaml`

---

## Option 4: Explicit Row Layout

Complete control over widget placement. Define exactly which widgets go in each row.

**Configuration:**
```yaml
layout:
  rows:
    # Row 1: Three equal columns
    - widgets: [cpu, memory, disk]

    # Row 2: Two equal columns
    - widgets: [sysinfo, network]

    # Row 3: Single widget (takes full row)
    - widgets: [docker]

    # Row 4: Two columns
    - widgets: [services, logs]
```

**Result:**
```
┌─────────┬─────────┬─────────┐
│   CPU   │ Memory  │  Disk   │  <- Row 1
├─────────┴─────────┴─────────┤
│  SysInfo  │    Network      │  <- Row 2
├───────────┴─────────────────┤
│          Docker             │  <- Row 3
├───────────┬─────────────────┤
│ Services  │      Logs       │  <- Row 4
└───────────┴─────────────────┘
```

**Benefits:**
- Precise control over layout
- Group related widgets
- Prioritize important widgets
- Works great for wide widgets

**See:** `config-explicit-rows.yaml`

---

## Combining All Options

You can combine Options 2, 3, and 4 together!

**Configuration:**
```yaml
widgets:
  docker:
    path: docker.sh
    column_span: 2  # Option 2

  processes:
    path: processes.sh
    full_row: true  # Option 3

layout:
  rows:  # Option 4
    - widgets: [cpu, memory, disk]
    - widgets: [docker, services]  # docker spans 2, services gets 1
    - widgets: [processes]          # full_row takes precedence
```

**Result:**
```
┌─────┬────────┬──────┐
│ CPU │ Memory │ Disk │
├─────┴────────┴──────┤
│    Docker    │ Srv  │  <- Docker spans 2 cols
├──────────────┴──────┤
│     Processes       │  <- Full row
└─────────────────────┘
```

**See:** `config-mixed.yaml`

---

## Priority System

Control which widgets appear on narrow terminals:

- `priority: 1` - **Critical** (always visible)
- `priority: 2` - **Normal** (hidden on very narrow terminals)
- `priority: 3` - **Low** (hidden on narrow terminals)

**Example:**
```yaml
widgets:
  cpu:
    priority: 1  # Always shown

  docker:
    priority: 2  # Hidden if < 100 cols

  ssl-certs:
    priority: 3  # Hidden if < 160 cols
```

---

## Best Practices

1. **Use auto-layout for simple dashboards**
   - Easy to configure
   - Responsive by default
   - Good for uniform widgets

2. **Use `full_row` for wide content**
   - Docker containers with long names
   - Log viewers
   - Process lists
   - SSL certificate info

3. **Use `column_span` for emphasis**
   - Highlight important widgets
   - Give more space to data-rich widgets
   - Create asymmetric layouts

4. **Use explicit rows for complex layouts**
   - Mixed widget sizes
   - Grouped widgets (metrics, infrastructure, logs)
   - Professional dashboards
   - When you need pixel-perfect control

5. **Combine all options**
   - Start with explicit rows
   - Add full_row for wide widgets
   - Fine-tune with column_span

---

## Example Configs

All example configs are in the repository:

- **config-simple.yaml** - Auto-layout with full_row
- **config-column-span.yaml** - Column spanning examples
- **config-explicit-rows.yaml** - Explicit row layout
- **config-mixed.yaml** - All options combined
- **config.yaml** - Production config with all widgets

Test them:
```bash
dotnet run -- --widgets-path ./widgets config-simple.yaml
dotnet run -- --widgets-path ./widgets config-explicit-rows.yaml
```

---

## Layout Precedence

When multiple layout options are set:

1. **Explicit rows** (`layout.rows`) takes highest precedence
2. **Column span** (`column_span`) overrides default width
3. **Full row** (`full_row`) makes widget span all columns
4. **Auto-layout** (`layout.order`) is the default fallback

---

## Responsive Behavior

All layout modes are responsive:

- **Narrow terminal** (< 100 cols): Forces 1 column, hides low-priority widgets
- **Medium terminal** (100-160 cols): 2 columns, shows priority 1-2 widgets
- **Wide terminal** (160-220 cols): 3 columns, shows all widgets
- **Ultra-wide** (220+ cols): 4 columns, optimal viewing

**Column span adapts automatically:**
- 2-span widget in 1-column mode = full width
- 3-span widget in 2-column mode = full width
- Widgets never exceed available columns
