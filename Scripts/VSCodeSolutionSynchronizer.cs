using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using VSCodePackage;

public class VSCodeSolutionSynchronizer : ScriptableObject
{
    static VSCodeScriptEditor s_ScriptEditor;

    [MenuItem("VSCode/Generate")]
    public static void Test()
    {
    }

    public static void RegisterScriptEditor(VSCodeScriptEditor scriptEditor)
    {
        s_ScriptEditor = scriptEditor;
    }
}

[InitializeOnLoad]
public class VSCodeScriptEditor : IExternalScriptEditor
{
    VSCodeDiscovery m_Discoverability;
    ProjectGeneration m_ProjectGeneration;
    static readonly GUIContent resetArguments = EditorGUIUtility.TrTextContent("Reset argument");
    string m_Arguments;

    public bool TryGetInstallationForPath(string editorPath, out ScriptEditor.Installation installation)
    {
        var lowerCasePath = editorPath.ToLower();
        var filename = Path.GetFileName(lowerCasePath).Replace(" ", "");
        if (filename.StartsWith("code"))
        {
            try
            {
                installation = Installations.First(inst => inst.Path == editorPath);
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

        installation = default(ScriptEditor.Installation);
        return false;
    }

    public void OnGUI()
    {
        Arguments = EditorGUILayout.TextField("External Script Editor Args", Arguments);
        if (GUILayout.Button(resetArguments, GUILayout.Width(120)))
        {
            Arguments = DefaultArgument;
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
            arguments = m_ProjectGeneration.ProjectDirectory;
            if (m_ProjectGeneration.ProjectDirectory != path)
            {
                arguments += $" -g {path}:{line}";
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

    public string DefaultArgument { get; } = "\"$(ProjectPath)\" -g \"$(File)\":$(Line)";

    public string Arguments
    {
        get => m_Arguments ?? (m_Arguments = EditorPrefs.GetString("vscode_arguments", DefaultArgument));
        set
        {
            m_Arguments = value;
            EditorPrefs.SetString("vscode_arguments", value);
        }
    }

    public ScriptEditor.Installation[] Installations => m_Discoverability.PathCallback();

    public VSCodeScriptEditor()
    {
        m_Discoverability = new VSCodeDiscovery();
        m_ProjectGeneration = new ProjectGeneration();
    }

    static VSCodeScriptEditor()
    {
        ScriptEditor.Register(new VSCodeScriptEditor());
    }
}
