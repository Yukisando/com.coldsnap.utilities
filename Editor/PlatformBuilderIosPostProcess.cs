#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

// Writes the Info.plist keys set in Platform Builder's App Store settings into every iOS export,
// whichever way the build was started (the window, a script, the command line). Compiled only
// while iOS is the active platform, which is also when Unity runs iOS post-process steps.
public class PlatformBuilderIosPostProcess : IPostprocessBuildWithReport
{
	public int callbackOrder => 100;

	public void OnPostprocessBuild(BuildReport report)
	{
		if (report.summary.platform != BuildTarget.iOS) return;

		PlatformBuilderSettings settings = PlatformBuilder.ReadSettingsFile();
		if (settings == null) return;

		string plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
		if (!File.Exists(plistPath)) return;

		PlistDocument plist = new PlistDocument();
		plist.ReadFromFile(plistPath);

		if (settings.iosDeclareNoEncryption)
			plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);

		if (!string.IsNullOrWhiteSpace(settings.iosTrackingUsageDescription))
			plist.root.SetString("NSUserTrackingUsageDescription", settings.iosTrackingUsageDescription.Trim());

		plist.WriteToFile(plistPath);
	}
}
#endif
