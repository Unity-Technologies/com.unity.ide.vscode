using System;
using System.Collections;
using UnityEditor;
using UnityEngine.TestTools;

public class RecompileScripts : IEditModeTestYieldInstruction
{
    public RecompileScripts()
        : this(true) { }

    public RecompileScripts(bool expectScriptCompilation)
        : this(expectScriptCompilation, true) { }

    public RecompileScripts(bool expectScriptCompilation, bool expectScriptCompilationSuccess)
    {
        ExpectScriptCompilation = expectScriptCompilation;
        ExpectScriptCompilationSuccess = expectScriptCompilationSuccess;
        ExpectDomainReload = true;
    }

    public bool ExpectDomainReload { get; }
    public bool ExpectedPlaymodeState { get; }
    public bool ExpectScriptCompilation { get; }
    public bool ExpectScriptCompilationSuccess { get; }
    public static RecompileScripts Current { get; private set; }

    public IEnumerator Perform()
    {
        Current = this;

        AssetDatabase.Refresh();

        if (ExpectScriptCompilation && !EditorApplication.isCompiling)
        {
            Current = null;
            throw new Exception("Editor does not need to recompile scripts");
        }

        EditorApplication.UnlockReloadAssemblies();

        while (EditorApplication.isCompiling)
        {
            yield return null;
        }

        Current = null;

        if (ExpectScriptCompilationSuccess && EditorUtility.scriptCompilationFailed)
        {
            EditorApplication.LockReloadAssemblies();
            throw new Exception("Script compilation failed");
        }
    }
}
