// Copyright (c) William Kent and .NET Foundation. All rights reserved.
// Licensed under the Ms-RL license. See LICENSE file in the project root for full license information.

using System;
using System.CodeDom;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Xml;

namespace WixBuildTools.MsgGen
{
    /// <summary>
    /// Message files generation class.
    /// </summary>
    public sealed class GenerateMessageFiles
    {
        /// <summary>
        /// Generate the message files.
        /// </summary>
        /// <param name="messagesDoc">Input Xml document containing message definitions.</param>
        /// <param name="codeCompileUnit">CodeDom container.</param>
        /// <param name="resourceWriter">Writer for default resource file.</param>
        public static void Generate(XmlDocument messagesDoc, CodeCompileUnit codeCompileUnit, ResourceWriter resourceWriter)
        {
            var usedNumbers = new Hashtable();

            if (messagesDoc == null)
            {
                throw new ArgumentNullException(nameof(messagesDoc));
            }

            if (codeCompileUnit == null)
            {
                throw new ArgumentNullException(nameof(codeCompileUnit));
            }

            if (resourceWriter == null)
            {
                throw new ArgumentNullException(nameof(resourceWriter));
            }

            string namespaceAttr = messagesDoc.DocumentElement.GetAttribute("Namespace");
            string resourcesAttr = messagesDoc.DocumentElement.GetAttribute("Resources");

            // namespace
            var messagesNamespace = new CodeNamespace(namespaceAttr);
            codeCompileUnit.Namespaces.Add(messagesNamespace);

            // imports
            messagesNamespace.Imports.Add(new CodeNamespaceImport("System"));
            messagesNamespace.Imports.Add(new CodeNamespaceImport("System.Reflection"));
            messagesNamespace.Imports.Add(new CodeNamespaceImport("System.Resources"));
            if (namespaceAttr != "WixToolset.Data")
            {
                messagesNamespace.Imports.Add(new CodeNamespaceImport("WixToolset.Data"));
            }

#pragma warning disable CS8606 // Possible null reference assignment to iteration variable (false positive)
            foreach (XmlElement classElement in messagesDoc.DocumentElement.ChildNodes)
            {
                if (classElement == null)
                {
                    throw new InvalidOperationException("XmlElement is null");
                }

                string className = classElement.GetAttribute("Name");
                string baseContainerName = classElement.GetAttribute("BaseContainerName");
                string containerName = classElement.GetAttribute("ContainerName");
                string messageLevel = classElement.GetAttribute("Level");

                // message container class
                messagesNamespace.Types.Add(CreateContainer(namespaceAttr, baseContainerName, containerName, messageLevel, resourcesAttr));

                // class
                CodeTypeDeclaration messagesClass = new CodeTypeDeclaration(className);
                messagesClass.TypeAttributes = TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed;
                messagesNamespace.Types.Add(messagesClass);

                // private constructor (needed since all methods in this class are static)
                CodeConstructor constructor = new CodeConstructor();
                constructor.Attributes = MemberAttributes.Private;
                constructor.ReturnType = null;
                messagesClass.Members.Add(constructor);

                // messages
                foreach (XmlElement messageElement in classElement.ChildNodes)
                {
                    if (messageElement == null)
                    {
                        throw new InvalidOperationException("XmlElement is null");
                    }

                    int number;
                    string id = messageElement.GetAttribute("Id");
                    string numberString = messageElement.GetAttribute("Number");
                    bool sourceLineNumbers = true;

                    // determine the message number (and ensure it was set properly)
                    if (numberString.Length > 0)
                    {
                        number = Convert.ToInt32(numberString, CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        throw new ApplicationException($"Message number must be assigned for {containerName} '{id}'.");
                    }

                    // check for message number collisions
                    if (usedNumbers.Contains(number))
                    {
                        throw new ApplicationException($"Collision detected between two or more messages with number '{number}'.");
                    }

                    usedNumbers.Add(number, null);

                    if (messageElement.GetAttribute("SourceLineNumbers") == "no")
                    {
                        sourceLineNumbers = false;
                    }

                    int instanceCount = 0;
                    foreach (XmlElement instanceElement in messageElement.ChildNodes)
                    {
                        if (instanceElement == null)
                        {
                            throw new InvalidOperationException("XmlElement is null");
                        }

                        string formatString = instanceElement.InnerText.Trim();
                        string resourceName = string.Concat(className, "_", id, "_", (++instanceCount).ToString(CultureInfo.CurrentCulture));

                        // create a resource
                        resourceWriter.AddResource(resourceName, formatString);

                        // create method
                        CodeMemberMethod method = new CodeMemberMethod();
                        method.ReturnType = new CodeTypeReference(baseContainerName);
                        method.Attributes = MemberAttributes.Public | MemberAttributes.Static;
                        messagesClass.Members.Add(method);

                        // method name
                        method.Name = id;

                        // return statement
                        CodeMethodReturnStatement stmt = new CodeMethodReturnStatement();
                        method.Statements.Add(stmt);

                        // return statement expression
                        CodeObjectCreateExpression expr = new CodeObjectCreateExpression();
                        stmt.Expression = expr;

                        // new struct
                        expr.CreateType = new CodeTypeReference(containerName);

                        // optionally have sourceLineNumbers as the first parameter
                        if (sourceLineNumbers)
                        {
                            // sourceLineNumbers parameter
                            expr.Parameters.Add(new CodeArgumentReferenceExpression("sourceLineNumbers"));
                        }
                        else
                        {
                            expr.Parameters.Add(new CodePrimitiveExpression(null));
                        }

                        // message number parameter
                        expr.Parameters.Add(new CodePrimitiveExpression(number));

                        // resource name parameter
                        expr.Parameters.Add(new CodePrimitiveExpression(resourceName));

                        // optionally have sourceLineNumbers as the first parameter
                        if (sourceLineNumbers)
                        {
                            method.Parameters.Add(new CodeParameterDeclarationExpression("SourceLineNumber", "sourceLineNumbers"));
                        }

                        foreach (XmlNode parameterNode in instanceElement.ChildNodes)
                        {
                            if (parameterNode == null)
                            {
                                throw new InvalidOperationException("XmlElement is null");
                            }

                            if (parameterNode is XmlElement parameterElement)
                            {
                                string type = parameterElement.GetAttribute("Type");
                                string name = parameterElement.GetAttribute("Name");

                                // method parameter
                                method.Parameters.Add(new CodeParameterDeclarationExpression(type, name));

                                // String.Format parameter
                                expr.Parameters.Add(new CodeArgumentReferenceExpression(name));
                            }
                        }
                    }
                }
            }
#pragma warning restore CS8606 // Possible null reference assignment to iteration variable
        }

        /// <summary>
        /// Create message container class.
        /// </summary>
        /// <param name="namespaceName">Namespace to use for resources stream.</param>
        /// <param name="baseContainerName">Name of the base message container class.</param>
        /// <param name="containerName">Name of the message container class.</param>
        /// <param name="messageLevel">Message level of for the message.</param>
        /// <param name="resourcesName">Name of the resources stream (will get namespace prepended).</param>
        /// <returns>Message container class CodeDom object.</returns>
        private static CodeTypeDeclaration CreateContainer(string namespaceName, string baseContainerName, string containerName, string messageLevel, string resourcesName)
        {
            CodeTypeDeclaration messageContainer = new CodeTypeDeclaration();

            messageContainer.Name = containerName;
            messageContainer.BaseTypes.Add(new CodeTypeReference(baseContainerName));

            // constructor
            CodeConstructor constructor = new CodeConstructor();
            constructor.Attributes = MemberAttributes.Public;
            constructor.ReturnType = null;
            messageContainer.Members.Add(constructor);

            CodeMemberField resourceManager = new CodeMemberField();
            resourceManager.Attributes = MemberAttributes.Private | MemberAttributes.Static;
            resourceManager.Name = "resourceManager";
            resourceManager.Type = new CodeTypeReference("ResourceManager");
            resourceManager.InitExpression = new CodeObjectCreateExpression("ResourceManager", new CodeSnippetExpression($"\"{namespaceName}.{resourcesName}\""), new CodeSnippetExpression("Assembly.GetExecutingAssembly()"));
            messageContainer.Members.Add(resourceManager);

            // constructor parameters
            constructor.Parameters.Add(new CodeParameterDeclarationExpression("SourceLineNumber", "sourceLineNumbers"));
            constructor.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "id"));
            constructor.Parameters.Add(new CodeParameterDeclarationExpression(typeof(string), "resourceName"));
            CodeParameterDeclarationExpression messageArgsParam = new CodeParameterDeclarationExpression("params object[]", "messageArgs");
            constructor.Parameters.Add(messageArgsParam);

            constructor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("sourceLineNumbers"));
            constructor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("id"));
            constructor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("resourceName"));
            constructor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("messageArgs"));

            // assign base.Level if messageLevel is specified
            if (!string.IsNullOrEmpty(messageLevel))
            {
                CodePropertyReferenceExpression levelReference = new CodePropertyReferenceExpression(new CodeBaseReferenceExpression(), "Level");
                CodeFieldReferenceExpression messageLevelField = new CodeFieldReferenceExpression(new CodeTypeReferenceExpression("MessageLevel"), messageLevel);
                constructor.Statements.Add(new CodeAssignStatement(levelReference, messageLevelField));
            }

            // Assign base.ResourceManager property
            CodePropertyReferenceExpression baseResourceManagerReference = new CodePropertyReferenceExpression(new CodeBaseReferenceExpression(), "ResourceManager");
            CodeFieldReferenceExpression resourceManagerField = new CodeFieldReferenceExpression(null, "resourceManager");
            constructor.Statements.Add(new CodeAssignStatement(baseResourceManagerReference, resourceManagerField));

            return messageContainer;
        }
    }
}
