// Copyright (c) William Kent and .NET Foundation. All rights reserved.
// Licensed under the Ms-RL license. See LICENSE file in the project root for full license information.

using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Schema;
using Microsoft.CSharp;

namespace WixBuildTools.MsgGen
{
    /// <summary>
    /// The main entry point for MsgGen.
    /// </summary>
    public class MsgGenMain
    {
        private bool showLogo;
        private bool showHelp;

        private string? sourceFile;
        private string? destClassFile;
        private string? destResourcesFile;

        /// <summary>
        /// The main entry point for MsgGen.
        /// </summary>
        /// <summary>
        /// Main method for the MsgGen application within the MsgGenMain class.
        /// </summary>
        /// <param name="args">Commandline arguments to the application.</param>
        [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1642:Constructor summary documentation should begin with standard text", Justification = "This is not a typical constructor; it is part of the main method.")]
        public MsgGenMain(string[] args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            this.showLogo = true;
            this.showHelp = false;

            this.sourceFile = null;
            this.destClassFile = null;
            this.destResourcesFile = null;

            // parse the command line
            this.ParseCommandLine(args);

            if (this.sourceFile == null || this.destClassFile == null)
            {
                this.showHelp = true;
            }

            if (this.destResourcesFile == null && this.destClassFile != null)
            {
                this.destResourcesFile = Path.ChangeExtension(this.destClassFile, ".resources");
            }

            // get the assemblies
            var msgGenAssembly = Assembly.GetExecutingAssembly();

            if (this.showLogo)
            {
                Console.WriteLine("Microsoft (R) Message Generation Tool version {0}", msgGenAssembly.GetName().Version!.ToString());
                Console.WriteLine("Copyright (C) Microsoft Corporation 2004. All rights reserved.");
                Console.WriteLine();
            }

            if (this.showHelp)
            {
                Console.WriteLine(" usage:  MsgGen.exe [-?] [-nologo] sourceFile destClassFile [destResourcesFile]");
                Console.WriteLine();
                Console.WriteLine("   -? this help information");
                Console.WriteLine();
                Console.WriteLine("For more information see: http://wix.sourceforge.net");
                return;   // exit
            }

            // load the schema
            var schemaCollection = new XmlSchemaSet();
            using (var reader = XmlReader.Create(msgGenAssembly.GetManifestResourceStream("WixBuildTools.MsgGen.Xsd.messages.xsd")))
            {
                schemaCollection.Add("http://schemas.microsoft.com/genmsgs/2004/07/messages", reader);
            }

            // load the source file and process it
            var readerSettings = new XmlReaderSettings();
            readerSettings.Schemas = schemaCollection;

#pragma warning disable CS8604 // Possible null reference argument (false positive)
            using (var sr = new StreamReader(this.sourceFile))
            using (var validatingReader = XmlReader.Create(sr, readerSettings))
            {
                var errorsDoc = new XmlDocument();
                errorsDoc.Load(validatingReader);

                var codeCompileUnit = new CodeCompileUnit();

                using (var resourceWriter = new ResourceWriter(this.destResourcesFile))
                {
                    GenerateMessageFiles.Generate(errorsDoc, codeCompileUnit, resourceWriter);
                }

                GenerateCSharpCode(codeCompileUnit, this.destClassFile);
            }
#pragma warning restore CS8604
        }

        /// <summary>Main entry point when run as program.</summary>
        /// <param name="args">Commandline arguments for the application.</param>
        /// <returns>Returns the application error code.</returns>
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                var msgGen = new MsgGenMain(args);
            }
            catch (Exception e)
            {
                Console.WriteLine("MsgGen.exe : fatal error MSGG0000: {0}\r\n\r\nStack Trace:\r\n{1}", e.Message, e.StackTrace);
                if (e is NullReferenceException || e is SEHException)
                {
                    throw;
                }

                return 2;
            }

            return 0;
        }

        /// <summary>
        /// Generate the actual C# code.
        /// </summary>
        /// <param name="codeCompileUnit">The code DOM.</param>
        /// <param name="destClassFile">Destination C# source file.</param>
        public static void GenerateCSharpCode(CodeCompileUnit codeCompileUnit, string destClassFile)
        {
            // generate the code with the C# code provider
            using var provider = new CSharpCodeProvider();

            // create a TextWriter to a StreamWriter to the output file
            using (var sw = new StreamWriter(destClassFile))
            {
                using (var tw = new IndentedTextWriter(sw, "    "))
                {
                    var options = new CodeGeneratorOptions();

                    // code generation options
                    options.BlankLinesBetweenMembers = true;
                    options.BracingStyle = "C";

                    // generate source code using the code generator
                    provider.GenerateCodeFromCompileUnit(codeCompileUnit, tw, options);
                }
            }
        }

        /// <summary>
        /// Parse the commandline arguments.
        /// </summary>
        /// <param name="args">Commandline arguments.</param>
        private void ParseCommandLine(string[] args)
        {
            for (int i = 0; i < args.Length; ++i)
            {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg))
                {
                    // skip blank arguments
                    continue;
                }

                if (arg[0] == '-' || arg[0] == '/')
                {
                    string parameter = arg.Substring(1);
                    if (parameter == "nologo")
                    {
                        this.showLogo = false;
                    }
                    else if (parameter == "?" || parameter == "help")
                    {
                        this.showHelp = true;
                    }
                }
                else if (arg[0] == '@')
                {
                    using (var reader = new StreamReader(arg.Substring(1)))
                    {
                        string? line;
                        var newArgs = new ArrayList();

                        while ((line = reader.ReadLine()) != null)
                        {
                            string newArg = string.Empty;
                            bool betweenQuotes = false;
                            for (int j = 0; j < line.Length; ++j)
                            {
                                // skip whitespace
                                if (!betweenQuotes && (line[j] == ' ' || line[j] == '\t'))
                                {
                                    if (!string.IsNullOrEmpty(newArg))
                                    {
                                        newArgs.Add(newArg);
                                        newArg = string.Empty;
                                    }

                                    continue;
                                }

                                // if we're escaping a quote
                                if (line[j] == '\\' && line[j] == '"')
                                {
                                    ++j;
                                }
                                else if (line[j] == '"')
                                {
                                    // if we've hit a new quote
                                    betweenQuotes = !betweenQuotes;
                                    continue;
                                }

                                newArg = string.Concat(newArg, line[j]);
                            }

                            if (!string.IsNullOrEmpty(newArg))
                            {
                                newArgs.Add(newArg);
                            }
                        }

                        string[] ar = (string[])newArgs.ToArray(typeof(string));
                        this.ParseCommandLine(ar);
                    }
                }
                else if (this.sourceFile == null)
                {
                    this.sourceFile = arg;
                }
                else if (this.destClassFile == null)
                {
                    this.destClassFile = arg;
                }
                else if (this.destResourcesFile == null)
                {
                    this.destResourcesFile = arg;
                }
                else
                {
                    throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, "Unknown argument '{0}'.", arg));
                }
            }
        }
    }
}
