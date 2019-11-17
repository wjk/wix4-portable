// Copyright (c) William Kent and .NET Foundation. All rights reserved.
// Licensed under the Ms-RL license. See LICENSE file in the project root for full license information.

using System;
using System.Reflection;
using System.Xml;

#pragma warning disable CS8606 // Possible null reference assignment to iteration variable (multiple false positives).

namespace WixToolset.Serialize
{
    /// <summary>
    /// Class used for reading XML files in to the CodeDom.
    /// </summary>
    public class CodeDomReader
    {
        private Assembly[] assemblies;

        /// <summary>
        /// Initializes a new instance of the <see cref="CodeDomReader"/> class, using the current assembly.
        /// </summary>
        public CodeDomReader()
        {
            this.assemblies = new Assembly[] { Assembly.GetExecutingAssembly() };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CodeDomReader"/> class, and
        /// takes in a list of assemblies in which to look for elements.
        /// </summary>
        /// <param name="assemblies">Assemblies in which to look for types that correspond
        /// to elements.</param>
        public CodeDomReader(Assembly[] assemblies)
        {
            this.assemblies = assemblies;
        }

        /// <summary>
        /// Loads an XML file into a strongly-typed code dom.
        /// </summary>
        /// <param name="filePath">File to load into the code dom.</param>
        /// <returns>The strongly-typed object at the root of the tree.</returns>
        public ISchemaElement Load(string filePath)
        {
            XmlDocument document = new XmlDocument();
            document.Load(filePath);
            ISchemaElement? schemaElement = null;

            foreach (XmlNode node in document.ChildNodes)
            {
                if (node is XmlElement element)
                {
                    if (schemaElement != null)
                    {
                        throw new InvalidOperationException("Multiple root elements found in file.");
                    }

                    schemaElement = this.CreateObjectFromElement(element);
                    this.ParseObjectFromElement(schemaElement, element);
                }
            }

            if (schemaElement == null)
            {
                throw new InvalidOperationException("No root element found in file.");
            }

            return schemaElement;
        }

        /// <summary>
        /// Parses an ISchemaElement from the XmlElement.
        /// </summary>
        /// <param name="schemaElement">ISchemaElement to fill in.</param>
        /// <param name="element">XmlElement to parse from.</param>
        private void ParseObjectFromElement(ISchemaElement? schemaElement, XmlElement element)
        {
            foreach (XmlAttribute attribute in element.Attributes)
            {
                if (attribute == null)
                {
                    throw new InvalidOperationException("XmlElement.Attributes contains null attribute");
                }

                if (schemaElement == null)
                {
                    throw new ArgumentNullException(nameof(schemaElement));
                }

                this.SetAttributeOnObject(schemaElement, attribute.LocalName, attribute.Value);
            }

            foreach (XmlNode node in element.ChildNodes)
            {
                if (node is XmlElement childElement)
                {
                    ISchemaElement? childSchemaElement;
                    if (!(schemaElement is ICreateChildren createChildren))
                    {
                        throw new InvalidOperationException("ISchemaElement with name " + element.LocalName + " does not implement ICreateChildren.");
                    }
                    else
                    {
                        childSchemaElement = createChildren.CreateChild(childElement.LocalName);
                    }

                    if (childSchemaElement == null)
                    {
                        childSchemaElement = this.CreateObjectFromElement(childElement);
                        if (childSchemaElement == null)
                        {
                            throw new InvalidOperationException("XmlElement with name " + childElement.LocalName + " does not have a corresponding ISchemaElement.");
                        }
                    }

                    this.ParseObjectFromElement(childSchemaElement, childElement);
                    IParentElement parentElement = (IParentElement)schemaElement;
                    parentElement.AddChild(childSchemaElement);
                }
                else
                {
                    if (node is XmlText childText)
                    {
                        if (schemaElement == null)
                        {
                            throw new ArgumentNullException(nameof(schemaElement));
                        }

                        this.SetAttributeOnObject(schemaElement, "Content", childText.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Sets an attribute on an ISchemaElement.
        /// </summary>
        /// <param name="schemaElement">Schema element to set attribute on.</param>
        /// <param name="name">Name of the attribute to set.</param>
        /// <param name="value">Value to set on the attribute.</param>
        private static void SetAttributeOnObject(ISchemaElement schemaElement, string name, string value)
        {
            if (!(schemaElement is ISetAttributes setAttributes))
            {
                throw new InvalidOperationException($"ISchemaElement with name {schemaElement.GetType().FullName} does not implement ISetAttributes.");
            }
            else
            {
                setAttributes.SetAttribute(name, value);
            }
        }

        /// <summary>
        /// Creates an object from an XML element by digging through the assembly list.
        /// </summary>
        /// <param name="element">XML Element to create an ISchemaElement from.</param>
        /// <returns>A constructed ISchemaElement.</returns>
        private ISchemaElement? CreateObjectFromElement(XmlElement element)
        {
            ISchemaElement? schemaElement = null;
            foreach (Assembly assembly in this.assemblies)
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (type == null || type.FullName == null)
                    {
                        throw new InvalidOperationException("assembly.GetTypes() returned null or invalid element");
                    }

                    if (type.FullName.EndsWith(element.LocalName, StringComparison.Ordinal)
                        && typeof(ISchemaElement).IsAssignableFrom(type))
                    {
                        schemaElement = (ISchemaElement?)Activator.CreateInstance(type);
                    }
                }
            }

            return schemaElement;
        }
    }
}
