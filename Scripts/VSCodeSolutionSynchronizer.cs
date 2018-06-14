using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VSCodePackage;

public class VSCodeSolutionSynchronizer : ScriptableObject
{
	static VSCodeScriptEditor s_ScriptEditor;

	[MenuItem("VSCode/Generate")]
	public static void Test()
	{
		s_ScriptEditor.GenerateAndWriteSolutionAndProjects();
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
	string m_Arguments;

	public bool TryGetInstallationForPath(string editorPath, out ExternalScriptEditor.Installation installation)
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
				installation = new ExternalScriptEditor.Installation
				{
					Name = editorPath,
					Path = editorPath
				};
			}
			return true;
		}

		installation = default(ExternalScriptEditor.Installation);
		return false;
	}

	public string DefaultArgument { get; } = "\"$(ProjectPath)\" -g \"$(File)\":$(Line)";

	public string Arguments
	{
		get
		{
			if (m_Arguments == null)
			{
				m_Arguments = EditorPrefs.GetString("vscode_arguments", DefaultArgument);
			}

			return m_Arguments;
		}
		set
		{
			m_Arguments = value;
			EditorPrefs.SetString("vscode_arguments", value);
		}
	}

	public ScriptEditor Editor => ScriptEditor.VisualStudioCode;

	public bool CustomArgumentsAllowed => true;

	public ExternalScriptEditor.Installation[] Installations => m_Discoverability.PathCallback();

	public VSCodeScriptEditor()
	{
		m_Discoverability = new VSCodeDiscovery();
		m_ProjectGeneration = new ProjectGeneration();
	}

	static VSCodeScriptEditor()
	{
		ExternalScriptEditor.Register(new VSCodeScriptEditor());
		//VSCodeSolutionSynchronizer.RegisterScriptEditor(vsCodeScriptEditor);
	}

	public void GenerateAndWriteSolutionAndProjects()
	{
		m_ProjectGeneration.GenerateSolutionAndProjectFiles();
	}
}