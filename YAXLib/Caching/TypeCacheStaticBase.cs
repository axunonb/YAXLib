﻿#nullable enable

namespace YAXLib.Caching;

/// <summary>
/// A static field in a generic type is not shared among instances of different constructed types.
/// </summary>
internal abstract class TypeCacheStaticBase
{
    private protected static readonly object Locker = new();
}
