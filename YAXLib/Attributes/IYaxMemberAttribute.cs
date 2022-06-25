// Copyright (C) Sina Iravanian, Julian Verdurmen, axuno gGmbH and other contributors.
// Licensed under the MIT license.

namespace YAXLib
{
    /// <summary>
    /// Attributes for members, that are process by <see cref="MemberWrapper"/>
    /// must implement this interface.
    /// </summary>
    internal interface IYaxMemberAttribute
    {
        /// <summary>
        /// The method is invoked by <see cref="MemberWrapper"/>.
        /// </summary>
        /// <param name="memberWrapper"></param>
        void Process (MemberWrapper memberWrapper);
    }
}
