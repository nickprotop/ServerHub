# C# Script Widget Template

C# widget template using dotnet-script for scripting without compilation.

## Prerequisites

Install dotnet-script:
```bash
dotnet tool install -g dotnet-script
```

## Features

- No compilation required
- Full C# language support
- Access to .NET libraries
- NuGet package support

## Usage

```bash
serverhub new-widget csharp-script --name service-monitor
```

## Template Variables

- **WIDGET_NAME** (required): Widget identifier
- **WIDGET_TITLE** (optional): Display title
- **REFRESH_INTERVAL** (optional): Refresh interval in seconds
- **AUTHOR** (optional): Widget author name
- **DESCRIPTION** (optional): Widget description

## Using NuGet Packages

Add package references at the top of your script:

```csharp
#r "nuget: Newtonsoft.Json, 13.0.1"

using Newtonsoft.Json;
```

## Protocol Reference

See the [Widget Protocol Documentation](https://github.com/nickprotop/ServerHub#widget-protocol) for more details.
