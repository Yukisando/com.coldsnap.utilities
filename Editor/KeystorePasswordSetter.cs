using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class KeystorePasswordSetter
{
    private const string MenuPath = "ColdSnap/Tools/Auto Apply Android Keystore Passwords";
    private const string EnabledKey = "ColdSnap.KeystorePasswordSetter.Enabled";

    // Replace these placeholders with your local signing credentials if you use this helper.
    private const string KeystorePassword = "keystore_pass";
    private const string KeyAliasPassword = "keystore_pass";

    static KeystorePasswordSetter()
    {
        EditorApplication.delayCall += ApplyPasswordsIfEnabled;
    }

    [MenuItem(MenuPath)]
    private static void ToggleAutoApply()
    {
        bool isEnabled = !EditorPrefs.GetBool(EnabledKey, false);
        EditorPrefs.SetBool(EnabledKey, isEnabled);

        if (isEnabled)
        {
            ApplyPasswords();
            Debug.Log("Android keystore password auto-apply enabled.");
            return;
        }

        ClearPasswords();
        Debug.Log("Android keystore password auto-apply disabled.");
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleAutoApplyValidate()
    {
        Menu.SetChecked(MenuPath, EditorPrefs.GetBool(EnabledKey, false));
        return true;
    }

    private static void ApplyPasswordsIfEnabled()
    {
        if (!EditorPrefs.GetBool(EnabledKey, false))
        {
            return;
        }

        ApplyPasswords();
    }

    private static void ApplyPasswords()
    {
        PlayerSettings.Android.keystorePass = KeystorePassword;
        PlayerSettings.Android.keyaliasPass = KeyAliasPassword;
    }

    private static void ClearPasswords()
    {
        PlayerSettings.Android.keystorePass = string.Empty;
        PlayerSettings.Android.keyaliasPass = string.Empty;
    }
}