// Copyright (C) Sina Iravanian, Julian Verdurmen, axuno gGmbH and other contributors.
// Licensed under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        ///     Deserializes the specified string containing the XML serialization and returns an object.
        /// </summary>
        /// <param name="input">The input string containing the XML serialization.</param>
        /// <returns>The deserialized object.</returns>
        public object Deserialize(string input)
        {
            try
            {
                using TextReader tr = new StringReader(input);
                var xDocument = XDocument.Load(tr, GetXmlLoadOptions());
                var baseElement = xDocument.Root;
                FindDocumentDefaultNamespace();
                return DeserializeBase(baseElement);
            }
            catch (XmlException ex)
            {
                OnExceptionOccurred(new YAXBadlyFormedXML(ex, ex.LineNumber, ex.LinePosition), Options.ExceptionBehavior);
                return null;
            }
        }

        /// <summary>
        ///     Deserializes an object while reading input from an instance of <c>XmlReader</c>.
        /// </summary>
        /// <param name="xmlReader">The <c>XmlReader</c> instance to read input from.</param>
        /// <returns>The deserialized object.</returns>
        public object Deserialize(XmlReader xmlReader)
        {
            try
            {
                var xDocument = XDocument.Load(xmlReader, GetXmlLoadOptions());
                var baseElement = xDocument.Root;
                FindDocumentDefaultNamespace();
                return DeserializeBase(baseElement);
            }
            catch (XmlException ex)
            {
                OnExceptionOccurred(new YAXBadlyFormedXML(ex, ex.LineNumber, ex.LinePosition), Options.ExceptionBehavior);
                return null;
            }
        }

        /// <summary>
        ///     Deserializes an object while reading input from an instance of <c>TextReader</c>.
        /// </summary>
        /// <param name="textReader">The <c>TextReader</c> instance to read input from.</param>
        /// <returns>The deserialized object.</returns>
        public object Deserialize(TextReader textReader)
        {
            try
            {
                var xDocument = XDocument.Load(textReader, GetXmlLoadOptions());
                var baseElement = xDocument.Root;
                FindDocumentDefaultNamespace();
                return DeserializeBase(baseElement);
            }
            catch (XmlException ex)
            {
                OnExceptionOccurred(new YAXBadlyFormedXML(ex, ex.LineNumber, ex.LinePosition), Options.ExceptionBehavior);
                return null;
            }
        }

        /// <summary>
        ///     Deserializes an object while reading from an instance of <c>XElement</c>
        /// </summary>
        /// <param name="element">The <c>XElement</c> instance to read from.</param>
        /// <returns>The deserialized object</returns>
        public object Deserialize(XElement element)
        {
            try
            {
                var xDocument = new XDocument();
                xDocument.Add(element);
                FindDocumentDefaultNamespace();
                return DeserializeBase(element);
            }
            catch (XmlException ex)
            {
                OnExceptionOccurred(new YAXBadlyFormedXML(ex, ex.LineNumber, ex.LinePosition), Options.ExceptionBehavior);
                return null;
            }
        }

        /// <summary>
        ///     Deserializes an object from the specified file which contains the XML serialization of the object.
        /// </summary>
        /// <param name="fileName">Path to the file.</param>
        /// <returns>The deserialized object.</returns>
        public object DeserializeFromFile(string fileName)
        {
            try
            {
                return Deserialize(File.ReadAllText(fileName));
            }
            catch (XmlException ex)
            {
                OnExceptionOccurred(new YAXBadlyFormedXML(ex, ex.LineNumber, ex.LinePosition), Options.ExceptionBehavior);
                return null;
            }
        }

        /// <summary>
        ///     Sets the object used as the base object in the next stage of de-serialization.
        ///     This method enables multi-stage de-serialization for YAXLib.
        /// </summary>
        /// <param name="obj">The object used as the base object in the next stage of de-serialization.</param>
        public void SetDeserializationBaseObject(object obj)
        {
            if (obj != null && !_type.IsInstanceOfType(obj)) throw new YAXObjectTypeMismatch(_type, obj.GetType());

            _desObject = obj;
        }

        #endregion

        #region Private methods
       
        /// <summary>
        ///     The basic method which performs the whole job of de-serialization.
        /// </summary>
        /// <param name="baseElement">The element to be deserialized.</param>
        /// <returns>object containing the deserialized data</returns>
        private object DeserializeBase(XElement baseElement)
        {
            _isSerializing = false;

            if (baseElement == null) return _desObject;

            ProcessRealTypeAttribute(baseElement);

            // HasCustomSerializer must be tested after analyzing any RealType attribute 
            if (_udtWrapper.HasCustomSerializer)
                return InvokeCustomDeserializerFromElement(_udtWrapper.CustomSerializerType, baseElement, null, _udtWrapper, this);

            // Deserialize objects with special treatment

            if (_type.IsGenericType && _type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                return DeserializeKeyValuePair(baseElement);

            if (KnownTypes.IsKnowType(_type)) return KnownTypes.Deserialize(baseElement, _type, TypeNamespace);

            if (TryDeserializeAsDictionary(baseElement, out var resultObject)) 
                return resultObject;

            if (TryDeserializeAsCollection(baseElement, out resultObject)) 
                return resultObject;

            if (ReflectionUtils.IsBasicType(_type)) return ReflectionUtils.ConvertBasicType(baseElement.Value, _type, Options.Culture);

            // Run the default deserialization algorithm
            return DeserializeDefault(baseElement);
        }

        /// <summary>
        /// The default serialization algorithm, which deserializes
        /// from value from attribute, from value and from XML element,
        /// and from custom serializers.
        /// </summary>
        /// <param name="baseElement"></param>
        /// <returns>The deserialized object</returns>
        private object DeserializeDefault(XElement baseElement)
        {
            var resultObject = _desObject ?? Activator.CreateInstance(_type, Array.Empty<object>());

            foreach (var member in GetFieldsToBeSerialized())
            {
                if (!IsAnythingToDeserialize(member)) continue;

                // reset handled exceptions status
                _exceptionOccurredDuringMemberDeserialization = false;

                var deserializedValue = string.Empty; // the element value gathered at the first phase
                XElement xElementValue = null; // the XElement instance gathered at the first phase
                XAttribute xAttributeValue = null; // the XAttribute instance gathered at the first phase

                var isHelperElementCreated = false;

                var serializationLocation = member.SerializationLocation;

                if (member.IsSerializedAsAttribute)
                {
                    deserializedValue = DeserializeFromAttribute(baseElement, ref xElementValue, ref xAttributeValue, serializationLocation, member);
                }
                else if (member.IsSerializedAsValue)
                {
                    deserializedValue = DeserializeFromValue(baseElement, ref xElementValue, serializationLocation, member);
                }
                else
                {
                    if (DeserializeFromXmlElement(baseElement, serializationLocation, member, resultObject,
                            ref deserializedValue, ref isHelperElementCreated, ref xElementValue)) 
                        continue;
                }

                // Phase 2: Now try to retrieve deserializedValue,
                // based on values gathered in xElementValue, xAttributeValue, and deserializedValue
                if (_exceptionOccurredDuringMemberDeserialization)
                {
                    _ = TrySetDefaultValue(baseElement, resultObject, xAttributeValue, xElementValue, member);
                }
                else if (member.HasCustomSerializer || member.MemberTypeWrapper.HasCustomSerializer)
                {
                    InvokeCustomDeserializer(baseElement, deserializedValue, xElementValue, xAttributeValue,
                        resultObject, member);
                }
                else if (deserializedValue != null)
                {
                    RetrieveElementValue(resultObject, member, deserializedValue, xElementValue);
                }

                RemoveRedundantElements(isHelperElementCreated, xElementValue, xAttributeValue);
            }

            return resultObject;
        }

        private void RemoveRedundantElements(bool isHelperElementCreated, XElement xElementValue,
            XAttribute xAttributeValue)
        {
            // remove the helper element
            if (isHelperElementCreated)
                xElementValue?.Remove();

            if (RemoveDeserializedXmlNodes)
            {
                xAttributeValue?.Remove();
                xElementValue?.Remove();
            }
        }

        private void InvokeCustomDeserializer(XElement baseElement, string deserializedValue, XElement xElementValue,
            XAttribute xAttributeValue, object resultObject, MemberWrapper member)
        {
            var customSerializerType = member.HasCustomSerializer
                ? member.CustomSerializerType
                : member.MemberTypeWrapper.CustomSerializerType;

            object desObj;
            if (member.IsSerializedAsAttribute)
                desObj = InvokeCustomDeserializerFromAttribute(customSerializerType, xAttributeValue, member, _udtWrapper,
                    this);
            else if (member.IsSerializedAsElement)
                desObj = InvokeCustomDeserializerFromElement(customSerializerType, xElementValue,
                    member.HasCustomSerializer ? member : null,
                    member.MemberTypeWrapper.HasCustomSerializer ? member.MemberTypeWrapper : null,
                    this);
            else if (member.IsSerializedAsValue)
                desObj = InvokeCustomDeserializerFromValue(customSerializerType, deserializedValue, member, _udtWrapper, this);
            else
                throw new Exception("unknown situation");

            try
            {
                member.SetValue(resultObject, desObj);
            }
            catch
            {
                OnExceptionOccurred(
                    new YAXPropertyCannotBeAssignedTo(member.Alias.LocalName,
                        xAttributeValue ?? xElementValue ?? baseElement as IXmlLineInfo), Options.ExceptionBehavior);
            }
        }

        private bool TrySetDefaultValue(XElement baseElement, object resultObject, XAttribute xAttributeValue,
            XElement xElementValue, MemberWrapper member)
        {
            // i.e. if it was NOT resuming deserialization,
            if (_desObject != null) 
                return false;
            
            // set default value, otherwise existing value for the member is kept

            if (!member.MemberType.IsValueType && _udtWrapper.IsNotAllowedNullObjectSerialization)
            {
                try
                {
                    member.SetValue(resultObject, null);
                }
                catch
                {
                    OnExceptionOccurred(
                        new YAXDefaultValueCannotBeAssigned(member.Alias.LocalName, member.DefaultValue,
                            xAttributeValue ?? xElementValue ?? baseElement as IXmlLineInfo, Options.Culture),
                        Options.ExceptionBehavior);
                    return false;
                }
                return true;
            }
            
            if (member.DefaultValue != null)
            {
                try
                {
                    member.SetValue(resultObject, member.DefaultValue);
                }
                catch
                {
                    OnExceptionOccurred(
                        new YAXDefaultValueCannotBeAssigned(member.Alias.LocalName, member.DefaultValue,
                            xAttributeValue ?? xElementValue ?? baseElement as IXmlLineInfo, Options.Culture),
                        Options.ExceptionBehavior);
                    return false;
                }

                return true;
            }

            if (!member.MemberType.IsValueType)
            {
                member.SetValue(resultObject, null);
                return true;
            }

            return false;
        }

        private bool DeserializeFromXmlElement(XElement baseElement, string serializationLocation, MemberWrapper member, object resultObject,
            ref string deserializedValue, ref bool isHelperElementCreated, ref XElement xElementValue)
        {
            // member is serialized as an xml element

            var canContinue = false;
            var elem = XMLUtils.FindElement(baseElement, serializationLocation,
                member.Alias.OverrideNsIfEmpty(TypeNamespace));

            if (elem != null)
            {
                deserializedValue = elem.Value;
                xElementValue = elem;
                return false;
            }

            // no such element was found yet

            if ((member.IsTreatedAsCollection || member.IsTreatedAsDictionary) &&
                member.CollectionAttributeInstance is
                    { SerializationType: YAXCollectionSerializationTypes.RecursiveWithNoContainingElement })
            {
                if (AtLeastOneOfCollectionMembersExists(baseElement, member))
                {
                    elem = baseElement;
                    canContinue = true;
                }
                else
                {
                    member.SetValue(resultObject, member.DefaultValue);
                    return true;
                }
            }
            else if (!ReflectionUtils.IsBasicType(member.MemberType) && !member.IsTreatedAsCollection &&
                     !member.IsTreatedAsDictionary)
            {
                // try to fix this problem by creating a helper element, maybe all its children are placed somewhere else
                var helperElement = XMLUtils.CreateElement(baseElement, serializationLocation,
                    member.Alias.OverrideNsIfEmpty(TypeNamespace));
                if (helperElement != null)
                {
                    isHelperElementCreated = true;
                    if (AtLeastOneOfMembersExists(helperElement, member.MemberType))
                    {
                        canContinue = true;
                        elem = helperElement;
                        deserializedValue = elem.Value;
                    }
                }
            }
            else if (_udtWrapper.IsNotAllowedNullObjectSerialization && member.DefaultValue is null)
            {
                // Any missing elements are allowed for deserialization:
                // * Don't set a value - uses default or initial value
                // * Ignore member.TreatErrorsAs
                // * Don't register YAXElementMissingException
                // * Skip Phase 2
                return true;
            }

            if (!canContinue)
                OnExceptionOccurred(new YAXElementMissingException(
                        StringUtils.CombineLocationAndElementName(serializationLocation,
                            member.Alias.OverrideNsIfEmpty(TypeNamespace)), baseElement),
                    !member.MemberType.IsValueType && _udtWrapper.IsNotAllowedNullObjectSerialization
                        ? YAXExceptionTypes.Ignore
                        : member.TreatErrorsAs);

            xElementValue = elem;
            return false;
        }

        private string DeserializeFromValue(XElement baseElement, ref XElement xElementValue, string serializationLocation,
            MemberWrapper member)
        {
            var deserializedValue = string.Empty;
            var elem = XMLUtils.FindLocation(baseElement, serializationLocation);
            if (elem == null) // no such element is was found
            {
                OnExceptionOccurred(new YAXElementMissingException(
                        serializationLocation, baseElement),
                    !member.MemberType.IsValueType && _udtWrapper.IsNotAllowedNullObjectSerialization
                        ? YAXExceptionTypes.Ignore
                        : member.TreatErrorsAs);
            }
            else
            {
                var values = elem.Nodes().OfType<XText>().ToArray();
                if (values.Length <= 0)
                {
                    // look for an element with the same name AND a yaxlib:realtype attribute
                    var innerElement = XMLUtils.FindElement(baseElement, serializationLocation,
                        member.Alias.OverrideNsIfEmpty(TypeNamespace));
                    if (innerElement != null &&
                        innerElement.Attribute_NamespaceSafe(Options.Namespace.Uri + Options.AttributeName.RealType,
                            _documentDefaultNamespace) != null)
                    {
                        deserializedValue = innerElement.Value;
                        xElementValue = innerElement;
                    }
                    else
                    {
                        OnExceptionOccurred(
                            new YAXElementValueMissingException(serializationLocation,
                                innerElement ?? baseElement),
                            !member.MemberType.IsValueType && _udtWrapper.IsNotAllowedNullObjectSerialization
                                ? YAXExceptionTypes.Ignore
                                : member.TreatErrorsAs);
                    }
                }
                else
                {
                    deserializedValue = values[0].Value;
                    values[0].Remove();
                }
            }

            return deserializedValue;
        }

        private string DeserializeFromAttribute(XElement baseElement, ref XElement xElementValue,
            ref XAttribute xAttributeValue, string serializationLocation, MemberWrapper member)
        {
            var deserializedValue = string.Empty;

            // find the parent element from its location
            var attr = XMLUtils.FindAttribute(baseElement, serializationLocation,
                member.Alias.OverrideNsIfEmpty(TypeNamespace));
            if (attr == null) // if the parent element does not exist
            {
                // look for an element with the same name AND a yaxlib:realtype attribute
                var elem = XMLUtils.FindElement(baseElement, serializationLocation,
                    member.Alias.OverrideNsIfEmpty(TypeNamespace));
                if (elem != null && elem.Attribute_NamespaceSafe(Options.Namespace.Uri + Options.AttributeName.RealType,
                        _documentDefaultNamespace) != null)
                {
                    deserializedValue = elem.Value;
                    xElementValue = elem;
                }
                else
                {
                    OnExceptionOccurred(new YAXAttributeMissingException(
                            StringUtils.CombineLocationAndElementName(serializationLocation, member.Alias),
                            elem ?? baseElement),
                        !member.MemberType.IsValueType && _udtWrapper.IsNotAllowedNullObjectSerialization
                            ? YAXExceptionTypes.Ignore
                            : member.TreatErrorsAs);
                }
            }
            else
            {
                deserializedValue = attr.Value;
                xAttributeValue = attr;
            }

            return deserializedValue;
        }

        private static bool IsAnythingToDeserialize(MemberWrapper member)
        {
            if (!member.CanWrite)
                return false;

            if (member.IsAttributedAsDontSerialize)
                return false;
            
            return true;
        }

#nullable enable
        private bool TryDeserializeAsCollection(XElement baseElement, out object? resultObject)
        {
            resultObject = null;
            if (!_udtWrapper.IsTreatedAsCollection || IsCreatedToDeserializeANonCollectionMember) return false;

            resultObject = DeserializeCollectionValue(_type, baseElement, _udtWrapper.Alias,
                _udtWrapper.CollectionAttributeInstance);

            return true;
        }

        private bool TryDeserializeAsDictionary(XElement baseElement, out object? resultObject)
        {
            resultObject = null;
            if (!_udtWrapper.IsTreatedAsDictionary || IsCreatedToDeserializeANonCollectionMember) return false;
            if (_udtWrapper.DictionaryAttributeInstance == null) return false;

            resultObject = DeserializeTaggedDictionaryValue(baseElement, _udtWrapper.Alias, _type,
                _udtWrapper.CollectionAttributeInstance, _udtWrapper.DictionaryAttributeInstance);
            return true;
        }
#nullable disable

        private void ProcessRealTypeAttribute(XElement baseElement)
        {
            var realTypeAttr = baseElement.Attribute_NamespaceSafe(Options.Namespace.Uri + Options.AttributeName.RealType,
                _documentDefaultNamespace);
            
            if (realTypeAttr == null) return;

            var theRealType = ReflectionUtils.GetTypeByName(realTypeAttr.Value);
            if (theRealType == null) return;

            _type = theRealType;
            _udtWrapper = TypeWrappersPool.Pool.GetTypeWrapper(_type, this);
        }

        /// <summary>
        ///     Checks whether at least one of the collection members of
        ///     the specified collection exists.
        /// </summary>
        /// <param name="elem">The XML element to check its content.</param>
        /// <param name="member">
        ///     The class-member corresponding to the collection for
        ///     which we intend to check existence of its members.
        /// </param>
        /// <returns></returns>
        private bool AtLeastOneOfCollectionMembersExists(XElement elem, MemberWrapper member)
        {
            if (!((member.IsTreatedAsCollection || member.IsTreatedAsDictionary) &&
                  member.CollectionAttributeInstance != null &&
                  member.CollectionAttributeInstance.SerializationType ==
                  YAXCollectionSerializationTypes.RecursiveWithNoContainingElement))
                throw new ArgumentException("member should be a collection serialized without containing element");

            XName eachElementName = null;

            if (member.CollectionAttributeInstance != null)
                eachElementName = StringUtils.RefineSingleElement(member.CollectionAttributeInstance.EachElementName);

            if (member.DictionaryAttributeInstance != null && member.DictionaryAttributeInstance.EachPairName != null)
                eachElementName = StringUtils.RefineSingleElement(member.DictionaryAttributeInstance.EachPairName);

            if (eachElementName == null)
            {
                var colItemType = ReflectionUtils.GetCollectionItemType(member.MemberType);
                eachElementName = StringUtils.RefineSingleElement(ReflectionUtils.GetTypeFriendlyName(colItemType));
            }

            // return if such an element exists
            return elem.Element(
                       eachElementName.OverrideNsIfEmpty(member.Namespace.IfEmptyThen(TypeNamespace)
                           .IfEmptyThenNone())) !=
                   null;
        }

        /// <summary>
        ///     Checks whether at least one of the members (property or field) of
        ///     the specified object exists.
        /// </summary>
        /// <param name="elem">The XML element to check its content.</param>
        /// <param name="type">
        ///     The class-member corresponding to the object for
        ///     which we intend to check existence of its members.
        /// </param>
        /// <returns></returns>
        private bool AtLeastOneOfMembersExists(XElement elem, Type type)
        {
            if (elem == null)
                throw new ArgumentNullException(nameof(elem));

            var typeWrapper = TypeWrappersPool.Pool.GetTypeWrapper(type, this);

            foreach (var member in GetFieldsToBeSerialized(typeWrapper))
            {
                if (!IsAnythingToDeserialize(member)) continue;

                if (CanProcessAttribute(elem, member)) return true;

                // No attribute, so there should be an element

                if (XMLUtils.FindElement(elem, member.SerializationLocation, member.Alias) != null)
                    return true;

                if (ReflectionUtils.IsBasicType(member.MemberType) || member.IsTreatedAsCollection ||
                    member.IsTreatedAsDictionary || member.MemberType == _type) continue;

                // try to create a helper element 
                var helperElement = XMLUtils.CreateElement(elem, member.SerializationLocation, member.Alias);
                if (helperElement == null) continue;

                var memberExists = AtLeastOneOfMembersExists(helperElement, member.MemberType);
                helperElement.Remove();
                return memberExists;
            }

            return false;
        }

        private bool CanProcessAttribute(XElement xElement, MemberWrapper member)
        {
            if (!member.IsSerializedAsAttribute) return false;

            // find the parent element from its location
            var attr = XMLUtils.FindAttribute(xElement, member.SerializationLocation, member.Alias);
            if (attr != null)
                return true;

            // maybe it has got a realtype attribute and hence have turned into an element
            var elem = XMLUtils.FindElement(xElement, member.SerializationLocation, member.Alias);
            return elem?.Attribute_NamespaceSafe(Options.Namespace.Uri + Options.AttributeName.RealType,
                _documentDefaultNamespace) != null;
        }

        /// <summary>
        ///     Retrieves the value of the element from the specified XML element or attribute.
        /// </summary>
        /// <param name="o">The object to store the retrieved value at.</param>
        /// <param name="member">The member of the specified object whose value we intent to retrieve.</param>
        /// <param name="elemValue">The value of the element stored as string.</param>
        /// <param name="xelemValue">
        ///     The XML element value to be retrieved. If the value to be retrieved
        ///     has been stored in an XML attribute, this reference is <c>null</c>.
        /// </param>
        private void RetrieveElementValue(object o, MemberWrapper member, string elemValue, XElement xelemValue)
        {
            var memberType = member.MemberType;

            // when serializing collection with no containing element, then the real type attribute applies to the class
            // containing the collection, not the collection itself. That's because the containing element of collection is not 
            // serialized. In this case the flag `isRealTypeAttributeNotRelevant` is set to true.
            var isRealTypeAttributeNotRelevant = member.CollectionAttributeInstance != null
                                                 && member.CollectionAttributeInstance.SerializationType ==
                                                 YAXCollectionSerializationTypes.RecursiveWithNoContainingElement;

            // try to retrieve the real-type if specified
            if (xelemValue != null && !isRealTypeAttributeNotRelevant)
            {
                var realTypeAttribute = xelemValue.Attribute_NamespaceSafe(Options.Namespace.Uri + Options.AttributeName.RealType,
                    _documentDefaultNamespace);
                if (realTypeAttribute != null)
                {
                    var realType = ReflectionUtils.GetTypeByName(realTypeAttribute.Value);
                    if (realType != null) memberType = realType;
                }
            }

            if (xelemValue != null && XMLUtils.IsElementCompletelyEmpty(xelemValue) &&
                !ReflectionUtils.IsBasicType(memberType) && !member.IsTreatedAsCollection &&
                !member.IsTreatedAsDictionary &&
                !AtLeastOneOfMembersExists(xelemValue, memberType))
            {
                try
                {
                    member.SetValue(o, member.DefaultValue);
                }
                catch
                {
                    OnExceptionOccurred(
                        new YAXDefaultValueCannotBeAssigned(member.Alias.LocalName, member.DefaultValue, xelemValue, Options.Culture),
                        member.TreatErrorsAs);
                }
            }
            else if (memberType == typeof(string))
            {
                if (string.IsNullOrEmpty(elemValue) && xelemValue != null)
                    elemValue = xelemValue.IsEmpty ? null : string.Empty;

                try
                {
                    member.SetValue(o, elemValue);
                }
                catch
                {
                    OnExceptionOccurred(new YAXPropertyCannotBeAssignedTo(member.Alias.LocalName, xelemValue),
                        Options.ExceptionBehavior);
                }
            }
            else if (ReflectionUtils.IsBasicType(memberType))
            {
                object convertedObj;

                try
                {
                    if (ReflectionUtils.IsNullable(memberType) && string.IsNullOrEmpty(elemValue))
                        convertedObj = member.DefaultValue;
                    else
                        convertedObj = ReflectionUtils.ConvertBasicType(elemValue, memberType, Options.Culture);

                    try
                    {
                        member.SetValue(o, convertedObj);
                    }
                    catch
                    {
                        OnExceptionOccurred(new YAXPropertyCannotBeAssignedTo(member.Alias.LocalName, xelemValue),
                            Options.ExceptionBehavior);
                    }
                }
                catch (Exception ex)
                {
                    if (ex is YAXException) throw;

                    OnExceptionOccurred(new YAXBadlyFormedInput(member.Alias.LocalName, elemValue, xelemValue),
                        member.TreatErrorsAs);

                    try
                    {
                        member.SetValue(o, member.DefaultValue);
                    }
                    catch
                    {
                        OnExceptionOccurred(
                            new YAXDefaultValueCannotBeAssigned(member.Alias.LocalName, member.DefaultValue,
                                xelemValue, Options.Culture), Options.ExceptionBehavior);
                    }
                }
            }
            else if (member.IsTreatedAsDictionary && member.DictionaryAttributeInstance != null)
            {
                DeserializeTaggedDictionaryMember(o, member, xelemValue);
            }
            else if (member.IsTreatedAsCollection)
            {
                DeserializeCollectionMember(o, member, memberType, elemValue, xelemValue);
            }
            else
            {
                var namespaceToOverride = member.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone();
                var ser = NewInternalSerializer(memberType, namespaceToOverride, null);

                ser.IsCreatedToDeserializeANonCollectionMember =
                    !(member.IsTreatedAsDictionary || member.IsTreatedAsCollection);

                if (_desObject != null) // i.e. it is in resuming mode
                    ser.SetDeserializationBaseObject(member.GetValue(o));

                var convertedObj = ser.DeserializeBase(xelemValue);
                FinalizeNewSerializer(ser, false);

                try
                {
                    member.SetValue(o, convertedObj);
                }
                catch
                {
                    OnExceptionOccurred(new YAXPropertyCannotBeAssignedTo(member.Alias.LocalName, xelemValue),
                        Options.ExceptionBehavior);
                }
            }
        }

        /// <summary>
        ///     Retrieves the collection value.
        /// </summary>
        /// <param name="collType">Type of the collection to be retrieved.</param>
        /// <param name="xElement">The xml element.</param>
        /// <param name="memberAlias">The member's alias, used only in exception titles.</param>
        /// <param name="collAttrInstance">The collection attribute instance.</param>
        /// <returns></returns>
        private object DeserializeCollectionValue(Type collType, XElement xElement, XName memberAlias,
            YAXCollectionAttribute collAttrInstance)
        {
            _ = TryGetContainerObject(xElement, collType, memberAlias, out object? containerObj);

            var dataItems = new List<object>(); // this will hold the actual data items
            var collItemType = ReflectionUtils.GetCollectionItemType(collType);
            var isPrimitive = ReflectionUtils.IsBasicType(collItemType);

            if (isPrimitive && collAttrInstance is
                    { SerializationType: YAXCollectionSerializationTypes.Serially })
            {
                // The collection was serialized serially
                GetSerialCollectionItems(xElement, memberAlias, collAttrInstance, collItemType, dataItems);
            }
            else 
            {
                // The collection was serialized recursively
                GetRecursiveCollectionItems(xElement, memberAlias, collAttrInstance, collItemType, isPrimitive, dataItems);
            } 

            // Now dataItems list is filled and will be processed

            if (TryGetCollectionAsArray(xElement, collType, collItemType, memberAlias, dataItems, out var array)) return array;

            if (TryGetCollectionAsDictionary(xElement, collType, collItemType, memberAlias, containerObj, dataItems, out var o)) return o;

            if (TryGetAsNonGenericDictionary(xElement, collType, memberAlias, containerObj, dataItems, out var nonGenericDictionary)) return nonGenericDictionary;

            if (TryGetAsBitArray(collType, dataItems, out var bitArray)) return bitArray;

            if (TryGetAsStack(xElement, collType, memberAlias, containerObj, dataItems, out var stack)) return stack;

            if (TryGetAsEnumerable(xElement, collType, memberAlias, containerObj, dataItems, out var enumerable)) return enumerable;

            return null;
        }

#nullable enable
        private bool TryGetAsEnumerable(XElement xElement, Type collType, XName memberAlias, object? containerObj,
            List<object> dataItems, out object? enumerable)
        {
            enumerable = null;

            if (!ReflectionUtils.IsIEnumerable(collType)) return false;

            if (containerObj == null)
            {
                enumerable = dataItems;
                return true;
            }

            enumerable = containerObj;

            var additionMethodName = "Add";

            if (ReflectionUtils.IsTypeEqualOrInheritedFromType(collType, typeof(Queue)) ||
                ReflectionUtils.IsTypeEqualOrInheritedFromType(collType, typeof(Queue<>)))
                additionMethodName = "Enqueue";
            else if (ReflectionUtils.IsTypeEqualOrInheritedFromType(collType, typeof(LinkedList<>)))
                additionMethodName = "AddLast";

            foreach (var dataItem in dataItems)
                try
                {
                    collType.InvokeMethod(additionMethodName, enumerable, new[] { dataItem });
                }
                catch
                {
                    OnExceptionOccurred(
                        new YAXCannotAddObjectToCollection(memberAlias.ToString(), dataItem, xElement),
                        Options.ExceptionBehavior);
                }

            return true;
        }

        private bool TryGetAsStack(XElement xElement, Type collType, XName memberAlias, object? containerObj, List<object> dataItems,
            out object? stack)
        {
            stack = null;

            if (!ReflectionUtils.IsTypeEqualOrInheritedFromType(collType, typeof(Stack)) &&
                !ReflectionUtils.IsTypeEqualOrInheritedFromType(collType, typeof(Stack<>))) return false;

            var st = containerObj;

            const string additionMethodName = "Push";

            for (var i = dataItems.Count - 1; i >= 0; i--) // the loop must be from end to beginning
                try
                {
                    collType.InvokeMethod(additionMethodName, st, new[] { dataItems[i] });
                }
                catch
                {
                    OnExceptionOccurred(
                        new YAXCannotAddObjectToCollection(memberAlias.ToString(), dataItems[i], xElement),
                        Options.ExceptionBehavior);
                }

            stack = st;
            return true;
        }

        private static bool TryGetAsBitArray(Type collType, List<object> dataItems, out object? bitArray)
        {
            bitArray = null;

            if (!ReflectionUtils.IsTypeEqualOrInheritedFromType(collType, typeof(BitArray))) return false;

            var ba = new bool[dataItems.Count];
            for (var i = 0; i < ba.Length; i++)
                try
                {
                    ba[i] = (bool)dataItems[i];
                }
                catch
                {
                    // Nothing to do, if cast fails
                }

            bitArray = Activator.CreateInstance(collType, ba);

            return true;
        }

        private bool TryGetAsNonGenericDictionary(XElement xElement, Type collType, XName memberAlias, object? containerObj,
            List<object> dataItems, out object? nonGenericDictionary)
        {
            nonGenericDictionary = containerObj;

            if (!ReflectionUtils.IsNonGenericIDictionary(collType)) return false;

            foreach (var lstItem in dataItems)
            {
                var key = lstItem.GetType().GetProperty("Key", BindingFlags.Instance | BindingFlags.Public)
                    .GetValue(lstItem, null);
                var value = lstItem.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)
                    .GetValue(lstItem, null);

                try
                {
                    collType.InvokeMethod("Add", nonGenericDictionary, new[] { key, value });
                }
                catch
                {
                    OnExceptionOccurred(
                        new YAXCannotAddObjectToCollection(memberAlias.ToString(), lstItem, xElement),
                        Options.ExceptionBehavior);
                }
            }

            return true;
        }

        private bool TryGetCollectionAsDictionary(XElement xElement, Type collType, Type collItemType, XName memberAlias,
            object? containerObj, List<object> dataItems, out object? dictionary)
        {
            dictionary = null;

            if (!ReflectionUtils.IsIDictionary(collType, out _, out _)) return false;

            // The collection is a Dictionary
            var dict = containerObj;

            foreach (var dataItem in dataItems)
            {
                var key = collItemType.GetProperty("Key").GetValue(dataItem, null);
                var value = collItemType.GetProperty("Value").GetValue(dataItem, null);
                try
                {
                    collType.InvokeMethod("Add", dict, new[] { key, value });
                    //colType.InvokeMember("Add", BindingFlags.InvokeMethod, null, dic, new[] { key, value });
                }
                catch
                {
                    OnExceptionOccurred(
                        new YAXCannotAddObjectToCollection(memberAlias.ToString(), dataItem, xElement),
                        Options.ExceptionBehavior);
                }
            }

            dictionary = dict;
            return true;
        }

        private bool TryGetCollectionAsArray(XElement xElement, Type collType, Type collItemType, XName memberAlias,
            List<object> dataItems, out object? array)
        {
            array = null;

            if (!ReflectionUtils.IsArray(collType)) return false;

            var dimsAttr = xElement.Attribute_NamespaceSafe(Options.Namespace.Uri + Options.AttributeName.Dimensions,
                _documentDefaultNamespace);
            var dims = Array.Empty<int>();
            if (dimsAttr != null) dims = StringUtils.ParseArrayDimsString(dimsAttr.Value);

            Array arrayInstance;
            if (dims.Length > 0)
            {
                var lowerBounds = new int[dims.Length]; // an array of zeros
                arrayInstance = Array.CreateInstance(collItemType, dims, lowerBounds); // create the array

                var count = Math.Min(arrayInstance.Length, dataItems.Count);
                // now fill the array
                for (var i = 0; i < count; i++)
                {
                    var dimIndex = GetArrayDimensionalIndex(i, dims);
                    try
                    {
                        arrayInstance.SetValue(dataItems[i], dimIndex);
                    }
                    catch
                    {
                        OnExceptionOccurred(
                            new YAXCannotAddObjectToCollection(memberAlias.ToString(), dataItems[i], xElement),
                            Options.ExceptionBehavior);
                    }
                }
            }
            else
            {
                arrayInstance = Array.CreateInstance(collItemType, dataItems.Count); // create the array

                var count = Math.Min(arrayInstance.Length, dataItems.Count);
                // now fill the array
                for (var i = 0; i < count; i++)
                    try
                    {
                        arrayInstance.SetValue(dataItems[i], i);
                    }
                    catch
                    {
                        OnExceptionOccurred(
                            new YAXCannotAddObjectToCollection(memberAlias.ToString(), dataItems[i], xElement),
                            Options.ExceptionBehavior);
                    }
            }

            array = arrayInstance;
            return true;
        }

#nullable disable

        /// <summary>
        /// Gets the data items for a collection that was serialized recursively.
        /// </summary>
        /// <param name="xElement"></param>
        /// <param name="memberAlias"></param>
        /// <param name="collAttrInstance"></param>
        /// <param name="collItemType"></param>
        /// <param name="isPrimitive"></param>
        /// <param name="dataItems">The list that will be filled.</param>
        private void GetRecursiveCollectionItems(XElement xElement, XName memberAlias, YAXCollectionAttribute collAttrInstance,
            Type collItemType, bool isPrimitive, List<object> dataItems)
        {
            XName eachElemName = null;
            if (collAttrInstance is { EachElementName: { } })
            {
                eachElemName = StringUtils.RefineSingleElement(collAttrInstance.EachElementName);
                eachElemName =
                    eachElemName.OverrideNsIfEmpty(memberAlias.Namespace.IfEmptyThen(TypeNamespace)
                        .IfEmptyThenNone());
            }

            var elemsToSearch = eachElemName == null ? xElement.Elements() : xElement.Elements(eachElemName);

            foreach (var childElem in elemsToSearch)
            {
                var curElementType = collItemType;
                var curElementIsPrimitive = isPrimitive;

                var realTypeAttribute = childElem.Attribute_NamespaceSafe(
                    Options.Namespace.Uri + Options.AttributeName.RealType,
                    _documentDefaultNamespace);
                if (realTypeAttribute != null)
                {
                    var theRealType = ReflectionUtils.GetTypeByName(realTypeAttribute.Value);
                    if (theRealType != null)
                    {
                        curElementType = theRealType;
                        curElementIsPrimitive = ReflectionUtils.IsBasicType(curElementType);
                    }
                }

                // Check if curElementType is derived or is the same is itemType.
                // For speed concerns we perform this check only when eachElemName is null
                if (eachElemName == null && (curElementType == typeof(object) ||
                                             !ReflectionUtils.IsTypeEqualOrInheritedFromType(curElementType,
                                                 collItemType)))
                    continue;

                if (curElementIsPrimitive)
                {
                    try
                    {
                        dataItems.Add(ReflectionUtils.ConvertBasicType(childElem.Value, curElementType, Options.Culture));
                    }
                    catch
                    {
                        OnExceptionOccurred(
                            new YAXBadlyFormedInput(childElem.Name.ToString(), childElem.Value, childElem),
                            Options.ExceptionBehavior);
                    }
                }
                else
                {
                    var namespaceToOverride = memberAlias.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone();
                    var ser = NewInternalSerializer(curElementType, namespaceToOverride, null);
                    dataItems.Add(ser.DeserializeBase(childElem));
                    FinalizeNewSerializer(ser, false);
                }
            }
        }

        /// <summary>
        /// Gets the data items for a collection that was serialized serially.
        /// </summary>
        /// <param name="xElement"></param>
        /// <param name="memberAlias"></param>
        /// <param name="collAttrInstance"></param>
        /// <param name="collItemType"></param>
        /// <param name="dataItems">The list that will be filled.</param>
        private void GetSerialCollectionItems(XElement xElement, XName memberAlias,
            YAXCollectionAttribute collAttrInstance,
            Type collItemType,
            List<object> dataItems)
        {
            var separators = collAttrInstance.SeparateBy.ToCharArray();

            // Should we add white space characters to the separators?
            if (collAttrInstance.IsWhiteSpaceSeparator)
                separators = separators.Union(new[] { ' ', '\t', '\r', '\n' }).ToArray();

            var elemValue = xElement.Value;
            var items = elemValue.Split(separators, StringSplitOptions.RemoveEmptyEntries);

            foreach (var wordItem in items)
                try
                {
                    dataItems.Add(ReflectionUtils.ConvertBasicType(wordItem, collItemType, Options.Culture));
                }
                catch
                {
                    OnExceptionOccurred(new YAXBadlyFormedInput(memberAlias.ToString(), elemValue, xElement),
                        Options.ExceptionBehavior);
                }
        }

#nullable enable
        private bool TryGetContainerObject(XElement xElement, Type colType, XName memberAlias, out object? containerObj)
        {
            containerObj = null;

            // The collection type has an empty constructor
            if (!ReflectionUtils.IsInstantiableCollection(colType)) return false;

            var namespaceToOverride = memberAlias.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone();
            var containerSer = NewInternalSerializer(colType, namespaceToOverride, null);
            containerSer.IsCreatedToDeserializeANonCollectionMember = true;
            containerSer.RemoveDeserializedXmlNodes = true;

            containerObj = containerSer.DeserializeBase(xElement);
            FinalizeNewSerializer(containerSer, false);
            
            return true;
        }
#nullable disable

        /// <summary>
        ///     Deserializes the collection member.
        /// </summary>
        /// <param name="o">The object to store the retrieved value at.</param>
        /// <param name="member">The member of the specified object whose value we intent to retreive.</param>
        /// <param name="colType">Type of the collection to be retrieved.</param>
        /// <param name="elemValue">The value of the element stored as string.</param>
        /// <param name="xelemValue">
        ///     The XML element value to be retrieved. If the value to be retrieved
        ///     has been stored in an XML attribute, this reference is <c>null</c>.
        /// </param>
        private void DeserializeCollectionMember(object o, MemberWrapper member, Type colType, string elemValue,
            XElement xelemValue)
        {
            object colObject;

            if (member.CollectionAttributeInstance != null && member.CollectionAttributeInstance.SerializationType ==
                YAXCollectionSerializationTypes.Serially &&
                (member.IsSerializedAsAttribute || member.IsSerializedAsValue))
            {
                colObject = DeserializeCollectionValue(colType, new XElement("temp", elemValue), "temp",
                    member.CollectionAttributeInstance);
            }
            else
            {
                var memberAlias = member.Alias.OverrideNsIfEmpty(TypeNamespace);
                colObject = DeserializeCollectionValue(colType, xelemValue, memberAlias,
                    member.CollectionAttributeInstance);
            }

            try
            {
                member.SetValue(o, colObject);
            }
            catch
            {
                OnExceptionOccurred(new YAXPropertyCannotBeAssignedTo(member.Alias.LocalName, xelemValue),
                    Options.ExceptionBehavior);
            }
        }

        /// <summary>
        ///     Gets the dimensional index for an element of a multi-dimensional array from a
        ///     linear index specified.
        /// </summary>
        /// <param name="linearIndex">The linear index.</param>
        /// <param name="dimensions">The dimensions of the array.</param>
        /// <returns></returns>
        private static int[] GetArrayDimensionalIndex(long linearIndex, int[] dimensions)
        {
            var result = new int[dimensions.Length];

            var d = (int) linearIndex;

            for (var n = dimensions.Length - 1; n > 0; n--)
            {
                result[n] = d % dimensions[n];
                d = (d - result[n]) / dimensions[n];
            }

            result[0] = d;
            return result;
        }

        private object DeserializeTaggedDictionaryValue(XElement xelemValue, XName alias, Type type,
            YAXCollectionAttribute colAttributeInstance, YAXDictionaryAttribute dicAttrInstance)
        {
            // otherwise the "else if(member.IsTreatedAsCollection)" block solves the problem
            Type keyType, valueType;
            if (!ReflectionUtils.IsIDictionary(type, out keyType, out valueType))
                throw new Exception("elemValue must be a Dictionary");

            // deserialize non-collection fields
            var namespaceToOverride = alias.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone();
            var containerSer = NewInternalSerializer(type, namespaceToOverride, null);
            containerSer.IsCreatedToDeserializeANonCollectionMember = true;
            containerSer.RemoveDeserializedXmlNodes = true;
            var dic = containerSer.DeserializeBase(xelemValue);
            FinalizeNewSerializer(containerSer, false);

            // now try to deserialize collection fields
            Type pairType = null;
            ReflectionUtils.IsIEnumerable(type, out pairType);
            XName eachElementName = StringUtils.RefineSingleElement(ReflectionUtils.GetTypeFriendlyName(pairType));
            var isKeyAttrib = false;
            var isValueAttrib = false;
            var isKeyContent = false;
            var isValueContent = false;
            var keyAlias = alias.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone() + "Key";
            var valueAlias = alias.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone() + "Value";

            if (colAttributeInstance != null && colAttributeInstance.EachElementName != null)
            {
                eachElementName = StringUtils.RefineSingleElement(colAttributeInstance.EachElementName);
                eachElementName =
                    eachElementName.OverrideNsIfEmpty(alias.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone());
            }

            if (dicAttrInstance != null)
            {
                if (dicAttrInstance.EachPairName != null)
                {
                    eachElementName = StringUtils.RefineSingleElement(dicAttrInstance.EachPairName);
                    eachElementName =
                        eachElementName.OverrideNsIfEmpty(alias.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone());
                }

                if (dicAttrInstance.SerializeKeyAs == YAXNodeTypes.Attribute)
                    isKeyAttrib = ReflectionUtils.IsBasicType(keyType);
                else if (dicAttrInstance.SerializeKeyAs == YAXNodeTypes.Content)
                    isKeyContent = ReflectionUtils.IsBasicType(keyType);

                if (dicAttrInstance.SerializeValueAs == YAXNodeTypes.Attribute)
                    isValueAttrib = ReflectionUtils.IsBasicType(valueType);
                else if (dicAttrInstance.SerializeValueAs == YAXNodeTypes.Content)
                    isValueContent = ReflectionUtils.IsBasicType(valueType);

                if (dicAttrInstance.KeyName != null)
                {
                    keyAlias = StringUtils.RefineSingleElement(dicAttrInstance.KeyName);
                    keyAlias = keyAlias.OverrideNsIfEmpty(alias.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone());
                }

                if (dicAttrInstance.ValueName != null)
                {
                    valueAlias = StringUtils.RefineSingleElement(dicAttrInstance.ValueName);
                    valueAlias =
                        valueAlias.OverrideNsIfEmpty(alias.Namespace.IfEmptyThen(TypeNamespace).IfEmptyThenNone());
                }
            }

            foreach (var childElem in xelemValue.Elements(eachElementName))
            {
                object key = null, value = null;
                YAXSerializer keySer = null, valueSer = null;

                var isKeyFound = VerifyDictionaryPairElements(ref keyType, ref isKeyAttrib, ref isKeyContent, keyAlias,
                    childElem);
                var isValueFound = VerifyDictionaryPairElements(ref valueType, ref isValueAttrib, ref isValueContent,
                    valueAlias, childElem);

                if (!isKeyFound && !isValueFound)
                    continue;

                if (isKeyFound)
                {
                    if (isKeyAttrib)
                    {
                        key = ReflectionUtils.ConvertBasicType(
                            childElem.Attribute_NamespaceSafe(keyAlias, _documentDefaultNamespace).Value, keyType, Options.Culture);
                    }
                    else if (isKeyContent)
                    {
                        key = ReflectionUtils.ConvertBasicType(childElem.GetXmlContent(), keyType, Options.Culture);
                    }
                    else if (ReflectionUtils.IsBasicType(keyType))
                    {
                        key = ReflectionUtils.ConvertBasicType(childElem.Element(keyAlias).Value, keyType, Options.Culture);
                    }
                    else
                    {
                        if (keySer == null)
                            keySer = NewInternalSerializer(keyType, keyAlias.Namespace, null);

                        key = keySer.DeserializeBase(childElem.Element(keyAlias));
                        FinalizeNewSerializer(keySer, false);
                    }
                }

                if (isValueFound)
                {
                    if (isValueAttrib)
                    {
                        value = ReflectionUtils.ConvertBasicType(
                            childElem.Attribute_NamespaceSafe(valueAlias, _documentDefaultNamespace).Value, valueType, Options.Culture);
                    }
                    else if (isValueContent)
                    {
                        value = ReflectionUtils.ConvertBasicType(childElem.GetXmlContent(), valueType, Options.Culture);
                    }
                    else if (ReflectionUtils.IsBasicType(valueType))
                    {
                        value = ReflectionUtils.ConvertBasicType(childElem.Element(valueAlias).Value, valueType, Options.Culture);
                    }
                    else
                    {
                        if (valueSer == null)
                            valueSer = NewInternalSerializer(valueType, valueAlias.Namespace, null);

                        value = valueSer.DeserializeBase(childElem.Element(valueAlias));
                        FinalizeNewSerializer(valueSer, false);
                    }
                }

                try
                {
                    type.InvokeMethod("Add", dic, new[] {key, value});
                    //type.InvokeMember("Add", BindingFlags.InvokeMethod, null, dic, new object[] { key, value });
                }
                catch
                {
                    OnExceptionOccurred(
                        new YAXCannotAddObjectToCollection(alias.LocalName,
                            new KeyValuePair<object, object>(key, value), childElem),
                        Options.ExceptionBehavior);
                }
            }

            return dic;
        }

        /// <summary>
        ///     De-serializes a dictionary member which also benefits from a <see cref="YAXDictionaryAttribute"/>.
        /// </summary>
        /// <param name="o">The object to hold the deserialized value.</param>
        /// <param name="member">The member corresponding to the dictionary member.</param>
        /// <param name="xelemValue">The XML element value.</param>
        private void DeserializeTaggedDictionaryMember(object o, MemberWrapper member, XElement xelemValue)
        {
            var dic = DeserializeTaggedDictionaryValue(xelemValue, member.Alias, member.MemberType,
                member.CollectionAttributeInstance, member.DictionaryAttributeInstance);

            try
            {
                member.SetValue(o, dic);
            }
            catch
            {
                OnExceptionOccurred(new YAXPropertyCannotBeAssignedTo(member.Alias.LocalName, xelemValue),
                    Options.ExceptionBehavior);
            }
        }

        /// <summary>
        ///     Verifies the existence of dictionary pair <c>Key</c> and <c>Value</c> elements.
        /// </summary>
        /// <param name="type">Type of the key or content.</param>
        /// <param name="isAttribute">if set to <see langword="true" /> means that key or content have been serialize as an attribute.</param>
        /// <param name="isContent">if set to <see langword="true" /> means that key or content has been serialize as an XML content.</param>
        /// <param name="alias">The alias for the key or content.</param>
        /// <param name="childElem">The child XML element to search <c>Key</c> and <c>Value</c> elements in.</param>
        /// <returns><ref langword="true"/> if the elements were found.</returns>
        private bool VerifyDictionaryPairElements(ref Type type, ref bool isAttribute, ref bool isContent,
            XName alias, XElement childElem)
        {
            bool isFound;

            if (isAttribute && childElem.Attribute_NamespaceSafe(alias, _documentDefaultNamespace) != null)
            {
                isFound = true;
            }
            else if (isContent && childElem.GetXmlContent() != null)
            {
                isFound = true;
            }
            else
            {
                isFound = VerifyDictionaryPairElementsInChild(ref type, ref isAttribute,  ref isContent, alias, childElem);
            }

            return isFound;
        }

        /// <summary>
        ///     Verifies the existence of a child dictionary pair <c>Key</c> and <c>Value</c> element.
        ///     Here we look for an element with the same name.
        ///     If it is found, we also check for a yaxlib:realtype attribute to get the real type.
        /// </summary>
        /// <param name="type">Type of the key or content.</param>
        /// <param name="isAttribute">if set to <see langword="true" /> means that key or content have been serialize as an attribute.</param>
        /// <param name="isContent">if set to <see langword="true" /> means that key or content has been serialize as an XML content.</param>
        /// <param name="alias">The alias for the key or content.</param>
        /// <param name="childElem">The child XML element to search <c>Key</c> and <c>Value</c> elements in.</param>
        /// <returns><ref langword="true"/> if the elements were found.</returns>
        private bool VerifyDictionaryPairElementsInChild(ref Type type, ref bool isAttribute, ref bool isContent, XName alias,
            XElement childElem)
        {
            var elem = childElem.Element(alias);
            if (elem == null) return false;

            var realTypeAttr = elem.Attribute_NamespaceSafe(Options.Namespace.Uri + Options.AttributeName.RealType,
                _documentDefaultNamespace);

            if (realTypeAttr == null) 
                return true; // we found a child element (but without yaxlib:realtype attribute)

            var theRealType = ReflectionUtils.GetTypeByName(realTypeAttr.Value);
            if (theRealType != null)
            {
                isAttribute = false;
                isContent = false;
                type = theRealType;
            }

            return true; // we found a child element (but without finding the real type)
        }

        /// <summary>
        ///     Deserializes the XML representation of a key-value pair, as specified, and returns
        ///     a <c>KeyValuePair</c> instance containing the deserialized data.
        /// </summary>
        /// <param name="baseElement">The element containing the XML representation of a key-value pair.</param>
        /// <returns>a <c>KeyValuePair</c> instance containing the deserialized data</returns>
        private object DeserializeKeyValuePair(XElement baseElement)
        {
            var genArgs = _type.GetGenericArguments();
            var keyType = genArgs[0];
            var valueType = genArgs[1];

            var xNameKey = TypeNamespace.IfEmptyThenNone() + "Key";
            var xNameValue = TypeNamespace.IfEmptyThenNone() + "Value";

            object keyValue, valueValue;
            if (ReflectionUtils.IsBasicType(keyType))
            {
                try
                {
                    keyValue = ReflectionUtils.ConvertBasicType(
                        baseElement.Element(xNameKey)?.Value, keyType, Options.Culture);
                }
                catch (NullReferenceException)
                {
                    keyValue = null;
                }
            }
            else if (ReflectionUtils.IsStringConvertibleIFormattable(keyType))
            {
                keyValue = Activator.CreateInstance(keyType, baseElement.Element(xNameKey)?.Value);
            }
            else if (ReflectionUtils.IsCollectionType(keyType))
            {
                keyValue = DeserializeCollectionValue(keyType,
                    baseElement.Element(xNameKey), xNameKey, null);
            }
            else
            {
                var ser = NewInternalSerializer(keyType, xNameKey.Namespace.IfEmptyThenNone(), null);
                keyValue = ser.DeserializeBase(baseElement.Element(xNameKey));
                FinalizeNewSerializer(ser, false);
            }

            if (ReflectionUtils.IsBasicType(valueType))
            {
                try
                {
                    valueValue = ReflectionUtils.ConvertBasicType(baseElement.Element(xNameValue)?.Value, valueType, Options.Culture);
                }
                catch (NullReferenceException)
                {
                    valueValue = null;
                }
            }
            else if (ReflectionUtils.IsStringConvertibleIFormattable(valueType))
            {
                valueValue = Activator.CreateInstance(valueType, baseElement.Element(xNameValue)?.Value);
            }
            else if (ReflectionUtils.IsCollectionType(valueType))
            {
                valueValue = DeserializeCollectionValue(valueType,
                    baseElement.Element(xNameValue), xNameValue, null);
            }
            else
            {
                var ser = NewInternalSerializer(valueType, xNameValue.Namespace.IfEmptyThenNone(), null);
                valueValue = ser.DeserializeBase(baseElement.Element(xNameValue));
                FinalizeNewSerializer(ser, false);
            }

            var pair = Activator.CreateInstance(_type, keyValue, valueValue);
            return pair;
        }

        /// <summary>
        ///     Generates XDocument LoadOptions from SerializationOption
        /// </summary>
        private LoadOptions GetXmlLoadOptions()
        {
            var options = LoadOptions.None;
            if (Options.SerializationOptions.HasFlag(YAXSerializationOptions.DisplayLineInfoInExceptions))
                options |= LoadOptions.SetLineInfo;
            return options;
        }

        private static object InvokeCustomDeserializerFromElement(Type customDeserType, XElement elemToDeser, MemberWrapper memberWrapper, UdtWrapper udtWrapper, YAXSerializer currentSerializer)
        {
            var customDeserializer = Activator.CreateInstance(customDeserType, Array.Empty<object>());
            return customDeserType.InvokeMethod("DeserializeFromElement", customDeserializer, new object[] {elemToDeser, new SerializationContext(memberWrapper, udtWrapper, currentSerializer.Options)});
        }

        private static object InvokeCustomDeserializerFromAttribute(Type customDeserType, XAttribute attrToDeser, MemberWrapper memberWrapper, UdtWrapper udtWrapper, YAXSerializer currentSerializer)
        {
            var customDeserializer = Activator.CreateInstance(customDeserType, Array.Empty<object>());
            return customDeserType.InvokeMethod("DeserializeFromAttribute", customDeserializer, new object[] {attrToDeser, new SerializationContext(memberWrapper, udtWrapper, currentSerializer.Options) });
        }

        private static object InvokeCustomDeserializerFromValue(Type customDeserType, string valueToDeser, MemberWrapper memberWrapper, UdtWrapper udtWrapper, YAXSerializer currentSerializer)
        {
            var customDeserializer = Activator.CreateInstance(customDeserType, Array.Empty<object>());
            return customDeserType.InvokeMethod("DeserializeFromValue", customDeserializer, new object[] {valueToDeser, new SerializationContext(memberWrapper, udtWrapper, currentSerializer.Options) });
        }

        #endregion
    }
}
