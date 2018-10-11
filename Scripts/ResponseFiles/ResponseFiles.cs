using System;
using System.Collections.Generic;
using System.IO;

namespace VSCodePackage.ResponseFiles
{
    public class ResponseFileData
    {
        public class Reference
        {
            public String Alias;
            public String Assembly;
        }

        public string[] Defines;
        public Reference[] FullPathReferences;
        public bool Unsafe;
        public string[] Errors;
        public string[] OtherArguments;
    }

    public class CompilerOption
    {
        public string Arg;
        public string Value;
    }

    class ResponseFiles
    {
        static readonly char[] CompilerOptionArgumentSeperators = { ';', ',' };

        public static ResponseFileData ParseResponseFileFromFile(
            string responseFilePath,
            string projectDirectory,
            string[] systemReferenceDirectories)
        {
            if (!File.Exists(responseFilePath))
            {
                var empty = new ResponseFileData
                {
                    Defines = new string[0],
                    FullPathReferences = new ResponseFileData.Reference[0],
                    Unsafe = false,
                    Errors = new string[0]
                };

                return empty;
            }

            var responseFileText = File.ReadAllText(responseFilePath);

            return ParseResponseFileText(
                responseFileText,
                responseFilePath,
                projectDirectory,
                systemReferenceDirectories);
        }

        // From:
        // https://github.com/mono/mono/blob/c106cdc775792ceedda6da58de7471f9f5c0b86c/mcs/mcs/settings.cs
        //
        // settings.cs: All compiler settings
        //
        // Author: Miguel de Icaza (miguel@ximian.com)
        //            Ravi Pratap  (ravi@ximian.com)
        //            Marek Safar  (marek.safar@gmail.com)
        //
        //
        // Dual licensed under the terms of the MIT X11 or GNU GPL
        //
        // Copyright 2001 Ximian, Inc (http://www.ximian.com)
        // Copyright 2004-2008 Novell, Inc
        // Copyright 2011 Xamarin, Inc (http://www.xamarin.com)
        static string[] ResponseFileTextToStrings(string responseFileText)
        {
            var args = new List<string>();

            var sb = new System.Text.StringBuilder();

            var textLines = responseFileText.Split('\n', '\r');

            foreach (var line in textLines)
            {
                int t = line.Length;

                for (int i = 0; i < t; i++)
                {
                    char c = line[i];

                    if (c == '"' || c == '\'')
                    {
                        char end = c;

                        for (i++; i < t; i++)
                        {
                            c = line[i];

                            if (c == end)
                                break;
                            sb.Append(c);
                        }
                    }
                    else if (c == ' ')
                    {
                        if (sb.Length > 0)
                        {
                            args.Add(sb.ToString());
                            sb.Length = 0;
                        }
                    }
                    else
                        sb.Append(c);
                }
                if (sb.Length > 0)
                {
                    args.Add(sb.ToString());
                    sb.Length = 0;
                }
            }

            return args.ToArray();
        }

        static ResponseFileData ParseResponseFileText(
            string responseFileText,
            string responseFileName,
            string projectDirectory,
            string[] systemReferenceDirectories)
        {
            var compilerOptions = new List<CompilerOption>();

            var responseFileStrings = ResponseFileTextToStrings(responseFileText);

            foreach (var line in responseFileStrings)
            {
                int idx = line.IndexOf(':');
                string arg, value;

                if (idx == -1)
                {
                    arg = line;
                    value = "";
                }
                else
                {
                    arg = line.Substring(0, idx);
                    value = line.Substring(idx + 1);
                }

                if (!string.IsNullOrEmpty(arg) && arg[0] == '-')
                    arg = '/' + arg.Substring(1);

                compilerOptions.Add(new CompilerOption { Arg = arg, Value = value });
            }

            var responseArguments = new List<string>();
            var defines = new List<string>();
            var references = new List<ResponseFileData.Reference>();
            bool unsafeDefined = false;
            var errors = new List<string>();

            foreach (var option in compilerOptions)
            {
                var arg = option.Arg;
                var value = option.Value;

                switch (arg)
                {
                    case "/d":
                    case "/define":
                    {
                        if (value.Length == 0)
                        {
                            errors.Add("No value set for define");
                            break;
                        }

                        var defs = value.Split(CompilerOptionArgumentSeperators);
                        foreach (string define in defs)
                            defines.Add(define.Trim());
                    }
                    break;

                    case "/r":
                    case "/reference":
                    {
                        if (value.Length == 0)
                        {
                            errors.Add("No value set for reference");
                            break;
                        }

                        string[] refs = value.Split(CompilerOptionArgumentSeperators);

                        if (refs.Length != 1)
                        {
                            errors.Add("Cannot specify multiple aliases using single /reference option");
                            break;
                        }

                        var reference = refs[0];
                        if (reference.Length == 0)
                        {
                            continue;
                        }

                        ResponseFileData.Reference responseReference;

                        int index = reference.IndexOf('=');
                        if (index > -1)
                        {
                            string alias = reference.Substring(0, index);
                            string assembly = reference.Substring(index + 1);

                            responseReference = new ResponseFileData.Reference { Alias = alias, Assembly = assembly };
                        }
                        else
                        {
                            responseReference = new ResponseFileData.Reference { Alias = string.Empty, Assembly = reference };
                        }

                        string fullPathReference = "";
                        var referencePath = responseReference.Assembly;
                        if (Path.IsPathRooted(referencePath))
                        {
                            fullPathReference = referencePath;
                        }
                        else
                        {
                            foreach (var directory in systemReferenceDirectories)
                            {
                                var systemReferencePath = Path.Combine(directory, referencePath);
                                if (File.Exists(systemReferencePath))
                                {
                                    fullPathReference = systemReferencePath;
                                    break;
                                }
                            }

                            var userPath = Path.Combine(projectDirectory, referencePath);
                            if (File.Exists(userPath))
                            {
                                fullPathReference = userPath;
                            }
                        }

                        if (fullPathReference == "")
                        {
                            errors.Add($"{responseFileName}: not parsed correctly: {responseReference.Assembly} could not be found as a system library.\n" +
                                "If this was meant as a user reference please provide the relative path from project root (parent of the Assets folder) in the response file.");
                            continue;
                        }

                        responseReference.Assembly = fullPathReference.Replace('\\', '/');
                        references.Add(responseReference);
                    }
                    break;

                    case "/unsafe":
                    case "/unsafe+":
                    {
                        unsafeDefined = true;
                    }
                    break;

                    case "/unsafe-":
                    {
                        unsafeDefined = false;
                    }
                    break;
                    default:
                        var valueWithColon = value.Length == 0 ? "" : ":" + value;
                        responseArguments.Add(arg + valueWithColon);
                        break;
                }
            }

            var responseFileData = new ResponseFileData
            {
                Defines = defines.ToArray(),
                FullPathReferences = references.ToArray(),
                Unsafe = unsafeDefined,
                Errors = errors.ToArray(),
                OtherArguments = responseArguments.ToArray(),
            };

            return responseFileData;
        }
    }
}
