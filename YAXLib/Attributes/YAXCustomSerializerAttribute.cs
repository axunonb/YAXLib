﻿// Copyright (C) Sina Iravanian, Julian Verdurmen, axuno gGmbH and other contributors.
// Licensed under the MIT license.

using System;
using YAXLib.Exceptions;

namespace YAXLib.Attributes
{
    /// <summary>
    ///     Specifies a custom serializer class for a field, property, class, or struct. YAXLib will instantiate an object
    ///     from the specified type in this attribute, and calls appropriate methods while serializing.
    ///     This attribute is applicable to fields, properties, classes, and structs.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class |
                    AttributeTargets.Struct)]
    public class YAXCustomSerializerAttribute : YAXBaseAttribute, IYaxMemberLevelAttribute
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="YAXCustomSerializerAttribute" /> class.
        /// </summary>
        /// <param name="customSerializerType">Type of the custom serializer.</param>
        public YAXCustomSerializerAttribute(Type customSerializerType)
        {
            CustomSerializerType = customSerializerType;
        }

        /// <summary>
        ///     Gets or sets the type of the custom serializer.
        /// </summary>
        /// <value>The type of the custom serializer.</value>
        public Type CustomSerializerType { get; }

        /// <inheritdoc/>
        void IYaxMemberLevelAttribute.Setup(MemberWrapper memberWrapper)
        {
            var isDesiredInterface =
                ReflectionUtils.IsDerivedFromGenericInterfaceType(CustomSerializerType, typeof(ICustomSerializer<>),
                    out var genTypeArg);

            if (!isDesiredInterface)
                throw new YAXObjectTypeMismatch(typeof(ICustomSerializer<>), CustomSerializerType);

            if (!genTypeArg.IsAssignableFrom(memberWrapper.MemberType))
                throw new YAXObjectTypeMismatch(memberWrapper.MemberType, genTypeArg);

            memberWrapper.CustomSerializerType = CustomSerializerType;
        }
    }
}