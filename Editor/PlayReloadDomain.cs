#if UNITY_6000_3_OR_NEWER

#region

using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

#endregion

/// <summary>
///     Enter Play Mode with domain reload disabled by default; press the reload-and-play button next to Play to
///     reload the domain. The project's Enter Play Mode settings are overridden for the play session only and
///     restored on return to edit mode.
/// </summary>
[InitializeOnLoad]
public static class PlayReloadDomain
{
    const string ToolbarElementPath = "ColdSnap/Play Mode/Play With Domain Reload";
    const string OriginalEnabledKey = "PlayReloadDomain_OriginalEnabled";
    const string OriginalOptionsKey = "PlayReloadDomain_OriginalOptions";
    const string HasOverrideKey = "PlayReloadDomain_HasOverride";

    // Set by the toolbar button and consumed on ExitingEditMode, before any domain reload can clear it.
    static bool reloadDomainOnNextPlay;

    static PlayReloadDomain() {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        // Recover if the editor was closed or crashed mid-session with the override still applied.
        EditorApplication.delayCall += () => {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Restore();
        };
    }

    [MainToolbarElement(ToolbarElementPath, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 1)]
    public static MainToolbarElement CreateToolbarButton() {
        var icon = EditorGUIUtility.IconContent("Refresh").image as Texture2D;
        var content = new MainToolbarContent(icon, "Play with domain reload (normal Play skips it).");
        return new MainToolbarButton(content, PlayWithDomainReload) {
            enabled = !EditorApplication.isPlayingOrWillChangePlaymode
        };
    }

    static void PlayWithDomainReload() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        reloadDomainOnNextPlay = true;
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state) {
        switch (state) {
            case PlayModeStateChange.ExitingEditMode:
                Apply(reloadDomainOnNextPlay);
                reloadDomainOnNextPlay = false;
                break;
            case PlayModeStateChange.EnteredEditMode:
                Restore();
                break;
        }

        MainToolbar.Refresh(ToolbarElementPath);
    }

    static void Apply(bool reloadDomain) {
        if (!SessionState.GetBool(HasOverrideKey, false)) {
            SessionState.SetBool(OriginalEnabledKey, EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(OriginalOptionsKey, (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(HasOverrideKey, true);
        }

        if (reloadDomain) {
            // Full domain + scene reload, as with the option disabled.
            EditorSettings.enterPlayModeOptionsEnabled = false;
        }
        else {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }
    }

    static void Restore() {
        if (!SessionState.GetBool(HasOverrideKey, false)) return;
        EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(OriginalEnabledKey, false);
        EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(OriginalOptionsKey, 0);
        SessionState.SetBool(HasOverrideKey, false);
    }
}

#endif
