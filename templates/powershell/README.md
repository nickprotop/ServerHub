# PowerShell Widget Template

Cross-platform PowerShell widget template for ServerHub.

## Prerequisites

Install PowerShell Core for cross-platform support:
- Linux: `sudo apt install powershell` or `sudo snap install powershell --classic`
- macOS: `brew install --cask powershell`
- Windows: Pre-installed or download from Microsoft

## Features

- Cross-platform PowerShell support
- Access to .NET libraries
- Rich cmdlet ecosystem
- Object-oriented pipeline

## Usage

```bash
serverhub new-widget powershell --name process-monitor
```

## Template Variables

- **WIDGET_NAME** (required): Widget identifier
- **WIDGET_TITLE** (optional): Display title
- **REFRESH_INTERVAL** (optional): Refresh interval in seconds
- **AUTHOR** (optional): Widget author name
- **DESCRIPTION** (optional): Widget description

## Example Customization

```powershell
# Example: Get top CPU processes
$processes = Get-Process | Sort-Object CPU -Descending | Select-Object -First 5

Write-Output "row: [bold]Top CPU Processes[/bold]"
Write-Output "row: table:Name|CPU (s)"

foreach ($proc in $processes) {
    Write-Output "row: table:$($proc.Name)|$([math]::Round($proc.CPU, 2))"
}
```

## Protocol Reference

See the [Widget Protocol Documentation](https://github.com/nickprotop/ServerHub#widget-protocol) for more details.
