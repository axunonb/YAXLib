// Copyright (C) Sina Iravanian, Julian Verdurmen, axuno gGmbH and other contributors.
// Licensed under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using YAXLib.Attributes;
using YAXLib.Enums;
using YAXLib.Exceptions;

namespace YAXLib
{
    public partial class YAXSerializer
    {
        #region Public methods

        /// <summary>
        ///     Serializes the specified object and returns a string containing the XML.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <returns>A <code>System.String</code> containing the XML</returns>
        public string Serialize(object obj)
        {
            return SerializeXDocument(obj).ToString();
        }

        /// <summary>
        ///     Serializes the specified object and returns an instance of <c>XDocument</c> containing the result.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <returns>An instance of <c>XDocument</c> containing the resulting XML</returns>
        public XDocument SerializeToXDocument(object obj)
        {
            return SerializeXDocument(obj);
        }

        /// <summary>
        ///     Serializes the specified object into a <c>TextWriter</c> instance.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <param name="textWriter">The <c>TextWriter</c> instance.</param>
        public void Serialize(object obj, TextWriter textWriter)
        {
            textWriter.Write(Serialize(obj));
        }

        /// <summary>
        ///     Serializes the specified object into a <c>XmlWriter</c> instance.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <param name="xmlWriter">The <c>XmlWriter</c> instance.</param>
        public void Serialize(object obj, XmlWriter xmlWriter)
        {
            SerializeXDocument(obj).WriteTo(xmlWriter);
        }

        /// <summary>
        ///     Serializes the specified object to file.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <param name="fileName">Path to the file.</param>
        public void SerializeToFile(object obj, string fileName)
        {
            var ser = string.Format(
                Options.Culture,
                "{0}{1}{2}",
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                Environment.NewLine,
                Serialize(obj));
            File.WriteAllText(fileName, ser, Encoding.UTF8);
        }

        #endregion

        #region Private methods
        
        /// <summary>
        ///     Serializes the object into an <c>XDocument</c> object.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <returns></returns>
        private XDocument SerializeXDocument(object obj)
        {
            // This method must be called by any public Serialize method
            _isSerializing = true;
            if (_serializedStack == null)
                _serializedStack = new Stack<object>();
            _mainDocument = new XDocument();
            _mainDocument.Add(SerializeBase(obj));
            return _mainDocument;
        }

        /// <summary>
        ///     One of the base methods that perform the whole job of serialization.
        /// </summary>
        /// <param name="obj">The object to be serialized</param>
        /// <returns>
        ///     an instance of <c>XElement</c> which contains the result of
        ///     serialization of the specified object
        /// </returns>
        private XElement SerializeBase(object obj)
        {
            if (obj == null)
                return new XElement(_udtWrapper.Alias);

            if (!_type.IsInstanceOfType(obj))
                throw new YAXObjectTypeMismatch(_type, obj.GetType());

            FindDocumentDefaultNamespace();

            if (TryTreatAsDictionary(obj, out var xElement)) return xElement;

            if (TryTreatAsCollection(obj, out xElement)) return xElement;

            if (TryUnderlyingTypeAsBasicType(obj, out xElement)) return xElement;

            if (TryUnderlyingTypeIsNotEqualOrNullableOfObjectType(obj, out xElement)) return xElement;

            // SerializeBase will add the object to the stack
            var elem = SerializeBase(obj, _udtWrapper.Alias);
            if (!_type.IsValueType)
                _serializedStack.Pop();
            Debug.Assert(_serializedStack.Count == 0,
                "Serialization stack is not empty at the end of serialization");
            
            return elem;
        }

#nullable enable
        private bool TryUnderlyingTypeAsBasicType(object obj, out XElement? xElement)
        {
            xElement = null;

            if (!ReflectionUtils.IsBasicType(_udtWrapper.UnderlyingType)) return false;

            var elemResult = MakeBaseElement(null, _udtWrapper.Alias, obj, out _);
            if (_udtWrapper.PreservesWhitespace)
                XMLUtils.AddPreserveSpaceAttribute(elemResult, Options.Culture);
            if (elemResult.Parent == null)
                _xmlNamespaceManager.AddNamespacesToElement(elemResult, _documentDefaultNamespace, Options,
                    _udtWrapper);
                
            xElement = elemResult;
            return true;
        }

        private bool TryTreatAsCollection(object obj, out XElement? xElement)
        {
            xElement = null;

            if (!_udtWrapper.IsTreatedAsCollection) return false;

            var elemResult = MakeCollectionElement(null, _udtWrapper.Alias, obj, null, null);
            if (_udtWrapper.PreservesWhitespace)
                XMLUtils.AddPreserveSpaceAttribute(elemResult, Options.Culture);
            if (elemResult.Parent == null)
                _xmlNamespaceManager.AddNamespacesToElement(elemResult, _documentDefaultNamespace, Options, _udtWrapper);
            
            xElement = elemResult;
            return true;
        }

        private bool TryTreatAsDictionary(object obj, out XElement? xElement)
        {
            xElement = null;

            // to serialize stand-alone collection or dictionary objects
            if (!_udtWrapper.IsTreatedAsDictionary) return false;

            var elemResult = MakeDictionaryElement(null, _udtWrapper.Alias, obj,
                _udtWrapper.DictionaryAttributeInstance, _udtWrapper.CollectionAttributeInstance,
                _udtWrapper.IsNotAllowedNullObjectSerialization);
            if (_udtWrapper.PreservesWhitespace)
                XMLUtils.AddPreserveSpaceAttribute(elemResult, Options.Culture);
            if (elemResult.Parent == null)
                _xmlNamespaceManager.AddNamespacesToElement(elemResult, _documentDefaultNamespace, Options, _udtWrapper);
            {
                xElement = elemResult;
                return true;
            }
        }

        private bool TryUnderlyingTypeIsNotEqualOrNullableOfObjectType(object obj, out XElement? xElement)
        {
            xElement = null;

            if (_udtWrapper.UnderlyingType.EqualsOrIsNullableOf(obj.GetType())) return false;

            // this block of code runs if the serializer is instantiated with a
            // another base value such as System.Object but is provided with an
            // object of its child
            var ser = NewInternalSerializer(obj.GetType(), TypeNamespace, null);
            var xDocument = ser.SerializeToXDocument(obj);
            xElement = xDocument.Root!;

            // do not pop from stack because the new internal serializer was sufficient for the whole serialization 
            // and this instance of serializer did not do anything extra
            FinalizeNewSerializer(ser, true, false);
            xElement.Name = _udtWrapper.Alias;

            AddMetadataAttribute(xElement, Options.Namespace.Uri + Options.AttributeName.RealType,
                obj.GetType().FullName,
                _documentDefaultNamespace);
            _xmlNamespaceManager.AddNamespacesToElement(xElement, _documentDefaultNamespace, Options, _udtWrapper);

            return true;
        }

#nullable disable
        private void PushObjectToSerializationStack(object obj)
        {
            if (!obj.GetType().IsValueType)
                _serializedStack.Push(obj);
        }

        /// <summary>
        ///     Sets the base XML element. This method is used when an <c>XMLSerializer</c>
        ///     instantiates another <c>XMLSerializer</c> to serialize nested objects.
        ///     Through this method the child objects have access to the already serialized elements of
        ///     their parent.
        /// </summary>
        /// <param name="baseElement">The base XML element.</param>
        private void SetBaseElement(XElement baseElement)
        {
            _baseElement = baseElement;
        }

        /// <summary>
        ///     The base method that performs the whole job of serialization.
        ///     Other serialization methods call this method to have their job done.
        /// </summary>
        /// <param name="obj">The object to be serialized</param>
        /// <param name="className">Name of the element that contains the serialized object.</param>
        /// <returns>
        ///     an instance of <c>XElement</c> which contains the result of
        ///     serialization of the specified object
        /// </returns>
        private XElement SerializeBase(object obj, XName className)
        {
            // this is set once again here since internal serializers
            // must not call public Serialize methods
            _isSerializing = true;

            SetBaseElement(className);

            if (RecursionCount >= Options.MaxRecursion - 1)
            {
                PushObjectToSerializationStack(obj);
                return _baseElement;
            }

            if (!_type.IsValueType)
            {
                var alreadySerializedObject = _serializedStack.FirstOrDefault(x => ReferenceEquals(x, obj));
                if (alreadySerializedObject != null)
                {
                    if (!_udtWrapper.ThrowUponSerializingCyclingReferences)
                    {
                        // although we are not going to serialize anything, push the object to be picked up
                        // by the pop statement right after serialization
                        PushObjectToSerializationStack(obj);
                        return _baseElement;
                    }

                    throw new YAXCannotSerializeSelfReferentialTypes(_type);
                }

                PushObjectToSerializationStack(obj);
            }

            if (_udtWrapper.HasComment && _baseElement.Parent == null && _mainDocument != null)
                foreach (var comment in _udtWrapper.Comment)
                    _mainDocument.Add(new XComment(comment));

            // if the containing element is set to preserve spaces, then emit the 
            // required attribute
            if (_udtWrapper.PreservesWhitespace) XMLUtils.AddPreserveSpaceAttribute(_baseElement, Options.Culture);

            // check if the main class/type has defined custom serializers
            if (_udtWrapper.HasCustomSerializer)
            {
                InvokeCustomSerializerToElement(_udtWrapper.CustomSerializerType, obj, _baseElement, null, _udtWrapper, this);
            }
            else if (KnownTypes.IsKnowType(_type))
            {
                KnownTypes.Serialize(obj, _baseElement, TypeNamespace);
            }
            else // no custom serializers or known type
            {
                SerializeFields(obj);
            }

            if (_baseElement.Parent == null) _xmlNamespaceManager.AddNamespacesToElement(_baseElement, _documentDefaultNamespace, Options, _udtWrapper);

            return _baseElement;
        }

        private void SerializeFields(object obj)
        {
            var isAnythingFoundToSerialize = false;

            // iterate through public properties
            foreach (var member in GetFieldsToBeSerialized())
            {
                if (member.HasNamespace) 
                    _xmlNamespaceManager.RegisterNamespace(member.Namespace, member.NamespacePrefix);

                if (IsNothingToSerialize(obj, member, out var elementValue)) continue;

                // make this flag true, so that we know that this object was not empty of fields
                isAnythingFoundToSerialize = true;

                var areOfSameType = AreOfSameType(obj, member, elementValue);

                var hasCustomSerializer =
                    member.HasCustomSerializer || member.MemberTypeWrapper.HasCustomSerializer;
                var isCollectionSerially = member.CollectionAttributeInstance is
                    { SerializationType: YAXCollectionSerializationTypes.Serially };
                var isKnownType = member.IsKnownType;

                var serializationLocation = member.SerializationLocation;
                var canSerializeAsAttributeOrValue =
                    areOfSameType || hasCustomSerializer || isCollectionSerially || isKnownType;

                // it gets true only for basic data types
                if (member.IsSerializedAsAttribute && canSerializeAsAttributeOrValue)
                {
                    SerializeAsAttribute(member, elementValue, (hasCustomSerializer, isCollectionSerially, isKnownType));
                }
                else if (member.IsSerializedAsValue && canSerializeAsAttributeOrValue)
                {
                    SerializeAsValue(member, elementValue, serializationLocation, isCollectionSerially);
                }
                else
                {
                    SerializeAsElement(member, elementValue, serializationLocation,
                        (hasCustomSerializer, isKnownType, areOfSameType));
                }
            }

            // This is an important step
            RemoveElementIfNecessary(isAnythingFoundToSerialize);
        }

        /// <summary>
        ///     It checks, if all the members of an element have been serialized
        ///     somewhere else, leaving the containing member empty.
        /// <para>
        ///     In this case, remove that element by itself, <b>unless</b> the element is empty, because the 
        ///     corresponding object did not have any fields to serialize (e.g., DBNull, Random).
        ///     Then keep the element.
        /// </para>
        /// </summary>
        /// <param name="isAnythingFoundToSerialize">
        ///     A flag that indicates whether the object has any fields to be serialized.
        ///     If it is <see langword="false"/>, then we will not remove
        ///     the containing element from the resulting XML.
        /// </param>
        private void RemoveElementIfNecessary(bool isAnythingFoundToSerialize)
        {
            if (_baseElement.Parent != null &&
                XMLUtils.IsElementCompletelyEmpty(_baseElement) &&
                isAnythingFoundToSerialize)
                _baseElement.Remove();
        }

        /// <summary>
        /// Tests for conditions, where there is nothing to be serialized.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="member"></param>
        /// <param name="elementValue"></param>
        /// <returns></returns>
#nullable enable
        private bool IsNothingToSerialize(object obj, MemberWrapper member, out object? elementValue)
        {
            elementValue = null;

            if (!member.CanRead)
                return true;

            // ignore this member if it is attributed as don't serialize
            if (member.IsAttributedAsDontSerialize)
                return true;

            elementValue = member.GetValue(obj);

            // ignore this member if it is null and we are not about to serialize null objects
            if (elementValue == null &&
                _udtWrapper.IsNotAllowedNullObjectSerialization)
                return true;

            if (elementValue == null &&
                member.IsAttributedAsDontSerializeIfNull)
                return true;
            
            return false;
        }
#nullable disable

        private void SetBaseElement(XName className)
        {
            if (_baseElement == null)
            {
                _baseElement = CreateElementWithNamespace(_udtWrapper, className);
            }
            else
            {
                var baseElem = new XElement(className, null);
                _baseElement.Add(baseElem);
                _baseElement = baseElem;
            }
        }

        private void SerializeAsElement(MemberWrapper member,
            object elementValue, string serializationLocation, (bool hasCustomSerializer,
            bool isKnownType, bool areOfSameType) flags)
        {
            var (hasCustomSerializer, isKnownType, areOfSameType) = flags;

            // Throw, if no parent element can be found
            var parElem = GetParentElement(serializationLocation);

            AddComments(parElem, member);

            if (hasCustomSerializer)
            {
                InvokeCustomSerializer(parElem, elementValue, member);
            }
            else if (isKnownType)
            {
                AddKnownTypeElement(parElem, elementValue, member);
            }
            else
            {
                AddElement(parElem, elementValue, member, areOfSameType);
            }
        }

        /// <summary>
        ///    Adds a 'regular' <see cref="XElement"/> to the parent element.
        /// </summary>
        /// <param name="parElem"></param>
        /// <param name="elementValue"></param>
        /// <param name="member"></param>
        /// <param name="areOfSameType"></param>
        private void AddElement(XElement parElem, object elementValue, MemberWrapper member, bool areOfSameType)
        {
            // make an element with the provided data
            var elemToAdd = MakeElement(parElem, member, elementValue, out var moveDescOnly,
                out var alreadyAdded);
            if (!areOfSameType)
            {
                var realType = elementValue.GetType();

                var realTypeDefinition = member.GetRealTypeDefinition(realType);
                if (realTypeDefinition != null)
                {
                    var alias = realTypeDefinition.Alias;
                    if (string.IsNullOrEmpty(alias))
                    {
                        var typeWrapper = TypeWrappersPool.Pool.GetTypeWrapper(realType, this);
                        alias = typeWrapper.Alias.LocalName;
                    }

                    // TODO: see how namespace is handled in other parts of the code and do the same thing
                    elemToAdd.Name = XName.Get(alias, elemToAdd.Name.Namespace.NamespaceName);
                }
                else
                {
                    AddMetadataAttribute(elemToAdd, Options.Namespace.Uri + Options.AttributeName.RealType,
                        realType.FullName, _documentDefaultNamespace);
                }
            }

            if (moveDescOnly) // if only the descendants of the resulting element are going to be added ...
            {
                XMLUtils.MoveDescendants(elemToAdd, parElem);
                if (elemToAdd.Parent == parElem)
                    elemToAdd.Remove();
            }
            else if (!alreadyAdded)
            {
                // see if such element already exists
                var existingElem = parElem.Element(member.Alias.OverrideNsIfEmpty(TypeNamespace));
                if (existingElem == null)
                {
                    // if not add the new element gracefully
                    parElem.Add(elemToAdd);
                }
                else // if an element with our desired name already exists
                {
                    if (ReflectionUtils.IsBasicType(member.MemberType))
                        existingElem.SetValue(elementValue);
                    else
                        XMLUtils.MoveDescendants(elemToAdd, existingElem);
                }
            }
        }

        private void AddKnownTypeElement(XElement parElem, object elementValue, MemberWrapper member)
        {
            var elemToFill = new XElement(member.Alias.OverrideNsIfEmpty(TypeNamespace));
            parElem.Add(elemToFill);
            KnownTypes.Serialize(elementValue, elemToFill,
                member.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThen(XNamespace.None));
            if (member.PreservesWhitespace)
                XMLUtils.AddPreserveSpaceAttribute(elemToFill, Options.Culture);
        }

        private void InvokeCustomSerializer(XElement parElem, object elementValue, MemberWrapper member)
        {
            var elemToFill = new XElement(member.Alias.OverrideNsIfEmpty(TypeNamespace));
            parElem.Add(elemToFill);
            if (member.HasCustomSerializer)
                InvokeCustomSerializerToElement(member.CustomSerializerType, elementValue, elemToFill, member, null, this);
            else if (member.MemberTypeWrapper.HasCustomSerializer)
                InvokeCustomSerializerToElement(member.MemberTypeWrapper.CustomSerializerType, elementValue, elemToFill,
                    null, member.MemberTypeWrapper, this);

            if (member.PreservesWhitespace)
                XMLUtils.AddPreserveSpaceAttribute(elemToFill, Options.Culture);
        }

        private static void AddComments(XElement parElem, MemberWrapper member)
        {
            if (!member.HasComment) return;

            foreach (var comment in member.Comment!)
                parElem.Add(new XComment(comment));
        }

        /// <summary>
        /// Gets the parent <see cref="XElement"/>.
        /// </summary>
        /// <param name="serializationLocation"></param>
        /// <returns></returns>
        /// <exception cref="YAXBadLocationException"></exception>
        private XElement GetParentElement(string serializationLocation)
        {
            // find the parent element from its location
            var parElem = XMLUtils.FindLocation(_baseElement, serializationLocation);
            if (parElem != null) return parElem;

            // see if the location can be created
            if (!XMLUtils.CanCreateLocation(_baseElement, serializationLocation))
                throw new YAXBadLocationException(serializationLocation);
            
            // try to create the location
            parElem = XMLUtils.CreateLocation(_baseElement, serializationLocation);
            if (parElem == null)
                throw new YAXBadLocationException(serializationLocation);

            return parElem;
        }

        private void SerializeAsValue(MemberWrapper member, object elementValue,
            string serializationLocation,
            bool isCollectionSerially)
        {
            var isKnownType = member.IsKnownType;

            // find the parent element from its location
            var parElem = XMLUtils.FindLocation(_baseElement, serializationLocation);
            if (parElem == null) // if the parent element does not exist
            {
                // see if the location can be created
                if (!XMLUtils.CanCreateLocation(_baseElement, serializationLocation))
                    throw new YAXBadLocationException(serializationLocation);
                // try to create the location
                parElem = XMLUtils.CreateLocation(_baseElement, serializationLocation);
                if (parElem == null)
                    throw new YAXBadLocationException(serializationLocation);
            }

            // if control is moved here, it means that the parent 
            // element has been found/created successfully

            string valueToSet;
            if (member.HasCustomSerializer)
            {
                valueToSet =
                    InvokeCustomSerializerToValue(member.CustomSerializerType, elementValue, member, _udtWrapper, this);
            }
            else if (member.MemberTypeWrapper.HasCustomSerializer)
            {
                valueToSet = InvokeCustomSerializerToValue(member.MemberTypeWrapper.CustomSerializerType, elementValue, member,
                    _udtWrapper, this);
            }
            else if (isKnownType)
            {
                var tempLoc = new XElement("temp");
                KnownTypes.Serialize(elementValue, tempLoc, string.Empty);
                valueToSet = tempLoc.Value;
            }
            else if (isCollectionSerially)
            {
                var tempLoc = new XElement("temp");
                var added = MakeCollectionElement(tempLoc, "name", elementValue,
                    member.CollectionAttributeInstance, member.Format);
                valueToSet = added.Value;
            }
            else
            {
                valueToSet = elementValue.ToXmlValue(Options.Culture);
            }

            parElem.Add(new XText(valueToSet));
            if (member.PreservesWhitespace)
                XMLUtils.AddPreserveSpaceAttribute(parElem, Options.Culture);
        }

        private void SerializeAsAttribute(MemberWrapper member, object elementValue,
            (bool hasCustomSerializer, bool isCollectionSerially, bool isKnownType) flags)
        {
            var (hasCustomSerializer, isCollectionSerially, isKnownType) = flags;
            var useElementValue = !(hasCustomSerializer || isCollectionSerially || isKnownType);
            // Fill the base element with an attribute
            var attributeToUse = CreateAttribute(member, elementValue, useElementValue);
            if (member.HasCustomSerializer)
            {
                InvokeCustomSerializerToAttribute(member.CustomSerializerType, elementValue, attributeToUse, member,
                    _udtWrapper, this);
            }
            else if (member.MemberTypeWrapper.HasCustomSerializer)
            {
                InvokeCustomSerializerToAttribute(member.MemberTypeWrapper.CustomSerializerType, elementValue,
                    attributeToUse, member, _udtWrapper, this);
            }
            else if (member.IsKnownType)
            {
                // TODO: create a functionality to serialize to XAttributes
                //KnownTypes.Serialize(attrToCreate, member.MemberType);
            }
            else if (isCollectionSerially)
            {
                var tempLoc = new XElement("temp");
                var added = MakeCollectionElement(tempLoc, "name", elementValue,
                    member.CollectionAttributeInstance, member.Format);
                attributeToUse.Value = added.Value;
            }

            // if member does not have any type wrappers then it has been already populated with the CreateAttribute method
        }

        private XAttribute CreateAttribute(MemberWrapper member, object elementValue, bool useElementValue)
        {
            var serializationLocation = member.SerializationLocation;

            if (XMLUtils.AttributeExists(_baseElement, serializationLocation,
                    member.Alias.OverrideNsIfEmpty(TypeNamespace)))
            {
                throw new YAXAttributeAlreadyExistsException(member.Alias?.LocalName);
            }

            var attribute = XMLUtils.CreateAttribute(_baseElement,
                serializationLocation, member.Alias.OverrideNsIfEmpty(TypeNamespace),
                useElementValue ? elementValue : string.Empty,
                _documentDefaultNamespace, Options.Culture);

            _xmlNamespaceManager.RegisterNamespace(member.Alias.OverrideNsIfEmpty(TypeNamespace).Namespace, null);

            if (attribute == null) throw new YAXBadLocationException(serializationLocation);

            return attribute;
        }

        /// <summary>
        ///     Adds the namespace applying to the object type specified in <paramref name="wrapper" />
        ///     to the <paramref name="className" />
        /// </summary>
        /// <param name="wrapper">The wrapper around the object who's namespace should be added</param>
        /// <param name="className">The root node of the document to which the namespace should be written</param>
        private XElement CreateElementWithNamespace(UdtWrapper wrapper, XName className)
        {
            var elemName = className.OverrideNsIfEmpty(wrapper.Namespace);
            if (elemName.Namespace == wrapper.Namespace)
                _xmlNamespaceManager.RegisterNamespace(elemName.Namespace, wrapper.NamespacePrefix);
            else
                _xmlNamespaceManager.RegisterNamespace(elemName.Namespace, null);

            return new XElement(elemName, null);
        }

        /// <summary>
        ///     Makes the element corresponding to the member specified.
        /// </summary>
        /// <param name="insertionLocation">The insertion location.</param>
        /// <param name="member">The member to serialize.</param>
        /// <param name="elementValue">The element value.</param>
        /// <param name="moveDescOnly">
        ///     if set to <see langword="true" /> specifies that only the descendants of the resulting element should be
        ///     added to the parent.
        /// </param>
        /// <param name="alreadyAdded">
        ///     if set to <see langword="true" /> specifies the element returned is
        ///     already added to the parent element and should not be added once more.
        /// </param>
        /// <returns></returns>
        private XElement MakeElement(XElement insertionLocation, MemberWrapper member, object elementValue,
            out bool moveDescOnly, out bool alreadyAdded)
        {
            moveDescOnly = false;

            _xmlNamespaceManager.RegisterNamespace(member.Namespace, member.NamespacePrefix);

            XElement elemToAdd;
            if (member.IsTreatedAsDictionary)
            {
                elemToAdd = MakeDictionaryElement(insertionLocation, member.Alias.OverrideNsIfEmpty(TypeNamespace),
                    elementValue, member.DictionaryAttributeInstance, member.CollectionAttributeInstance,
                    member.IsAttributedAsDontSerializeIfNull);
                if (member.CollectionAttributeInstance != null &&
                    member.CollectionAttributeInstance.SerializationType ==
                    YAXCollectionSerializationTypes.RecursiveWithNoContainingElement &&
                    !elemToAdd.HasAttributes)
                    moveDescOnly = true;
                alreadyAdded = elemToAdd.Parent == insertionLocation;
            }
            else if (member.IsTreatedAsCollection)
            {
                elemToAdd = MakeCollectionElement(insertionLocation, member.Alias.OverrideNsIfEmpty(TypeNamespace),
                    elementValue, member.CollectionAttributeInstance, member.Format);

                if (member.CollectionAttributeInstance != null &&
                    member.CollectionAttributeInstance.SerializationType ==
                    YAXCollectionSerializationTypes.RecursiveWithNoContainingElement &&
                    !elemToAdd.HasAttributes)
                    moveDescOnly = true;
                alreadyAdded = elemToAdd.Parent == insertionLocation;
            }
            else
            {
                elemToAdd = MakeBaseElement(insertionLocation, member.Alias.OverrideNsIfEmpty(TypeNamespace),
                    elementValue, out alreadyAdded);
            }

            if (member.PreservesWhitespace)
                XMLUtils.AddPreserveSpaceAttribute(elemToAdd, Options.Culture);

            return elemToAdd;
        }

        /// <summary>
        ///     Creates a dictionary element according to the specified options, as described
        ///     by the attribute instances.
        /// </summary>
        /// <param name="insertionLocation">The insertion location.</param>
        /// <param name="elementName">Name of the element.</param>
        /// <param name="elementValue">The element value, corresponding to a dictionary object.</param>
        /// <param name="dicAttrInst">reference to the dictionary attribute instance.</param>
        /// <param name="collectionAttrInst">reference to collection attribute instance.</param>
        /// <param name="dontSerializeNull"><see langword="true"/> to not serialize <see langword="null"/></param>
        /// <returns>
        ///     an instance of <see cref="XElement"/> which contains the dictionary object
        ///     serialized properly
        /// </returns>
        private XElement MakeDictionaryElement(XElement insertionLocation, XName elementName, object elementValue,
            YAXDictionaryAttribute dicAttrInst, YAXCollectionAttribute collectionAttrInst, bool dontSerializeNull)
        {
            if (elementValue == null) return new XElement(elementName);

            if (!ReflectionUtils.IsIDictionary(elementValue.GetType(), out var keyType, out var valueType))
                throw new ArgumentException($"{nameof(elementValue)} must be a Dictionary", nameof(elementValue));

            // Serialize non-collection members
            var elemToAdd = SerializeUsingInternalSerializer(insertionLocation, elementName, elementValue);

            // now iterate through collection members

            // An IDictionary implements IEnumerable, so we can cast
            var dicInst = (IEnumerable) elementValue;
            
            (Type keyType, Type valueType, bool isKeyAttrib, bool isValueAttrib, bool isKeyContent, bool isValueContent,
                string keyFormat, string
                valueFormat, XName keyAlias, XName valueAlias) details = (keyType, valueType, false, false, false,
                    false, null, null,
                    elementName.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone() + "Key",
                    elementName.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone() + "Value");
            
            _ = GetElementName(collectionAttrInst, elementName, out var eachElementName);

            if (dicAttrInst != null)
            {
                _ = GetElementName(dicAttrInst, elementName, out eachElementName);

                details = GetDictionaryAttributeDetails(elementName, dicAttrInst, details);
            }

            foreach (var obj in dicInst)
            {
                var elemChild = CreateDictionaryElement(elementName, eachElementName, obj, dontSerializeNull, details);

                elemToAdd.Add(elemChild);
            }

            return elemToAdd;
        }

        private XElement SerializeUsingInternalSerializer(XElement insertionLocation, XName elementName, object elementValue)
        {
            var ser = NewInternalSerializer(elementValue.GetType(), elementName.Namespace, insertionLocation);
            var elem = ser.SerializeBase(elementValue, elementName);
            FinalizeNewSerializer(ser, true);
            return elem;
        }

        private XElement CreateDictionaryElement(XName elementName, XName eachElementName, object obj,
            bool dontSerializeNull,
            (Type keyType, Type valueType, bool isKeyAttrib, bool isValueAttrib, bool isKeyContent, bool isValueContent,
                string keyFormat, string valueFormat, XName keyAlias, XName valueAlias) details)
        {
            var keyObj = obj.GetType().GetProperty("Key")!.GetValue(obj, null);
            var valueObj = obj.GetType().GetProperty("Value")!.GetValue(obj, null);

            var areKeyOfSameType = keyObj == null || keyObj.GetType().EqualsOrIsNullableOf(details.keyType);
            var areValueOfSameType = valueObj == null || valueObj.GetType().EqualsOrIsNullableOf(details.valueType);

            keyObj = ReflectionUtils.TryFormatObject(keyObj, details.keyFormat);
            valueObj = ReflectionUtils.TryFormatObject(valueObj, details.valueFormat);

            if (eachElementName == null)
            {
                eachElementName =
                    StringUtils.RefineSingleElement(ReflectionUtils.GetTypeFriendlyName(obj.GetType()));
                eachElementName =
                    eachElementName.OverrideNsIfEmpty(elementName.Namespace.IfEmptyThen(TypeNamespace)
                        .IfEmptyThenNone());
            }

            var elemChild = new XElement(eachElementName, null);

            if (details.isKeyAttrib && areKeyOfSameType)
            {
                elemChild.AddAttributeNamespaceSafe(details.keyAlias, keyObj, _documentDefaultNamespace, Options.Culture);
            }
            else if (details.isKeyContent && areKeyOfSameType)
            {
                elemChild.AddXmlContent(keyObj, Options.Culture);
            }
            else
            {
                var addedElem = AddObjectToElement(elemChild, details.keyAlias, keyObj);
                if (!areKeyOfSameType)
                {
                    if (addedElem.Parent == null)
                        // sometimes empty elements are removed because its members are serialized in
                        // other elements, therefore we need to make sure to re-add the element.
                        elemChild.Add(addedElem);

                    // keyObj can't be null, if areKeyOfSameType is false
                    AddMetadataAttribute(addedElem, Options.Namespace.Uri + Options.AttributeName.RealType,
                        keyObj!.GetType().FullName, _documentDefaultNamespace);
                }
            }

            if (details.isValueAttrib && areValueOfSameType)
            {
                elemChild.AddAttributeNamespaceSafe(details.valueAlias, valueObj, _documentDefaultNamespace, Options.Culture);
            }
            else if (details.isValueContent && areValueOfSameType)
            {
                elemChild.AddXmlContent(valueObj, Options.Culture);
            }
            else if (!(valueObj == null && dontSerializeNull))
            {
                var addedElem = AddObjectToElement(elemChild, details.valueAlias, valueObj);
                if (!areValueOfSameType)
                {
                    if (addedElem.Parent == null)
                        // sometimes empty elements are removed because its members are serialized in
                        // other elements, therefore we need to make sure to re-add the element.
                        elemChild.Add(addedElem);

                    AddMetadataAttribute(addedElem, Options.Namespace.Uri + Options.AttributeName.RealType,
                        valueObj!.GetType().FullName, _documentDefaultNamespace);
                }
            }

            return elemChild;
        }

        private (Type keyType, Type valueType, bool isKeyAttrib, bool isValueAttrib, bool isKeyContent, bool
            isValueContent,
            string keyFormat, string valueFormat, XName keyAlias, XName valueAlias)
            GetDictionaryAttributeDetails(XName elementName,
                YAXDictionaryAttribute dicAttrInst,
                (Type keyType, Type valueType, bool isKeyAttrib, bool isValueAttrib, bool isKeyContent, bool
                    isValueContent, string
                    keyFormat, string valueFormat, XName keyAlias, XName valueAlias) details)
        {
            // Process key
            if (dicAttrInst.SerializeKeyAs == YAXNodeTypes.Attribute)
                details.isKeyAttrib = ReflectionUtils.IsBasicType(details.keyType);
            else if (dicAttrInst.SerializeKeyAs == YAXNodeTypes.Content)
                details.isKeyContent = ReflectionUtils.IsBasicType(details.keyType);

            // Process value
            if (dicAttrInst.SerializeValueAs == YAXNodeTypes.Attribute)
                details.isValueAttrib = ReflectionUtils.IsBasicType(details.valueType);
            else if (dicAttrInst.SerializeValueAs == YAXNodeTypes.Content)
                details.isValueContent = ReflectionUtils.IsBasicType(details.valueType);

            details.keyFormat = dicAttrInst.KeyFormatString;
            details.valueFormat = dicAttrInst.ValueFormatString;

            details.keyAlias = StringUtils.RefineSingleElement(dicAttrInst.KeyName ?? "Key");
            if (details.keyAlias.Namespace.IsEmpty())
                _xmlNamespaceManager.RegisterNamespace(details.keyAlias.Namespace, null);
            details.keyAlias = details.keyAlias.OverrideNsIfEmpty(
                elementName.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone());

            details.valueAlias = StringUtils.RefineSingleElement(dicAttrInst.ValueName ?? "Value");
            if (details.valueAlias.Namespace.IsEmpty())
                _xmlNamespaceManager.RegisterNamespace(details.valueAlias.Namespace, null);
            details.valueAlias =
                details.valueAlias.OverrideNsIfEmpty(elementName.Namespace.IfEmptyThen(TypeNamespace)
                    .IfEmptyThenNone());

            return details;
        }

#nullable enable
        private bool GetElementName(YAXCollectionAttribute? collectionAttrInst, XName elementName,
            out XName? eachElementName)
        {
            eachElementName = null;

            if (collectionAttrInst == null || string.IsNullOrEmpty(collectionAttrInst.EachElementName)) return false;

            eachElementName = StringUtils.RefineSingleElement(collectionAttrInst.EachElementName);
            if (eachElementName.Namespace.IsEmpty())
                _xmlNamespaceManager.RegisterNamespace(eachElementName.Namespace, null);
            eachElementName =
                eachElementName.OverrideNsIfEmpty(
                    elementName.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone());
            return true;
        }

        private bool GetElementName(YAXDictionaryAttribute? dicAttrInst, XName elementName,
            out XName? eachElementName)
        {
            eachElementName = null;
            if (dicAttrInst == null || dicAttrInst.EachPairName == null) return false;

            eachElementName = StringUtils.RefineSingleElement(dicAttrInst.EachPairName);
            if (eachElementName.Namespace.IsEmpty())
                _xmlNamespaceManager.RegisterNamespace(eachElementName.Namespace, null);
            eachElementName =
                eachElementName.OverrideNsIfEmpty(elementName.Namespace.IfEmptyThen(TypeNamespace)
                    .IfEmptyThenNone());
            return true;
        }
#nullable disable

        /// <summary>
        ///     Adds an element containing data related to the specified object, to an existing xml element.
        /// </summary>
        /// <param name="elem">The parent element.</param>
        /// <param name="alias">The name for the element to be added.</param>
        /// <param name="obj">
        ///     The object corresponding to which an element is going to be added to
        ///     an existing parent element.
        /// </param>
        /// <returns>the enclosing XML element.</returns>
        private XElement AddObjectToElement(XElement elem, XName alias, object obj)
        {
            UdtWrapper udt = null;
            if (obj != null)
                udt = TypeWrappersPool.Pool.GetTypeWrapper(obj.GetType(), this);

            if (alias == null && udt != null)
                alias = udt.Alias.OverrideNsIfEmpty(TypeNamespace);

            XElement elemToAdd = null;

            if (udt != null && udt.IsTreatedAsDictionary)
            {
                elemToAdd = MakeDictionaryElement(elem, alias, obj, null, null, udt.IsNotAllowedNullObjectSerialization);
                if (elemToAdd.Parent != elem)
                    elem.Add(elemToAdd);
            }
            else if (udt != null && udt.IsTreatedAsCollection)
            {
                elemToAdd = MakeCollectionElement(elem, alias, obj, null, null);
                if (elemToAdd.Parent != elem)
                    elem.Add(elemToAdd);
            }
            else if (udt != null && udt.IsEnum)
            {
                elemToAdd = MakeBaseElement(elem, alias, udt.EnumWrapper.GetAlias(obj), out var alreadyAdded);
                if (!alreadyAdded)
                    elem.Add(elemToAdd);
            }
            else
            {
                elemToAdd = MakeBaseElement(elem, alias, obj, out var alreadyAdded);
                if (!alreadyAdded)
                    elem.Add(elemToAdd);
            }

            return elemToAdd;
        }

        /// <summary>
        ///     Serializes a collection object.
        /// </summary>
        /// <param name="insertionLocation">The insertion location.</param>
        /// <param name="elementName">Name of the element.</param>
        /// <param name="elementValue">The object to be serialized.</param>
        /// <param name="collectionAttrInst">The collection attribute instance.</param>
        /// <param name="format">formatting string, which is going to be applied to all members of the collection.</param>
        /// <returns>
        ///     an instance of <see cref="XElement"/> which contains the collection
        ///     serialized properly
        /// </returns>
        private XElement MakeCollectionElement(
            XElement insertionLocation, XName elementName, object elementValue,
            YAXCollectionAttribute collectionAttrInst, string format)
        {
            if (elementValue == null)
                return new XElement(elementName);

            if (elementValue is not IEnumerable collectionInst)
                throw new ArgumentException($"{nameof(elementName)} must be an IEnumerable", nameof(elementValue));

            // Serialize non-collection members
            var elemToAdd = SerializeUsingInternalSerializer(insertionLocation, elementName, collectionInst);

            // now iterate through collection members

            var serType = YAXCollectionSerializationTypes.Recursive;
            var separator = string.Empty;
            XName eachElementName = null;

            if (collectionAttrInst != null)
            {
                serType = collectionAttrInst.SerializationType;
                separator = collectionAttrInst.SeparateBy;
                _ = GetElementName(collectionAttrInst, elementName, out eachElementName);
            }

            var colItemType = ReflectionUtils.GetCollectionItemType(collectionInst.GetType());
            var colItemsUdt = TypeWrappersPool.Pool.GetTypeWrapper(colItemType, this);

            if (serType == YAXCollectionSerializationTypes.Serially && !ReflectionUtils.IsBasicType(colItemType))
                serType = YAXCollectionSerializationTypes.Recursive;

            if (serType == YAXCollectionSerializationTypes.Serially && elemToAdd.IsEmpty)
            {
                elemToAdd = MakeSerialTypeCollectionElement(insertionLocation, elementName, format, collectionInst, colItemsUdt, separator);
            }
            else
            {
                AddCollectionItems(elemToAdd, elementName, eachElementName, collectionInst, colItemsUdt, format,
                    colItemType);
            }

            var arrayDims = ReflectionUtils.GetArrayDimensions(collectionInst);
            if (arrayDims != null && arrayDims.Length > 1)
                AddMetadataAttribute(elemToAdd, Options.Namespace.Uri + Options.AttributeName.Dimensions,
                    StringUtils.GetArrayDimsString(arrayDims), _documentDefaultNamespace);

            return elemToAdd;
        }

        private void AddCollectionItems(XElement elemToAdd, XName elementName, XName eachElementName,
            IEnumerable collectionInst, UdtWrapper colItemsUdt, string format, Type colItemType)
        {
            foreach (var obj in collectionInst)
            {
                var objToAdd = ReflectionUtils.TryFormatObject(obj, format);
                var curElemName = eachElementName;

                if (curElemName == null) curElemName = colItemsUdt.Alias;

                var itemElem = AddObjectToElement(elemToAdd, curElemName.OverrideNsIfEmpty(elementName.Namespace),
                    objToAdd);

                if (AreOfSameType(obj, colItemType)) continue;

                // i.e., it has been removed, e.g., because all its members have been serialized outside the element
                if (itemElem.Parent == null)
                    elemToAdd.Add(itemElem); // return it back

                AddMetadataAttribute(itemElem, Options.Namespace.Uri + Options.AttributeName.RealType,
                    obj.GetType().FullName, _documentDefaultNamespace);
            }
        }

#nullable enable
        /// <summary>
        ///     Are element value and the member declared type the same?
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="member"></param>
        /// <param name="elementValue"></param>
        /// <returns></returns>
        private static bool AreOfSameType(object? obj, MemberWrapper member, object? elementValue)
        {
            var originalValue = member.GetOriginalValue(obj, null);
            return (elementValue == null && originalValue == null) || member.MemberType.EqualsOrIsNullableOf(originalValue.GetType());
        }

        /// <summary>
        ///     Are <paramref name="obj"/>object type and type <paramref name="toCompare"/> the same?
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="toCompare"></param>
        /// <returns></returns>
        private static bool AreOfSameType(object? obj, Type toCompare)
        {
            return obj == null || obj.GetType().EqualsOrIsNullableOf(toCompare);
        }

        private XElement? MakeSerialTypeCollectionElement(XElement insertionLocation, XName elementName, string format,
            IEnumerable collectionInst, UdtWrapper colItemsUdt, string separator)
        {
            var sb = new StringBuilder();

            var isFirst = true;
            foreach (var obj in collectionInst)
            {
                object? objToAdd;
                if (colItemsUdt.IsEnum)
                    objToAdd = colItemsUdt.EnumWrapper.GetAlias(obj);
                else if (format != null)
                    objToAdd = ReflectionUtils.TryFormatObject(obj, format);
                else
                    objToAdd = obj;

                if (isFirst)
                {
                    sb.Append(objToAdd.ToXmlValue(Options.Culture));
                    isFirst = false;
                }
                else
                {
                    sb.AppendFormat(Options.Culture, "{0}{1}", separator, objToAdd);
                }
            }

            var elemToAdd = MakeBaseElement(insertionLocation, elementName, sb.ToString(), out var alreadyAdded);
            return !alreadyAdded ? elemToAdd : null;
        }
#nullable disable

        /// <summary>
        ///     Makes an XML element with the specified name, corresponding to the object specified.
        /// </summary>
        /// <param name="insertionLocation">The insertion location.</param>
        /// <param name="name">The name of the element.</param>
        /// <param name="value">The object to be serialized in an XML element.</param>
        /// <param name="alreadyAdded">
        ///     if set to <see langword="true" /> specifies the element returned is
        ///     already added to the parent element and should not be added once more.
        /// </param>
        /// <returns>
        ///     an instance of <c>XElement</c> which will contain the serialized object,
        ///     or <c>null</c> if the serialized object is already added to the base element
        /// </returns>
        private XElement MakeBaseElement(XElement insertionLocation, XName name, object value, out bool alreadyAdded)
        {
            alreadyAdded = false;
            if (value == null || ReflectionUtils.IsBasicType(value.GetType()))
            {
                if (value != null)
                    value = value.ToXmlValue(Options.Culture);

                return new XElement(name, value);
            }

            if (ReflectionUtils.IsStringConvertibleIFormattable(value.GetType()))
            {
                var elementValue = value.GetType().InvokeMethod("ToString", value, Array.Empty<object>());
                //object elementValue = value.GetType().InvokeMember("ToString", BindingFlags.InvokeMethod, null, value, new object[0]);
                return new XElement(name, elementValue);
            }

            var elem = SerializeUsingInternalSerializer(insertionLocation, name, value);
            alreadyAdded = true;
            return elem;
        }

        private static void InvokeCustomSerializerToElement(Type customSerType, object objToSerialize, XElement elemToFill, MemberWrapper memberWrapper, UdtWrapper udtWrapper, YAXSerializer currentSerializer)
        {
            var customSerializer = Activator.CreateInstance(customSerType, Array.Empty<object>());
            customSerType.InvokeMethod("SerializeToElement", customSerializer, new[] {objToSerialize, elemToFill, new SerializationContext(memberWrapper, udtWrapper, currentSerializer.Options) });
        }

        private static void InvokeCustomSerializerToAttribute(Type customSerType, object objToSerialize, XAttribute attrToFill, MemberWrapper memberWrapper, UdtWrapper udtWrapper, YAXSerializer currentSerializer)
        {
            var customSerializer = Activator.CreateInstance(customSerType, Array.Empty<object>());
            customSerType.InvokeMethod("SerializeToAttribute", customSerializer, new[] {objToSerialize, attrToFill, new SerializationContext(memberWrapper, udtWrapper, currentSerializer.Options) });
        }

        private static string InvokeCustomSerializerToValue(Type customSerType, object objToSerialize, MemberWrapper memberWrapper, UdtWrapper udtWrapper, YAXSerializer currentSerializer)
        {
            Debug.Assert(memberWrapper != null && udtWrapper != null);
            var customSerializer = Activator.CreateInstance(customSerType, Array.Empty<object>());
            return (string) customSerType.InvokeMethod("SerializeToValue", customSerializer, new[] {objToSerialize, new SerializationContext(memberWrapper, udtWrapper, currentSerializer.Options) });
        }

        private void AddMetadataAttribute(XElement parent, XName attrName, object attrValue,
            XNamespace documentDefaultNamespace)
        {
            if (!_udtWrapper.SuppressMetadataAttributes)
            {
                parent.AddAttributeNamespaceSafe(attrName, attrValue, documentDefaultNamespace, Options.Culture);
                _xmlNamespaceManager.RegisterNamespace(Options.Namespace.Uri, Options.Namespace.Prefix);
            }
        }

        #endregion
    }
}
