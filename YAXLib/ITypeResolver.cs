﻿// Copyright (C) Sina Iravanian, Julian Verdurmen, axuno gGmbH and other contributors.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using YAXLib.Options;

namespace YAXLib;
public interface ITypeResolver
{
    IList<IYaxMemberInfo> ResolveMembers(IList<IYaxMemberInfo> sourceMembers, Type underlyingType, SerializerOptions options);

    string GetTypeName(string proposedName, Type udtType, SerializerOptions serializerOptions);
}