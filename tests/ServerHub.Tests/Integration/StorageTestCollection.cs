// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using Xunit;

namespace ServerHub.Tests.Integration;

/// <summary>
/// Defines a test collection for storage tests to ensure they run serially (not in parallel).
/// This is necessary because StorageService is a singleton and tests share the same database connection.
/// </summary>
[CollectionDefinition("Storage Tests", DisableParallelization = true)]
public class StorageTestCollection
{
    // This class is never instantiated. It exists only to define the collection.
}
