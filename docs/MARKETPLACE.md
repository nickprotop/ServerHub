# ServerHub Marketplace

The ServerHub Marketplace is a community-driven registry for sharing and discovering custom widgets. It provides a secure, centralized way to extend ServerHub with widgets created by the community.

## Overview

The marketplace allows you to:
- **Discover** widgets created by the community
- **Install** widgets with automatic checksum verification
- **Share** your own widgets with other ServerHub users
- **Browse** by category (monitoring, infrastructure, development, databases, networking, security, cloud, utilities)

All marketplace widgets are hosted on GitHub and verified with SHA256 checksums. The marketplace uses a tiered verification system to help you make informed decisions about which widgets to trust.

## Browse and Search

### Search for Widgets

Find widgets by keyword, searching across names, descriptions, and categories:

```bash
# Search by keyword
serverhub marketplace search monitoring

# Search for Docker-related widgets
serverhub marketplace search docker

# Search for specific functionality
serverhub marketplace search "ssl certificate"
```

### List All Widgets

View all available widgets or filter by category:

```bash
# List all widgets
serverhub marketplace list

# List by category
serverhub marketplace list --category monitoring
serverhub marketplace list --category infrastructure
serverhub marketplace list --category databases
```

**Available categories:**
- `monitoring` - System and application monitoring widgets
- `infrastructure` - Infrastructure management and status
- `development` - Development tools and workflows
- `databases` - Database monitoring and management
- `networking` - Network status and diagnostics
- `security` - Security monitoring and alerts
- `cloud` - Cloud provider integrations
- `utilities` - General purpose utilities

### View Widget Details

Get detailed information about a specific widget before installing:

```bash
serverhub marketplace info username/widget-name
```

This shows:
- Full description
- Author information
- Category
- Available versions
- Required dependencies
- Verification status
- Installation count
- Source code repository link

## Install Widgets

### Basic Installation

Install the latest version of a widget:

```bash
serverhub marketplace install username/widget-name
```

The installer will:
1. Download the widget script from GitHub releases
2. Verify the SHA256 checksum
3. Check for required dependencies
4. Install to `~/.config/serverhub/widgets/marketplace/`
5. Add the widget to your config with checksum

### Install Specific Version

Pin to a specific version for stability:

```bash
serverhub marketplace install username/widget-name@1.0.0
```

### View Installed Widgets

See which marketplace widgets you have installed:

```bash
serverhub marketplace list-installed
```

This shows:
- Widget name and version
- Installation date
- Verification status
- Update availability

### Update Widgets

Check for and install widget updates:

```bash
# Check for updates
serverhub marketplace list-installed

# Update to latest version
serverhub marketplace install username/widget-name
```

When you update a widget, the new checksum is automatically validated and added to your config.

### Uninstall Widgets

Remove a marketplace widget:

```bash
# Remove widget files and config entry
serverhub marketplace uninstall username/widget-name

# Or manually remove from ~/.config/serverhub/widgets/marketplace/
# and delete the entry from your config.yaml
```

## Security & Verification

The marketplace uses a **security-first approach** with multiple layers of protection.

### Security Features

**SHA256 Checksums**
- All widgets have mandatory checksums in the registry
- Checksums are verified during installation
- Modified or tampered widgets will fail verification
- Checksums are automatically added to your config

**GitHub-only Hosting**
- Widgets must be hosted on GitHub releases
- Source code is publicly reviewable
- Release assets provide stable URLs
- GitHub's infrastructure provides availability

**Dependency Checking**
- Widget manifests declare required system commands
- Installer verifies dependencies before installation
- Clear error messages if dependencies are missing
- No silent failures

**Code Transparency**
- All widget code is publicly available
- Source repository linked in widget details
- You can review code before installing
- Community can audit and report issues

### Verification Tiers

The marketplace uses a three-tier verification system to help you assess trust:

#### ✓ Verified (Green Badge)

**Highest trust level** - Code reviewed by ServerHub maintainers.

What this means:
- Maintainers have manually reviewed the widget code
- Code follows security best practices
- Widget functionality is verified
- No obvious security issues or malicious code
- Author is known to the community

**This is NOT a guarantee of perfection** - bugs can exist in verified widgets. Verification means the code has been reviewed, not that it's bug-free.

#### ⚡ Community (Yellow Badge)

**Moderate trust level** - Multiple installs, no reported issues.

What this means:
- Widget has been installed by multiple users
- No security issues or problems reported
- Community usage provides some confidence
- Not officially reviewed by maintainers

Requirements for community tier:
- 10+ successful installs
- Active for 30+ days
- No reported security issues
- No reported functionality problems

#### ⚠ Unverified (Red Badge)

**Low trust level** - New or untested widget.

What this means:
- Widget is new to the marketplace
- Has not been reviewed by maintainers
- Limited or no community usage
- Unknown author or reputation

Installing unverified widgets requires explicit confirmation:
```
WARNING: This widget is unverified.
- Not reviewed by ServerHub maintainers
- Limited community usage
- Unknown security status

You are responsible for reviewing the code before installing.

Source code: https://github.com/username/widget-name

Continue? [y/N]:
```

### Your Responsibility

**You are responsible for reviewing code before installing unverified widgets.**

The verification tiers help guide your decision, but **you should always:**

1. **Review the source code** - Click the repository link and read the script
2. **Check the author** - Is this a known, reputable developer?
3. **Verify dependencies** - Do the required commands make sense?
4. **Start with verified widgets** - Prefer verified or community-tier widgets when possible
5. **Report issues** - If you find problems, report them to help the community

The marketplace reduces friction for sharing widgets, but it doesn't eliminate the need for caution.

## Contributing Widgets

Want to share your widget with the community? The process is straightforward:

### 1. Create Your Widget

Build a widget following the [Widget Protocol](WIDGET_PROTOCOL.md):

```bash
#!/bin/bash
echo "title: My Widget"
echo "row: [status:ok] Widget is working"
```

Test it locally with ServerHub to ensure it works correctly.

### 2. Host on GitHub

Create a GitHub repository for your widget:

```bash
# Create repository
gh repo create my-awesome-widget --public

# Add your widget script
git add my-widget.sh
git commit -m "Initial release"
git push
```

### 3. Create a Release

Create a GitHub release with your widget as an asset:

```bash
# Tag and create release
git tag v1.0.0
git push --tags

# Create release with widget script as asset
gh release create v1.0.0 my-widget.sh \
  --title "v1.0.0 - Initial Release" \
  --notes "First stable release"
```

**Important:** The widget script must be a release asset (not just in the repository).

### 4. Calculate Checksum

Calculate the SHA256 checksum of your widget script:

```bash
sha256sum my-widget.sh
```

You'll need this for the manifest.

### 5. Submit Manifest

Fork the [serverhub-registry](https://github.com/nickprotop/serverhub-registry) repository and add your widget manifest:

Create `manifests/username/widget-name.json`:

```json
{
  "id": "username/widget-name",
  "name": "My Awesome Widget",
  "description": "Brief description of what the widget does",
  "author": "Your Name",
  "category": "monitoring",
  "tags": ["monitoring", "custom", "api"],
  "repository": "https://github.com/username/widget-name",
  "versions": [
    {
      "version": "1.0.0",
      "download_url": "https://github.com/username/widget-name/releases/download/v1.0.0/my-widget.sh",
      "sha256": "a1b2c3d4e5f6...",
      "release_date": "2026-01-27",
      "dependencies": ["curl", "jq"]
    }
  ]
}
```

Submit a pull request with your manifest.

### 6. Review Process

Maintainers will:
- Verify the manifest format
- Check the checksum matches
- Review the widget code (for verified tier)
- Provide feedback if changes are needed
- Merge when approved

See the [Contributing Guide](https://github.com/nickprotop/serverhub-registry/blob/main/docs/CONTRIBUTING.md) for detailed submission guidelines.

## Registry Structure

The marketplace registry is maintained at [serverhub-registry](https://github.com/nickprotop/serverhub-registry).

### Registry Components

**registry.json**
- Central index of all widgets
- Generated from individual manifests
- Used by the CLI for search and list operations

**manifests/**
- Individual widget manifests
- Organized by author: `manifests/username/widget-name.json`
- Each manifest contains all versions and metadata

**GitHub Pages Site**
- Browse widgets at [nickprotop.github.io/serverhub-registry](https://nickprotop.github.io/serverhub-registry/)
- Visual interface for discovering widgets
- Verification badges and filtering

### Manifest Format

Widget manifests follow a structured format:

```json
{
  "id": "username/widget-name",          // Unique identifier
  "name": "Display Name",                // Human-readable name
  "description": "Brief description",    // One-sentence summary
  "author": "Author Name",               // Widget author
  "category": "monitoring",              // Primary category
  "tags": ["tag1", "tag2"],             // Searchable tags
  "repository": "https://github.com/...", // Source code
  "verification_tier": "unverified",     // unverified, community, verified
  "versions": [                          // Version history
    {
      "version": "1.0.0",                // Semantic version
      "download_url": "https://...",     // Direct download URL
      "sha256": "abc123...",             // Checksum
      "release_date": "2026-01-27",      // ISO date
      "dependencies": ["curl", "jq"],    // Required commands
      "min_serverhub_version": "0.0.9"   // Minimum ServerHub version
    }
  ]
}
```

See [Manifest Specification](https://github.com/nickprotop/serverhub-registry/blob/main/docs/MANIFEST_SPEC.md) for complete details.

## Best Practices

### For Users

**Installing Widgets**
- Start with verified widgets when possible
- Review unverified widget code before installing
- Check dependencies before installing
- Use specific versions for production systems
- Test in development before production deployment

**Security**
- Run `serverhub --verify-checksums` periodically to check for tampering
- Review widget updates before installing
- Report suspicious widgets to maintainers
- Don't install widgets with unreasonable dependencies
- Never run ServerHub as root

**Configuration**
- Keep marketplace widgets separate from custom widgets
- Document which widgets you've installed
- Version control your config.yaml
- Test widget updates in non-production first

### For Contributors

**Widget Development**
- Follow the [Widget Protocol](WIDGET_PROTOCOL.md)
- Keep widgets focused on a single purpose
- Minimize dependencies
- Handle errors gracefully
- Provide clear output messages

**Security**
- Never include credentials in widget code
- Validate all inputs
- Use secure APIs and protocols
- Document security considerations
- Keep dependencies minimal and well-known

**Maintenance**
- Use semantic versioning
- Provide clear release notes
- Respond to issues promptly
- Keep the manifest updated
- Test on multiple systems

## Troubleshooting

### Installation Issues

**"Widget not found in registry"**
- Check the widget ID format: `username/widget-name`
- Verify the widget exists: `serverhub marketplace list`
- Check your internet connection

**"Checksum verification failed"**
- Widget file was tampered with or corrupted
- GitHub release asset was modified after manifest submission
- Contact the widget author or report the issue

**"Missing dependency: command-name"**
- Install the required command: `apt install package-name`
- Check widget details for dependency list
- Contact widget author if dependency is unclear

**"Version not found"**
- Requested version doesn't exist
- Check available versions: `serverhub marketplace info username/widget-name`
- Try installing without version specifier for latest

### Configuration Issues

**Widget installed but not showing in dashboard**
- Add widget to your config.yaml layout order
- Check widget ID matches config entry
- Verify widget path and checksum
- Restart ServerHub

**Widget shows "Checksum mismatch"**
- Widget file was modified after installation
- Reinstall the widget: `serverhub marketplace install username/widget-name`
- If problem persists, report to maintainers

### Getting Help

**Report Issues**
- ServerHub issues: [nickprotop/ServerHub/issues](https://github.com/nickprotop/ServerHub/issues)
- Registry issues: [nickprotop/serverhub-registry/issues](https://github.com/nickprotop/serverhub-registry/issues)

**Discussions**
- General questions: [ServerHub Discussions](https://github.com/nickprotop/ServerHub/discussions)
- Widget development help: Check existing discussions or start a new one

**Security Issues**
- Report security vulnerabilities privately to maintainers
- See [SECURITY.md](https://github.com/nickprotop/serverhub-registry/blob/main/docs/SECURITY.md)

## Future Enhancements

Potential future improvements to the marketplace:

**Enhanced Verification**
- Automated code scanning
- Reputation scores based on usage
- Community ratings and reviews
- Verified author badges

**Improved Discovery**
- Screenshots and demos
- Popularity rankings
- Related widgets suggestions
- Trending widgets

**Better Management**
- Bulk widget updates
- Widget dependencies and conflicts
- Automatic update notifications
- Rollback to previous versions

**Integration**
- Direct installation from web interface
- One-click widget installation URLs
- IDE/editor integration for development
- CI/CD integration for testing

---

For more information:
- [Widget Protocol Documentation](WIDGET_PROTOCOL.md)
- [Example Widgets and Use Cases](EXAMPLES.md)
- [Registry Contributing Guide](https://github.com/nickprotop/serverhub-registry/blob/main/docs/CONTRIBUTING.md)
- [Registry Manifest Specification](https://github.com/nickprotop/serverhub-registry/blob/main/docs/MANIFEST_SPEC.md)
