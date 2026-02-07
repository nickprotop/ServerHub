# ServerHub Roadmap

This document outlines planned features and enhancements for ServerHub. The roadmap is organized by priority and category to guide development and community contributions.

**Vision**: Transform ServerHub from a monitoring dashboard into a comprehensive server management platform that monitors, alerts, acts, and learns - all while maintaining its core philosophy of simplicity and extensibility.

---

## 🎯 Top Priorities (Next Release)

Track progress for high-priority features actively in development or planned for the next release.

### Quick Wins
- [x] **Command Palette (Ctrl+P)** - Quick action finder with fuzzy search
  - Status: ✅ Completed
  - Impact: High UX improvement
  - Effort: Medium
  - Files: `src/UI/CommandPaletteDialog.cs`, `src/Services/CommandPaletteService.cs`, `src/Models/PaletteCommand.cs`

- [ ] **Dashboard Presets/Profiles** - Save and switch between multiple configs
  - Status: Not Started
  - Impact: High (especially for multi-server setups)
  - Effort: Low (config structure already supports this)
  - Implementation: `serverhub --profile production`, `~/.config/serverhub/profiles/`

- [ ] **Search & Filter** - Search widget content and filter by status
  - Status: Not Started
  - Impact: Medium (improves navigation for dashboards with many widgets)
  - Effort: Medium
  - Features: Ctrl+F search, show only errors/warnings, highlight matches

### High Impact Features
- [ ] **Alert System with Notifications** - Proactive monitoring with alerts
  - Status: Not Started
  - Impact: Very High (transforms passive dashboard to active monitor)
  - Effort: High
  - Components:
    - [ ] Status change detection (ok → warning → error)
    - [ ] Desktop notifications (notify-send)
    - [ ] Alert history log
    - [ ] Configurable alert rules in config.yaml
    - [ ] Optional webhook support (Slack, Discord, email)

- [ ] **Historical Data & Trends** - Store and visualize metric history
  - Status: Not Started
  - Impact: Very High (provides context for decision-making)
  - Effort: High
  - Components:
    - [ ] SQLite/JSON storage for time-series data
    - [ ] Sparklines in widget summaries
    - [ ] Trend indicators (↑↓ for metrics)
    - [ ] Expanded view with time-series graphs
    - [ ] Configurable retention period

- [ ] **Multi-Server Support** - Monitor multiple servers from one dashboard
  - Status: Not Started
  - Impact: Very High (massive use case expansion)
  - Effort: Very High
  - Components:
    - [ ] Server definitions in config with SSH credentials
    - [ ] Remote widget execution via SSH
    - [ ] Unified multi-server dashboard view
    - [ ] Per-server filtering and selection
    - [ ] Server groups (production, staging, dev)

---

## 🔔 Alerting & Monitoring

Enhance ServerHub's monitoring capabilities with intelligent alerting and trend analysis.

### Alert System
- [ ] Status change detection and notifications
- [ ] Desktop notifications (Linux notify-send)
- [ ] Webhook support (Slack, Discord, custom endpoints)
- [ ] Email notifications (SMTP)
- [ ] Alert history with timestamps
- [ ] Configurable alert rules per widget
- [ ] Alert escalation (warning → error thresholds)
- [ ] Alert acknowledgment system
- [ ] Mute/snooze alerts temporarily

### Historical Data & Analytics
- [ ] Time-series data storage (SQLite or JSON)
- [ ] Sparklines in widget dashboard view
- [ ] Full time-series graphs in expanded view
- [ ] Trend indicators (↑↓ symbols for direction)
- [ ] Configurable data retention
- [ ] Data export (CSV, JSON)
- [ ] Predictive alerts ("disk full in 3 days")
- [ ] Anomaly detection (unusual patterns)

### Smart Status
- [ ] Dashboard-level aggregate status indicator
- [ ] Category-based health grouping
- [ ] "Problems" view (show only issues)
- [ ] Status summary in title bar
- [ ] Widget status history

---

## 🖥️ Multi-Server & Remote Monitoring

Scale ServerHub to monitor multiple servers and remote systems.

### Multi-Server Core
- [ ] Server definitions in config.yaml
- [ ] SSH-based remote widget execution
- [ ] Unified dashboard (all servers)
- [ ] Per-server view switching (hotkey)
- [ ] Server connection status indicators
- [ ] Parallel execution across servers

### Server Groups
- [ ] Define server groups (prod, staging, dev)
- [ ] Filter dashboard by group
- [ ] Group-level actions (restart all in group)
- [ ] Group health status
- [ ] Quick group switching

### Advanced Remote Features
- [ ] SSH key management
- [ ] Connection pooling
- [ ] Automatic reconnection
- [ ] Bastion/jump host support
- [ ] Agent mode (lightweight daemon on remote servers)

---

## 📊 Enhanced Visualization

Improve data presentation with advanced charts and customizable layouts.

### Advanced Charts
- [ ] Line graphs for time-series (expanded view)
- [ ] Bar charts for comparisons
- [ ] Gauge widgets (percentage metrics)
- [ ] Histogram support
- [ ] Color gradients for continuous metrics
- [ ] Custom chart configurations

### Layout Enhancements
- [ ] Custom widget sizes (span multiple columns)
- [ ] Half-height widgets for compact info
- [ ] Manual grid layout (not just auto-flow)
- [ ] Widget pinning/freezing
- [ ] Collapsible widget groups
- [ ] Hide/show widgets dynamically

### Themes & Customization
- [ ] Dark/light/custom themes
- [ ] Per-widget color customization
- [ ] Custom status colors (ok/warning/error)
- [ ] Font size configuration
- [ ] Border style options
- [ ] Color scheme presets

---

## 🤖 Automation & Intelligence

Add smart automation and conditional logic to reduce manual intervention.

### Conditional Actions
- [ ] Trigger actions based on widget status
- [ ] If-then rules: "if disk > 90%, clean temp"
- [ ] Action chains (sequential execution)
- [ ] Scheduled actions (cron-like syntax)
- [ ] Action templates library
- [ ] Dry-run mode for action testing

### Widget Dependencies
- [ ] Declare widget dependencies: `depends_on: [network]`
- [ ] Smart refresh (only refresh when dependencies change)
- [ ] Execution ordering based on dependencies
- [ ] Cascade actions (action on A triggers check on B)
- [ ] Dependency graph visualization

### Predictive Intelligence
- [ ] Trend analysis and predictions
- [ ] Resource exhaustion warnings
- [ ] Pattern recognition (unusual behavior)
- [ ] Machine learning for anomaly detection
- [ ] Recommended actions based on patterns

---

## 🔍 Navigation & Usability

Improve user experience with better navigation and discoverability.

### Command Palette
- [x] Ctrl+P quick action finder
- [x] Fuzzy search for all actions (searches label, description, and command text)
- [ ] Recent actions history
- [x] Keyboard shortcut hints
- [x] Widget jump by name
- [ ] Action favorites/bookmarks

### Search & Filter
- [ ] Global search (Ctrl+F) across all widgets
- [ ] Filter by status (errors, warnings, ok)
- [ ] Hide specific widgets temporarily
- [ ] Search highlighting
- [ ] Regex search support
- [ ] Save search filters

### Dashboard Presets
- [ ] Save multiple config profiles
- [ ] Quick profile switching (F4 or --profile flag)
- [ ] Profile templates (monitoring-only, full-control)
- [ ] Per-server profiles
- [ ] Profile import/export
- [ ] Profile inheritance (extend base profile)

### Session Management
- [ ] Save/restore dashboard state
- [ ] Session history
- [ ] Undo/redo for config changes
- [ ] Auto-save configuration drafts
- [ ] Recently viewed widgets

---

## 🛠️ Developer Experience

Make widget development faster and more enjoyable.

### Live Development
- [ ] `--watch` mode: auto-reload on file changes
- [ ] File watcher for widget directory
- [ ] Hot reload without restart
- [ ] Development mode indicators
- [ ] Live error display

### Debugging Tools
- [ ] Show raw widget output (pre-parsing)
- [ ] Execution time per widget
- [ ] Performance profiler
- [ ] Memory usage tracking
- [ ] Widget execution logs
- [ ] Mock mode (test with sample data)

### Testing Framework
- [ ] Unit test support: `serverhub test-widget --unit`
- [ ] Integration tests with mock data
- [ ] Regression test suite
- [ ] CI/CD integration helpers
- [ ] Test coverage reporting
- [ ] Automated widget testing in marketplace

### Documentation Generator
- [ ] Auto-generate widget docs from protocol output
- [ ] Widget README template
- [ ] Protocol validation with detailed errors
- [ ] Example generator from widget output
- [ ] Interactive widget documentation browser

---

## 🔐 Enterprise & Teams

Features for team collaboration and organizational use.

### Private Repositories
- [ ] Support custom marketplace URLs
- [ ] Organization-specific widget libraries
- [ ] Authentication for private repos
- [ ] Internal widget approval workflow
- [ ] Widget versioning and rollback

### Access Control
- [ ] User permission system (view, execute, admin)
- [ ] Widget-level permissions
- [ ] Action permission restrictions
- [ ] Audit log for all actions
- [ ] Session management and timeouts
- [ ] API token authentication

### Collaboration
- [ ] Share dashboard configs easily
- [ ] Team widget libraries
- [ ] Widget ratings and reviews
- [ ] Usage statistics in marketplace
- [ ] Comments and annotations on widgets
- [ ] Shared alert configurations

---

## 📦 Data Integration

Connect ServerHub with external tools and platforms.

### Export & Observability
- [ ] Prometheus exporter
- [ ] InfluxDB integration
- [ ] JSON API mode: `--api-mode`
- [ ] Webhook events on status changes
- [ ] Grafana dashboard integration
- [ ] OpenTelemetry support

### Import & Migration
- [ ] Import from other monitoring tools
- [ ] Config converters (Grafana → ServerHub)
- [ ] Migration from similar dashboards
- [ ] Bulk widget import
- [ ] Config validation and linting

### Third-Party Integrations
- [ ] Kubernetes monitoring widgets
- [ ] Cloud provider integrations (AWS, Azure, GCP)
- [ ] Container orchestration (Docker Swarm, K8s)
- [ ] CI/CD pipeline monitoring
- [ ] Log aggregation integration (ELK, Loki)

---

## 🌟 Future Ideas

Longer-term concepts and experimental features for consideration.

### Advanced Features
- [ ] Web UI (browser-based dashboard)
- [ ] Mobile app (iOS/Android)
- [ ] Distributed monitoring (multiple ServerHub instances)
- [ ] Plugin system (extend beyond widgets)
- [ ] Scripting API (Lua/JavaScript for custom logic)
- [ ] Natural language queries ("show me high CPU processes")

### AI & Machine Learning
- [ ] Intelligent alert reduction (suppress noise)
- [ ] Automatic widget discovery (scan system, suggest widgets)
- [ ] Chatbot integration (ask questions about server state)
- [ ] Performance optimization suggestions
- [ ] Capacity planning recommendations

### Community Features
- [ ] Widget marketplace ratings and reviews
- [ ] Widget screenshots and demos
- [ ] Community discussions
- [ ] Widget of the month
- [ ] Contribution leaderboard

---

## 🤝 Contributing

We welcome contributions! Here's how you can help:

### High Priority
Items marked in [Top Priorities](#-top-priorities-next-release) are actively being worked on. Check GitHub issues for discussion and coordination.

### Pick an Item
Choose any unchecked item from this roadmap. Open an issue to discuss implementation before starting major work.

### Propose New Features
Don't see something you want? Open an issue with:
- Use case description
- Proposed implementation approach
- Potential impact on existing features

### Marketplace Widgets
The fastest way to extend ServerHub is by creating marketplace widgets. See [WIDGET_DEVELOPMENT.md](docs/WIDGET_DEVELOPMENT.md).

---

## 📝 Roadmap Updates

This roadmap is a living document. Status updates:
- **Last Updated**: 2025-02-07
- **Next Review**: On major release
- **Feedback**: Open issues or discussions on GitHub

**Legend**:
- [ ] Not started
- [~] In progress
- [x] Completed

---

**Want to influence priorities?** ⭐ Star features in GitHub issues or start discussions about what matters most to you!
