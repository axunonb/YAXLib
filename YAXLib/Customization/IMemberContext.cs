﻿// Copyright (C) Sina Iravanian, Julian Verdurmen, axuno gGmbH and other contributors.
// Licensed under the MIT license.

namespace YAXLib.Customization;

/// <summary>
/// The member context interface provides information about the attributes of a member and member metadata.
/// </summary>
public interface IMemberContext
{
    /// <summary>
    /// The member's <see cref="IYaxMemberInfo" /> for member serialization, else <see langword="null" />.
    /// </summary>
    IYaxMemberInfo MemberInfo { get; }

    /// <summary>
    /// The member's <see cref="IYaxFieldInfo" /> for field serialization, else <see langword="null" />.
    /// </summary>
    IYaxFieldInfo? FieldInfo { get; }

    /// <summary>
    /// The member's <see cref="IYaxPropertyInfo" /> for property serialization, else <see langword="null" />.
    /// </summary>
    IYaxPropertyInfo? PropertyInfo { get; }

    /// <summary>
    /// The member's <see cref="Customization.TypeContext" /> for member serialization./>.
    /// </summary>
    TypeContext TypeContext { get; }

    /// <summary>
    /// Gets value of this member in the specified object.
    /// </summary>
    /// <param name="obj">The object from which the value must be retrieved.</param>
    /// <param name="index">
    /// Optional index values for indexed properties.
    /// The indexes of indexed properties are zero-based. This value should be <see langword="null" /> for non-indexed
    /// properties.
    /// </param>
    /// <returns>The value for this member.</returns>
    object? GetValue(object? obj, object[]? index = null);
}