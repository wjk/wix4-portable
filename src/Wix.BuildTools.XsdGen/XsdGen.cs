// Copyright (c) William Kent and .NET Foundation. All rights reserved.
// Licensed under the Ms-RL license. See LICENSE file in the project root for full license information.

using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using Microsoft.CSharp;

namespace WixToolset.Tools
{
    /// <summary>
    /// Generates a strongly-typed C# class from an XML schema (XSD).
    /// </summary>
    public class XsdGen
    {
        private string xsdFile;
        private string outFile;
        private string outputNamespace;
        private string commonNamespace;
        private bool showHelp;

        /// <summary>
        /// The main entry point for XsdGen.
        /// </summary>
        /// <param name="args">Command-line arguments passed to the program.</param>
        [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1642:Constructor summary documentation should begin with standard text", Justification = "This is not a typical constructor; it is really part of the Main method.")]
#pragma warning disable CS8618 // Non-nullable fields are initialized in ParseCommandLineArgs() method.
        private XsdGen(string[] args)
#pragma warning restore CS8618
        {
            this.ParseCommandlineArgs(args);

            // show usage information
            if (this.showHelp)
            {
                Console.WriteLine("usage: XsdGen.exe <schema>.xsd <outputFile> <namespace> [<commonNamespace>]");
                return;
            }

            // ensure that the schema file exists
            if (!File.Exists(this.xsdFile))
            {
                throw new ApplicationException(string.Format(CultureInfo.InvariantCulture, "Schema file does not exist: '{0}'.", this.xsdFile));
            }

            XmlSchema document;
            using (StreamReader xsdFileReader = new StreamReader(this.xsdFile))
            using (XmlTextReader reader = new XmlTextReader(xsdFileReader))
            {
                document = XmlSchema.Read(reader, new ValidationEventHandler(this.ValidationHandler));
            }

            CodeCompileUnit codeCompileUnit = StronglyTypedClasses.Generate(document, this.outputNamespace, this.commonNamespace);

            using (CSharpCodeProvider codeProvider = new CSharpCodeProvider())
            {
                CodeGeneratorOptions options = new CodeGeneratorOptions();
                options.BlankLinesBetweenMembers = true;
                options.BracingStyle = "C";
                options.IndentString = "    ";

                using (StreamWriter csharpFileWriter = new StreamWriter(this.outFile))
                {
                    codeProvider.GenerateCodeFromCompileUnit(codeCompileUnit, csharpFileWriter, options);
                }
            }
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        /// <returns>The error code.</returns>
        [STAThread]
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Need to catch any exception so it can be reported to the user.")]
        public static int Main(string[] args)
        {
            try
            {
                XsdGen xsdGen = new XsdGen(args);
            }
            catch (Exception e)
            {
                Console.WriteLine("XsdGen.exe : fatal error MSF0000: {0}\r\n\r\nStack Trace:\r\n{1}", e.Message, e.StackTrace);
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// Validation event handler.
        /// </summary>
        /// <param name="sender">Sender for the event.</param>
        /// <param name="e">Event args.</param>
        public void ValidationHandler(object sender, ValidationEventArgs e)
        {
        }

        /// <summary>
        /// Parse the command line arguments.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        private void ParseCommandlineArgs(string[] args)
        {
            if (args.Length < 3)
            {
                this.showHelp = true;
            }
            else
            {
                this.xsdFile = args[0];
                this.outFile = args[1];
                this.outputNamespace = args[2];

                if (args.Length >= 4)
                {
                    this.commonNamespace = args[3];
                }
            }
        }
    }
}
