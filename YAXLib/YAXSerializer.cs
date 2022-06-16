// Copyright (C) Sina Iravanian, Julian Verdurmen, axuno gGmbH and other contributors.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using YAXLib.Enums;
using YAXLib.Exceptions;
using YAXLib.Options;

namespace YAXLib
{
    /// <summary>
    ///     An XML serialization class which lets developers design the XML file structure and select the exception handling
    ///     policy.
    ///     This class also supports serializing most of the collection classes such as the Dictionary generic class.
    /// </summary>
    public partial class YAXSerializer
    {
        #region Fields

        /// <summary>
        ///     The list of all errors that have occurred.
        /// </summary>
        private readonly YAXParsingErrors _parsingErrors;

        /// <summary>
        ///     A manager that keeps a map of namespaces to their prefixes (if any) to be added ultimately to the xml result
        /// </summary>
        private readonly XmlNamespaceManager _xmlNamespaceManager;

        /// <summary>
        ///     a reference to the base xml element used during serialization.
        /// </summary>
        private XElement _baseElement;

        /// <summary>
        ///     Reference to a pre assigned de-serialization base object
        /// </summary>
        private object _desObject;

        /// <summary>
        ///     The main document's default namespace. This is stored so that if an attribute has the default namespace,
        ///     it should be serialized without namespace assigned to it. Storing it here does NOT mean that elements
        ///     and attributes without any namespace must adapt this namespace. It is just for comparison and control
        ///     purposes.
        /// </summary>
        /// <remarks>
        ///     Is set by method <see cref="YAXSerializer.FindDocumentDefaultNamespace"/>
        /// </remarks>
        private XNamespace _documentDefaultNamespace;

        /// <summary>
        ///     Specifies whether an exception is occurred during the de-serialization of the current member
        /// </summary>
        private bool _exceptionOccurredDuringMemberDeserialization;

        /// <summary>
        ///     <see langword="true" /> if this instance is busy serializing objects, <see langword="false" /> otherwise.
        /// </summary>
        private bool _isSerializing;

        /// <summary>
        ///     XML document object which will hold the resulting serialization
        /// </summary>
        private XDocument _mainDocument;

        /// <summary>
        ///     A collection of already serialized objects, kept for the sake of loop detection and preventing stack overflow
        ///     exception
        /// </summary>
        private Stack<object> _serializedStack;

        /// <summary>
        ///     The class or structure that is to be serialized/deserialized.
        /// </summary>
        private Type _type;

        /// <summary>
        ///     The type wrapper for the underlying type used in the serializer
        /// </summary>
        private UdtWrapper _udtWrapper;

        #endregion

        #region Constructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="YAXSerializer" /> class.
        /// </summary>
        /// <param name="type">The type of the object being serialized/deserialized.</param>
        public YAXSerializer(Type type)
            : this(type, new SerializerOptions())
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="YAXSerializer" /> class.
        /// </summary>
        /// <param name="type">The type of the object being serialized/deserialized.</param>
        /// <param name="serializationOptions">The serialization option flags.</param>
        [Obsolete("Will be removed in v4. Use YAXSerializer(Type) or YAXSerializer(Type, SerializerOptions) instead.")]
        public YAXSerializer(Type type, YAXSerializationOptions serializationOptions)
            : this(type, YAXExceptionHandlingPolicies.ThrowWarningsAndErrors, YAXExceptionTypes.Error,
                serializationOptions)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="YAXSerializer" /> class.
        /// </summary>
        /// <param name="type">The type of the object being serialized/deserialized.</param>
        /// <param name="exceptionPolicy">The exception handling policy.</param>
        [Obsolete("Will be removed in v4. Use YAXSerializer(Type) or YAXSerializer(Type, SerializerOptions) instead.")]
        public YAXSerializer(Type type, YAXExceptionHandlingPolicies exceptionPolicy)
            : this(type, exceptionPolicy, YAXExceptionTypes.Error, YAXSerializationOptions.SerializeNullObjects)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="YAXSerializer" /> class.
        /// </summary>
        /// <param name="type">The type of the object being serialized/deserialized.</param>
        /// <param name="exceptionPolicy">The exception handling policy.</param>
        /// <param name="defaultExType">The exceptions are treated as the value specified, unless otherwise specified.</param>
        [Obsolete("Will be removed in v4. Use YAXSerializer(Type) or YAXSerializer(Type, SerializerOptions) instead.")]
        public YAXSerializer(Type type, YAXExceptionHandlingPolicies exceptionPolicy, YAXExceptionTypes defaultExType)
            : this(type, exceptionPolicy, defaultExType, YAXSerializationOptions.SerializeNullObjects)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="YAXSerializer" /> class.
        /// </summary>
        /// <param name="t">The type of the object being serialized/deserialized.</param>
        /// <param name="exceptionPolicy">The exception handling policy.</param>
        /// <param name="defaultExType">The exceptions are treated as the value specified, unless otherwise specified.</param>
        /// <param name="option">The serialization option.</param>
        [Obsolete("Will be removed in v4. Use YAXSerializer(Type) or YAXSerializer(Type, SerializerOptions) instead.")]
        public YAXSerializer(Type t, YAXExceptionHandlingPolicies exceptionPolicy, YAXExceptionTypes defaultExType,
            YAXSerializationOptions option) : this(t,
            new SerializerOptions {
                ExceptionHandlingPolicies = exceptionPolicy, 
                ExceptionBehavior = defaultExType,
                SerializationOptions = option
            })
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="YAXSerializer" /> class.
        /// </summary>
        /// <param name="t">The type of the object being serialized/de-serialized.</param>
        /// <param name="options">The <see cref="SerializerOptions"/> settings to influence the process of serialization or de-serialization</param>
        public YAXSerializer(Type t, SerializerOptions options)
        {
            _type = t;
            Options = options;
            
            // this must be the last call
            _parsingErrors = new YAXParsingErrors();
            _xmlNamespaceManager = new XmlNamespaceManager();
            _udtWrapper = TypeWrappersPool.Pool.GetTypeWrapper(_type, this);
            if (_udtWrapper.HasNamespace)
                TypeNamespace = _udtWrapper.Namespace;
        }

        #endregion

        #region Properties

        /// <summary>
        ///     Gets or sets the number of recursions (number of total created <see cref="YAXSerializer"/> instances).
        /// </summary>
        internal int RecursionCount { get; set; }

        internal XmlNamespaceManager XmlNamespaceManager => _xmlNamespaceManager;

        internal XNamespace TypeNamespace { get; set; }

        internal bool HasTypeNamespace => TypeNamespace.IsEmpty();

        /// <summary>
        ///     Gets the <see cref="SerializationOption"/> settings
        ///     to influence the process of serialization or de-serialization of <see cref="YAXSerializer"/>s.
        /// </summary>
        public SerializerOptions Options { get; }

        /// <summary>
        ///     Gets the default type of the exception.
        /// </summary>
        /// <value>The default type of the exception.</value>
        [Obsolete("Will be removed in v4. Use SerializerOptions.ExceptionBehavior instead.")]
        public YAXExceptionTypes DefaultExceptionType => Options.ExceptionBehavior;

        /// <summary>
        ///     Gets the serialization option.
        /// </summary>
        /// <value>The serialization option.</value>
        [Obsolete("Will be removed in v4. Use SerializerOptions.SerializationOptions instead.")]
        public YAXSerializationOptions SerializationOption => Options.SerializationOptions;

        /// <summary>
        ///     Gets the exception handling policy.
        /// </summary>
        /// <value>The exception handling policy.</value>
        [Obsolete("Will be removed in v4. Use SerializerOptions.ExceptionHandlingPolicies instead.")]
        public YAXExceptionHandlingPolicies ExceptionHandlingPolicy => Options.ExceptionHandlingPolicies;

        /// <summary>
        ///     Gets the parsing errors.
        /// </summary>
        /// <value>The parsing errors.</value>
        public YAXParsingErrors ParsingErrors => _parsingErrors;

        /// <summary>
        ///     Gets or sets a value indicating whether this instance is created to deserialize a non collection member of another
        ///     object.
        /// </summary>
        /// <value>
        ///     <see langword="true" /> if this instance is created to deserialize a non collection member of another object; otherwise,
        ///     <see langword="false" />.
        /// </value>
        private bool IsCreatedToDeserializeANonCollectionMember { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether XML elements or attributes should be removed after being deserialized
        /// </summary>
        private bool RemoveDeserializedXmlNodes { get; set; }

        /// <summary>
        ///     The URI address which holds the xmlns:yaxlib definition.
        /// </summary>
        [Obsolete("Will be removed in v4. Use SerializerOptions.Namespace.Uri instead.")]
        public XNamespace YaxLibNamespaceUri
        {
            get => Options.Namespace.Uri;
            set => Options.Namespace.Uri = value;
        }

        /// <summary>
        ///     The prefix used for the xml namespace
        /// </summary>
        [Obsolete("Will be removed in v4. Use SerializerOptions.Namespace.Prefix instead.")]
        public string YaxLibNamespacePrefix
        {
            get => Options.Namespace.Prefix;
            set => Options.Namespace.Prefix = value; 
        }

        /// <summary>
        ///     The attribute name used to deserialize meta-data for multi-dimensional arrays.
        /// </summary>
        [Obsolete("Will be removed in v4. Use SerializerOptions.AttributeName.Dimensions instead.")]
        public string DimensionsAttributeName
        {
            get => Options.AttributeName.Dimensions;
            set => Options.AttributeName.Dimensions = value;
        }

        /// <summary>
        ///     The attribute name used to deserialize meta-data for real types of objects serialized through
        ///     a reference to their base class or interface.
        /// </summary>
        [Obsolete("Will be removed in v4. Use SerializerOptions.AttributeName.RealType instead.")]
        public string RealTypeAttributeName
        {
            get => Options.AttributeName.RealType;
            set => Options.AttributeName.RealType = value;
        }

        /// <summary>
        ///     Specifies the maximum serialization depth (default 300).
        ///     This roughly equals the maximum element depth of the resulting XML.
        ///     0 means unlimited.
        ///     1 means an empty XML tag with no content.
        /// </summary>
        [Obsolete("Will be removed in v4. Use SerializerOptions.MaxRecursion instead.")]
        public int MaxRecursion
        {
            get => Options.MaxRecursion;
            set => Options.MaxRecursion = value;
        }

        #endregion

        internal void SetNamespaceToOverrideEmptyNamespace(XNamespace otherNamespace)
        {
            // if namespace info is not already set during construction, 
            // then set it from the other YAXSerializer instance
            if (otherNamespace.IsEmpty() && !HasTypeNamespace) TypeNamespace = otherNamespace;
        }

        #region Public methods

        /// <summary>
        ///     Cleans up auxiliary memory used by YAXLib during different sessions of serialization.
        /// </summary>
        public static void CleanUpAuxiliaryMemory()
        {
            TypeWrappersPool.CleanUp();
        }

        #endregion

        #region Private methods

        private YAXSerializer NewInternalSerializer(Type type, XNamespace namespaceToOverride,
            XElement insertionLocation)
        {
            RecursionCount = Options.MaxRecursion == 0 ? 0 : RecursionCount + 1;
            var serializer = new YAXSerializer(type, Options) {RecursionCount = RecursionCount};
            
            serializer._serializedStack = _serializedStack;
            serializer._documentDefaultNamespace = _documentDefaultNamespace;
            if (namespaceToOverride != null)
                serializer.SetNamespaceToOverrideEmptyNamespace(namespaceToOverride);

            if (insertionLocation != null)
                serializer.SetBaseElement(insertionLocation);

            return serializer;
        }

        private void FinalizeNewSerializer(YAXSerializer serializer, bool importNamespaces,
            bool popFromSerializationStack = true)
        {
            if (serializer == null)
                return;

            if (RecursionCount > 0) RecursionCount--;

            if (popFromSerializationStack && _isSerializing && serializer._type != null &&
                !serializer._type.IsValueType)
                _serializedStack.Pop();

            if (importNamespaces)
                _xmlNamespaceManager.ImportNamespaces(serializer);
            _parsingErrors.AddRange(serializer.ParsingErrors);
        }

        /// <summary>
        ///     Gets the sequence of fields to be serialized or to be deserialized for the specified type.
        ///     This sequence is retrieved according to the field-types specified by the user.
        /// </summary>
        /// <param name="typeWrapper">
        ///     The type wrapper for the type whose serializable
        ///     fields is going to be retrieved.
        /// </param>
        /// <returns>the sequence of fields to be de/serialized for the specified type</returns>
        private IEnumerable<MemberWrapper> GetFieldsToBeSerialized(UdtWrapper typeWrapper)
        {
            foreach (var member in typeWrapper.UnderlyingType.GetMembers(BindingFlags.Instance |
                                                                         BindingFlags.NonPublic | BindingFlags.Public))
            {
                var name0 = member.Name[0];
                if ((char.IsLetter(name0) ||
                     name0 == '_'
                    ) && // TODO: this is wrong, .NET supports unicode variable names or those starting with @
                    (member.MemberType == MemberTypes.Property || member.MemberType == MemberTypes.Field))
                {
                    var prop = member as PropertyInfo;
                    if (prop != null)
                    {
                        // ignore indexers; if member is an indexer property, do not serialize it
                        if (prop.GetIndexParameters().Length > 0)
                            continue;

                        // don't serialize delegates as well
                        if (ReflectionUtils.IsTypeEqualOrInheritedFromType(prop.PropertyType, typeof(Delegate)))
                            continue;
                    }

                    if (typeWrapper.IsCollectionType || typeWrapper.IsDictionaryType) //&& typeWrapper.IsAttributedAsNotCollection)
                        if (ReflectionUtils.IsPartOfNetFx(member))
                            continue;

                    var memInfo = new MemberWrapper(member, this);
                    if (memInfo.IsAllowedToBeSerialized(typeWrapper.FieldsToSerialize,
                        _udtWrapper.DoNotSerializePropertiesWithNoSetter)) yield return memInfo;
                }
            }
        }

        /// <summary>
        ///     Gets the sequence of fields to be serialized or to be deserialized for the serializer's underlying type.
        ///     This sequence is retrieved according to the field-types specified by the user.
        /// </summary>
        /// <returns>The sequence of fields to be de/serialized for the serializer's underlying type.</returns>
        private IEnumerable<MemberWrapper> GetFieldsToBeSerialized()
        {
            return GetFieldsToBeSerialized(_udtWrapper).OrderBy(t => t.Order);
        }

        /// <summary>
        ///     Called when an exception occurs inside the library. It applies the exception handling policies.
        /// </summary>
        /// <param name="ex">The exception that has occurred.</param>
        /// <param name="exceptionType">Type of the exception.</param>
        private void OnExceptionOccurred(YAXException ex, YAXExceptionTypes exceptionType)
        {
            _exceptionOccurredDuringMemberDeserialization = true;
            if (exceptionType == YAXExceptionTypes.Ignore) return;

            _parsingErrors.AddException(ex, exceptionType);
            if (Options.ExceptionHandlingPolicies == YAXExceptionHandlingPolicies.ThrowWarningsAndErrors ||
                Options.ExceptionHandlingPolicies == YAXExceptionHandlingPolicies.ThrowErrorsOnly &&
                exceptionType == YAXExceptionTypes.Error)
                throw ex;
        }

        private void FindDocumentDefaultNamespace()
        {
            if (_udtWrapper.HasNamespace && string.IsNullOrEmpty(_udtWrapper.NamespacePrefix))
                // it has a default namespace defined (one without a prefix)
                _documentDefaultNamespace = _udtWrapper.Namespace; // set the default namespace
        }

        #endregion
    }
}