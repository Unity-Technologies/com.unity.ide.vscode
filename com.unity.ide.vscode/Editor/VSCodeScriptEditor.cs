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

        static readonly string[] k_SupportedFileNames = { "code.exe", "visualstudiocode.app", "visualstudiocode-insiders.app", "vscode.app", "code.app", "code.cmd", "code-insiders.cmd", "code", "com.visualstudio.code" };

        public bool TryGetInstallationForPath(string editorPath, out ScriptEditor.Installation installation)
        {
            var lowerCasePath = editorPath.ToLower();
            var filename = Path.GetFileName(lowerCasePath).Replace(" ", "");
            var installations = Installations;
            if (!k_SupportedFileNames.Contains(filename))
            {
                installation = default;
                return false;
            }
            if (!installations.Any())
            {
                installation = default;
                return false;
            }
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

        public bool OpenFileAtLineColumn(string path, int line, int column)
        {
            if (line == -1)
                line = 1;
            if (column == -1)
                column = 0;

            string arguments;
            if (Arguments != DefaultArgument)
            {
                arguments = m_ProjectGeneration.ProjectDirectory != path
                    ? ParseArgument(Arguments, path, line, column)
                    : m_ProjectGeneration.ProjectDirectory;
            }
            else
            {
                arguments = $@"""{m_ProjectGeneration.ProjectDirectory}""";
                if (m_ProjectGeneration.ProjectDirectory != path && path.Length != 0)
                {
                    arguments += $@" -g ""{path}"":{line}:{column}";
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

        string ParseArgument(string arguments, string path, int line, int column)
        {
            var newargument = arguments.Replace("$(ProjectPath)", m_ProjectGeneration.ProjectDirectory);
            newargument = newargument.Replace("$(File)", path);
            newargument = newargument.Replace("$(Line)", line > 0 ? line.ToString() : "1");
            newargument = newargument.Replace("$(Column)", column >= 0 ? column.ToString() : "0");
            return newargument;
        }

        string DefaultArgument { get; } = "\"$(ProjectPath)\" -g \"$(File)\":$(Line):$(Column)";
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
