# Storage System Guide

ServerHub includes a built-in time-series storage system that allows widgets to persist metrics and query historical data. This enables features like:

- Historical trend visualization
- Data aggregation (averages, max, min)
- Long-term monitoring and analytics
- Persistent state across restarts

## Architecture

### Database

- **Engine**: SQLite with Write-Ahead Logging (WAL) for concurrency
- **Location**: `~/.config/serverhub/serverhub.db` (configurable)
- **Schema**: Single `widget_data` table with optimized indexes
- **Isolation**: Data is automatically scoped per widget ID

### Data Model

```sql
CREATE TABLE widget_data (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    widget_id TEXT NOT NULL,              -- Widget identifier
    measurement TEXT NOT NULL,            -- Metric name (e.g., "cpu_usage")
    tags TEXT,                            -- JSON: {"core":"0","host":"srv01"}
    timestamp INTEGER NOT NULL,           -- Unix seconds
    field_name TEXT NOT NULL,             -- Field name (e.g., "value")
    field_value REAL,                     -- Numeric value
    field_text TEXT,                      -- String value
    field_json TEXT,                      -- JSON value
    UNIQUE(widget_id, measurement, tags, timestamp, field_name)
);
```

## Configuration

Add storage configuration to `~/.config/serverhub/config.yaml`:

```yaml
storage:
  enabled: true                           # Master toggle (default: true)
  database_path: ~/.config/serverhub/serverhub.db
  retention_days: 30                      # How long to keep data
  cleanup_interval_hours: 1               # How often to run cleanup
  max_database_size_mb: 500              # Warn if DB exceeds this
  auto_vacuum: true                       # Run VACUUM after cleanup
```

### Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `enabled` | `true` | Enable/disable storage system |
| `database_path` | `~/.config/serverhub/serverhub.db` | SQLite database file path |
| `retention_days` | `30` | Delete data older than this many days |
| `cleanup_interval_hours` | `1` | How often to run automatic cleanup |
| `max_database_size_mb` | `500` | Log warning if database exceeds this size |
| `auto_vacuum` | `true` | Run VACUUM to reclaim space after cleanup |

## Widget Protocol

### Storing Data

Use the `datastore:` directive with InfluxDB-style line protocol:

```bash
datastore: MEASUREMENT[,tag=val,tag=val] field=val[,field=val] [timestamp]
```

**Examples:**

```bash
# Simple metric
echo "datastore: cpu_usage value=75.5"

# With tags for grouping
echo "datastore: cpu_usage,core=0,host=srv01 value=75.5"

# Multiple fields
echo "datastore: disk_io,device=sda reads=1500,writes=2300"

# Boolean field (stored as 0.0 or 1.0)
echo "datastore: service_status,name=nginx enabled=true"

# Explicit timestamp (Unix seconds)
echo "datastore: metric value=100 1707348000"
```

### Retrieving Data

#### Inline Value (datafetch)

```bash
# Latest value
echo "row: CPU: [datafetch:cpu_usage.value]%"

# Aggregated values
echo "row: 30s avg: [datafetch:cpu_usage.value:avg:30s]%"
echo "row: 1h max: [datafetch:cpu_usage.value:max:1h]%"
echo "row: 1h min: [datafetch:cpu_usage.value:min:1h]%"
echo "row: Sum: [datafetch:disk_io.writes:sum:1h]"
echo "row: Count: [datafetch:cpu_usage.value:count:1h]"
```

#### Historical Visualizations

```bash
# Sparkline (inline trend)
echo "row: Trend: [history_sparkline:cpu_usage.value:30s:cool:20]"

# Vertical bar chart
echo "row: [history_graph:cpu_usage.value:1m:cool:Load:0-100:40]"

# Line graph
echo "row: [history_line:cpu_usage.value:1h:warm:CPU:0-100:60:8:braille]"
```

## CLI Commands

### Storage Stats

View database statistics and configuration:

```bash
serverhub storage stats
```

Shows:
- Database size and location
- Total record count
- Number of widgets with data
- Oldest and newest records
- Last cleanup and VACUUM times
- Current configuration
- Warnings if database exceeds size limits

### Manual Cleanup

Run cleanup manually instead of waiting for automatic interval:

```bash
# Interactive (prompts for confirmation)
serverhub storage cleanup

# Force (skip confirmation)
serverhub storage cleanup --force
```

Cleanup process:
1. Deletes records older than `retention_days`
2. Runs VACUUM if `auto_vacuum` is enabled
3. Shows before/after statistics
4. Reports space reclaimed

### Export Data

Export widget data to CSV or JSON:

```bash
# Export to CSV (stdout)
serverhub storage export --widget cpu --format csv

# Export to file
serverhub storage export --widget cpu --format csv --output cpu_data.csv

# Export JSON
serverhub storage export --widget cpu --format json --output cpu_data.json

# Filter by time range
serverhub storage export --widget cpu --since 24h --output recent.csv

# Filter by measurement
serverhub storage export --widget cpu --measurement cpu_usage --format csv
```

## Automatic Maintenance

### Cleanup Timer

ServerHub automatically runs cleanup based on `cleanup_interval_hours`:

1. Checks database size and logs warnings if exceeding `max_database_size_mb`
2. Deletes records older than `retention_days`
3. Runs VACUUM if `auto_vacuum` is enabled and records were deleted
4. Logs results

**To disable automatic cleanup:**
```yaml
storage:
  cleanup_interval_hours: 0  # Disables automatic cleanup
```

### VACUUM

VACUUM reclaims disk space after deletions. When `auto_vacuum: true`:
- Runs after cleanup if records were deleted
- Optimizes database file size
- Rebuilds indexes for better performance

**Impact:**
- Locks database briefly during VACUUM
- Can take seconds to minutes depending on database size
- Significantly reduces file size after large deletions

## Best Practices

### Widget Design

**Store metrics at appropriate intervals:**
```bash
# Fast-changing metrics: store every refresh
echo "datastore: cpu_usage value=$CPU_PERCENT"

# Slow-changing metrics: store less frequently
if (( $(date +%s) % 60 == 0 )); then
    echo "datastore: disk_total value=$DISK_GB"
fi
```

**Use tags for grouping:**
```bash
# Per-core CPU metrics
for core in 0 1 2 3; do
    usage=$(get_core_usage $core)
    echo "datastore: cpu_usage,core=$core value=$usage"
done

# Query specific core: [datafetch:cpu_usage.value:avg:1m] with tags={"core":"0"}
```

**Choose appropriate time ranges:**
- Dashboard: 30s-1m (recent trends)
- Expanded view: 1h-24h (medium-term history)
- Analytics: 7d-30d (long-term trends)

### Performance

**Database grows based on:**
- Number of widgets
- Refresh intervals
- Number of fields per metric
- Retention period

**Example calculation:**
- 10 widgets × 1 metric each × 1 field × every 5 seconds
- = 12 inserts/minute × 60 × 24 = 17,280 records/day
- × 30 days retention = ~518,400 total records
- ≈ 50-100 MB database size

**Optimization tips:**
- Reduce `retention_days` if storage grows too large
- Use longer refresh intervals for slow-changing metrics
- Store only essential metrics
- Enable `auto_vacuum` to reclaim space

### Monitoring

**Check database health regularly:**
```bash
serverhub storage stats
```

**Watch for warnings:**
- Database size exceeding `max_database_size_mb`
- No recent cleanup (check `Last Cleanup`)
- Very old records (check `Oldest Record`)

**Manual intervention if needed:**
```bash
# Force cleanup
serverhub storage cleanup --force

# Check results
serverhub storage stats
```

## Troubleshooting

### Storage Not Working

**Check if storage is enabled:**
```bash
# Look for storage section in config
cat ~/.config/serverhub/config.yaml | grep -A 5 "storage:"
```

**Verify database exists:**
```bash
ls -lh ~/.config/serverhub/serverhub.db
```

**Check logs for errors:**
```bash
export SHARPCONSOLEUI_DEBUG_LOG=/tmp/serverhub-debug.log
export SHARPCONSOLEUI_DEBUG_LEVEL=Debug
serverhub

# In another terminal:
tail -f /tmp/serverhub-debug.log | grep Storage
```

### Database Too Large

**Check current size:**
```bash
serverhub storage stats
```

**Solutions:**
1. Reduce retention period:
   ```yaml
   storage:
     retention_days: 7  # Instead of 30
   ```

2. Run manual cleanup:
   ```bash
   serverhub storage cleanup --force
   ```

3. Increase cleanup frequency:
   ```yaml
   storage:
     cleanup_interval_hours: 1  # Instead of 24
   ```

### Slow Queries

**Symptoms:**
- Dashboard feels sluggish
- High CPU usage

**Solutions:**
1. Reduce time range in queries:
   ```bash
   # Instead of: [history_graph:cpu:24h]
   # Use: [history_graph:cpu:1h]
   ```

2. Run VACUUM to optimize:
   ```bash
   serverhub storage cleanup --force
   ```

3. Check database size:
   ```bash
   serverhub storage stats
   ```

### Data Not Persisting

**Verify directive syntax:**
```bash
# Test widget output
serverhub test-widget widgets/mywidget.sh

# Look for "datastore:" lines
# Check for parse errors
```

**Check storage service initialization:**
```bash
# Look for initialization in logs
grep "Storage initialized" /tmp/serverhub-debug.log
```

**Verify widget ID:**
```bash
# Storage is scoped per widget ID
# Make sure widget is properly configured
cat ~/.config/serverhub/config.yaml | grep -A 3 "mywidget:"
```

## Direct Database Access

For advanced use cases, you can query the SQLite database directly:

```bash
# Open database
sqlite3 ~/.config/serverhub/serverhub.db

# List all measurements
SELECT DISTINCT measurement FROM widget_data;

# Get recent CPU data
SELECT timestamp, field_value
FROM widget_data
WHERE widget_id = 'cpu' AND measurement = 'cpu_usage'
ORDER BY timestamp DESC
LIMIT 10;

# Average over last hour
SELECT AVG(field_value) as avg_cpu
FROM widget_data
WHERE widget_id = 'cpu'
  AND measurement = 'cpu_usage'
  AND timestamp >= unixepoch('now', '-1 hour');

# Export to CSV
.mode csv
.headers on
.output cpu_export.csv
SELECT * FROM widget_data WHERE widget_id = 'cpu';
.quit
```

## Security Considerations

- Database file permissions: 600 (owner read/write only)
- No network exposure (SQLite is local only)
- Widget data isolation via widget_id
- No authentication needed (single-user system)
- File-based backups recommended for production use

## Backup and Restore

### Backup

```bash
# Stop ServerHub first
# Copy database file
cp ~/.config/serverhub/serverhub.db ~/backups/serverhub-$(date +%Y%m%d).db

# Or use SQLite backup command
sqlite3 ~/.config/serverhub/serverhub.db ".backup ~/backups/serverhub-backup.db"
```

### Restore

```bash
# Stop ServerHub
# Restore database file
cp ~/backups/serverhub-20260208.db ~/.config/serverhub/serverhub.db

# Restart ServerHub
serverhub
```

## Migration from File-Based Caching

If you have widgets using file-based caching (e.g., `~/.cache/serverhub/widget.txt`):

**Before (file-based):**
```bash
CACHE_FILE=~/.cache/serverhub/cpu-load.txt
echo "$CPU_VALUE" >> "$CACHE_FILE"
VALUES=$(tail -30 "$CACHE_FILE" | tr '\n' ',')
echo "row: [sparkline:$VALUES]"
```

**After (storage-based):**
```bash
echo "datastore: cpu_load value=$CPU_VALUE"
echo "row: [history_sparkline:cpu_load.value:30s:cool:30]"
```

**Benefits:**
- No manual cache file management
- No stale data detection
- Automatic cleanup via retention
- Query any time range
- Survives widget updates

## Performance Benchmarks

Typical performance on modern hardware:

- **Insert**: ~10,000 records/second
- **Query (latest)**: < 1ms
- **Query (aggregated 1h)**: < 10ms
- **Query (time series 24h)**: < 50ms
- **Cleanup (1M records)**: ~2-5 seconds
- **VACUUM (500MB DB)**: ~5-10 seconds

## Roadmap

Future enhancements under consideration:

- Cross-widget queries (compare metrics across widgets)
- Downsampling (reduce data points for long-term storage)
- Compression (reduce disk space usage)
- Replication (backup to remote storage)
- Web API (query data remotely)
- Alerts (trigger on threshold violations)
