using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace VSCodeEditor {
    [InitializeOnLoad]
    public class VSCodeScriptEditor : IExternalScriptEditor
    {
        IDiscovery m_Discoverability;
        IGenerator m_ProjectGeneration;
        static readonly GUIContent k_ResetArguments = EditorGUIUtility.TrTextContent("Reset argument");
        string m_Arguments;

        public bool TryGetInstallationForPath(string editorPath, out ScriptEditor.Installation installation)
        {
            var lowerCasePath = editorPath.ToLower();
            var filename = Path.GetFileName(lowerCasePath).Replace(" ", "");
            var installations = Installations;
            if (filename.StartsWith("code") && installations.Count() != 0)
            {
                try
                {
                    installation = installations.First(inst => inst.Path == editorPath);
                }
                catch (InvalidOperationException)
                {
                    installation = new ScriptEditor.Installation
                    {
                        Name = "Visual Studio Code",
                        Path = editorPath
                    };
                }

                return true;
            }

            installation = default;
            return false;
        }

        public void OnGUI()
        {
            Arguments = EditorGUILayout.TextField("External Script Editor Args", Arguments);
            if (GUILayout.Button(k_ResetArguments, GUILayout.Width(120)))
            {
                Arguments = DefaultArgument;
            }
        }

        public void CreateIfDoesntExist()
        {
            if (!m_ProjectGeneration.HasSolutionBeenGenerated())
            {
                m_ProjectGeneration.Sync();
            }
        }

        public void SyncIfNeeded(IEnumerable<string> affectedFiles, IEnumerable<string> reimportedFiles)
        {
            m_ProjectGeneration.SyncIfNeeded(affectedFiles, reimportedFiles);
        }

        public void Sync()
        {
            m_ProjectGeneration.Sync();
        }

        public void Initialize(string editorInstallationPath)
        {
        }

        public bool OpenFileAtLine(string path, int line)
        {
            if (line == -1)
                line = 1;

            string arguments;
            if (Arguments != DefaultArgument)
            {
                if (m_ProjectGeneration.ProjectDirectory != path)
                {
                    arguments = ParseArgument(Arguments, path, line);
                }
                else
                {
                    arguments = m_ProjectGeneration.ProjectDirectory;
                }
            }
            else
            {
                arguments = $@"""{m_ProjectGeneration.ProjectDirectory}""";
                if (m_ProjectGeneration.ProjectDirectory != path && path.Length != 0)
                {
                    arguments += $@" -g ""{path}"":{line}";
                }
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = EditorPrefs.GetString("kScriptsDefaultApp"),
                    Arguments = arguments,
                    UseShellExecute = true,
                }
            };

            process.Start();
            return true;
        }

        string ParseArgument(string arguments, string path, int line)
        {
            var newargument = arguments.Replace("$(ProjectPath)", m_ProjectGeneration.ProjectDirectory);
            newargument = newargument.Replace("$(File)", path);
            newargument = newargument.Replace("$(Line)", line.ToString());
            return newargument;
        }

        string DefaultArgument { get; } = "\"$(ProjectPath)\" -g \"$(File)\":$(Line)";
        string Arguments
        {
            get => m_Arguments ?? (m_Arguments = EditorPrefs.GetString("vscode_arguments", DefaultArgument));
            set
            {
                m_Arguments = value;
                EditorPrefs.SetString("vscode_arguments", value);
            }
        }

        public ScriptEditor.Installation[] Installations => m_Discoverability.PathCallback();

        public VSCodeScriptEditor(IDiscovery discovery, IGenerator projectGeneration)
        {
            m_Discoverability = discovery;
            m_ProjectGeneration = projectGeneration;
        }

        static VSCodeScriptEditor()
        {
            ScriptEditor.Register(new VSCodeScriptEditor(new VSCodeDiscovery(), new ProjectGeneration()));
        }
    }
}
