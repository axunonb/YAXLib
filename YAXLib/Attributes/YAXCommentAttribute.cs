// Copyright (C) Sina Iravanian, Julian Verdurmen, axuno gGmbH and other contributors.
// Licensed under the MIT license.

using System;

namespace YAXLib.Attributes
{
    /// <summary>
    ///     Creates a comment node per each line of the comment string provided.
    ///     This attribute is applicable to classes, structures, fields, and properties.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field |
                    AttributeTargets.Property)]
    public class YAXCommentAttribute : YAXBaseAttribute, IYaxMemberLevelAttribute
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="YAXCommentAttribute" /> class.
        /// </summary>
        /// <param name="comment">The comment.</param>
        public YAXCommentAttribute(string comment)
        {
            Comment = comment;
        }


        /// <summary>
        ///     Gets or sets the comment.
        /// </summary>
        /// <value>The comment.</value>
        public string Comment { get; set; }

        
        /// <inheritdoc/>
        void IYaxMemberLevelAttribute.Setup(MemberWrapper memberWrapper)
        {
            if (string.IsNullOrEmpty(Comment)) return;

            var comments = Comment!.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < comments.Length; i++) comments[i] = $" {comments[i].Trim()} ";

            memberWrapper.Comment = comments;
        }
    }
}