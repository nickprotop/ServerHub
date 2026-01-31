// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace ServerHub;

/// <summary>
/// Global application state flags
/// </summary>
public static class ApplicationState
{
    /// <summary>
    /// True when running in widget test mode (test-widget command)
    /// Disables security restrictions for widget execution
    /// </summary>
    public static bool IsTestMode { get; set; }
}
