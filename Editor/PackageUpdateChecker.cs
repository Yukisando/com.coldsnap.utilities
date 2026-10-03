using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

/// <summary>
/// Shows which ColdSnap Utilities commit/version this project has installed and
/// whether GitHub has a newer one. Unity's Package Manager can't show that for Git
/// packages (its Version History only ever lists the installed entry), so this
/// asks GitHub directly. Also checks once per editor session and logs a warning
/// when the project is behind.
/// </summary>
[InitializeOnLoad]
public class PackageUpdateChecker : EditorWindow
{
	const string PackageName = "com.coldsnap.utilities";
	const string GitUrl = "https://github.com/Yukisando/com.coldsnap.utilities.git";
	const string ApiLatestCommit = "https://api.github.com/repos/Yukisando/com.coldsnap.utilities/commits/main";
	const string RawBase = "https://raw.githubusercontent.com/Yukisando/com.coldsnap.utilities/";
	const string SessionCheckedKey = "ColdSnap.PackageUpdateChecker.Checked";

#pragma warning disable 0649 //Filled by JsonUtility
	[Serializable] class CommitResponse { public string sha; public CommitDetail commit; }
	[Serializable] class CommitDetail { public string message; public CommitAuthor committer; }
	[Serializable] class CommitAuthor { public string date; }
	[Serializable] class PackageJson { public string version; }
#pragma warning restore 0649

	enum State { Idle, Checking, UpToDate, UpdateAvailable, NotGit, Failed }

	static State state = State.Idle;
	static string error;
	static string installedVersion;
	static string installedHash;
	static string latestHash;
	static string latestVersion;
	static string latestTitle;
	static string latestDate;
	static string latestChangelog;

	Vector2 changelogScroll;

	static PackageUpdateChecker()
	{
		if (SessionState.GetBool(SessionCheckedKey, false)) return;
		SessionState.SetBool(SessionCheckedKey, true);
		EditorApplication.delayCall += () => Check(true);
	}

	[MenuItem("ColdSnap/Package/Check for Updates")]
	static void Open()
	{
		var window = GetWindow<PackageUpdateChecker>(true, "ColdSnap Utilities", true);
		window.minSize = new Vector2(460, 360);
		Check(false);
	}

	static void Check(bool quiet)
	{
		if (state == State.Checking) return;

		var info = PackageInfo.FindForAssetPath("Packages/" + PackageName);
		installedVersion = info != null ? info.version : "?";
		installedHash = info != null && info.git != null ? info.git.hash : null;
		latestHash = latestVersion = latestTitle = latestDate = latestChangelog = error = null;

		if (string.IsNullOrEmpty(installedHash))
		{
			//Embedded/local copy (e.g. the package's own repo): nothing to compare.
			state = State.NotGit;
			Repaint_();
			return;
		}

		state = State.Checking;
		Repaint_();

		Get(ApiLatestCommit, json =>
		{
			var commit = JsonUtility.FromJson<CommitResponse>(json);
			if (commit == null || string.IsNullOrEmpty(commit.sha)) { Fail("Unexpected reply from GitHub.", quiet); return; }

			latestHash = commit.sha;
			latestTitle = commit.commit != null ? FirstLine(commit.commit.message) : "";
			latestDate = commit.commit != null && commit.commit.committer != null ? commit.commit.committer.date : "";
			state = latestHash == installedHash ? State.UpToDate : State.UpdateAvailable;

			if (state == State.UpdateAvailable && quiet)
				Debug.LogWarning($"ColdSnap Utilities update available: installed {installedVersion} ({Short(installedHash)}), latest {Short(latestHash)} \"{latestTitle}\". Open ColdSnap > Package > Check for Updates.");

			//Version and changelog of the latest commit, for the window.
			Get(RawBase + latestHash + "/package.json", pj =>
			{
				var parsed = JsonUtility.FromJson<PackageJson>(pj);
				latestVersion = parsed != null ? parsed.version : null;
				Repaint_();
			}, _ => { });
			Get(RawBase + latestHash + "/CHANGELOG.md", md => { latestChangelog = md; Repaint_(); }, _ => { });
			Repaint_();
		}, message => Fail(message, quiet));
	}

	static void Fail(string message, bool quiet)
	{
		state = State.Failed;
		error = message;
		if (!quiet) Debug.LogWarning($"ColdSnap Utilities update check failed: {message}");
		Repaint_();
	}

	void OnGUI()
	{
		EditorGUILayout.Space();
		GUILayout.Label("ColdSnap Utilities", EditorStyles.boldLabel);

		EditorGUILayout.LabelField("Installed", $"{installedVersion}   ({Short(installedHash) ?? "local"})");
		if (latestHash != null)
			EditorGUILayout.LabelField("Latest on GitHub", $"{latestVersion ?? "…"}   ({Short(latestHash)})");
		if (!string.IsNullOrEmpty(latestTitle))
			EditorGUILayout.LabelField("Latest change", $"{latestTitle}   {FormatDate(latestDate)}");

		EditorGUILayout.Space();
		switch (state)
		{
			case State.Checking:
				EditorGUILayout.HelpBox("Checking GitHub…", MessageType.None);
				break;
			case State.UpToDate:
				EditorGUILayout.HelpBox("You're on the latest version.", MessageType.Info);
				break;
			case State.UpdateAvailable:
				EditorGUILayout.HelpBox("A newer version is on GitHub.", MessageType.Warning);
				if (GUILayout.Button("Update to latest", GUILayout.Height(28)))
				{
					//Re-adding the Git URL makes Package Manager fetch the newest commit
					//and rewrite the hash in packages-lock.json.
					UnityEditor.PackageManager.Client.Add(GitUrl);
					state = State.Idle;
					Close();
				}
				break;
			case State.NotGit:
				EditorGUILayout.HelpBox("This copy isn't installed from Git (embedded or local package), so there's nothing to compare.", MessageType.Info);
				break;
			case State.Failed:
				EditorGUILayout.HelpBox($"Couldn't reach GitHub: {error}", MessageType.Error);
				break;
		}

		if (state != State.Checking && GUILayout.Button("Check again")) Check(false);

		if (!string.IsNullOrEmpty(latestChangelog))
		{
			EditorGUILayout.Space();
			GUILayout.Label("Changelog (latest)", EditorStyles.boldLabel);
			changelogScroll = EditorGUILayout.BeginScrollView(changelogScroll);
			EditorGUILayout.TextArea(latestChangelog, EditorStyles.wordWrappedLabel);
			EditorGUILayout.EndScrollView();
		}
	}

	static void Get(string url, Action<string> onSuccess, Action<string> onError)
	{
		var request = UnityWebRequest.Get(url);
		request.SetRequestHeader("User-Agent", "ColdSnap-Utilities-Unity"); //GitHub API requires one
		var operation = request.SendWebRequest();
		operation.completed += _ =>
		{
			bool ok = string.IsNullOrEmpty(request.error) && request.responseCode == 200;
			string body = ok ? request.downloadHandler.text : null;
			string message = ok ? null : $"{request.responseCode} {request.error}";
			request.Dispose();
			if (ok) onSuccess(body);
			else onError(message);
		};
	}

	static void Repaint_()
	{
		foreach (var window in Resources.FindObjectsOfTypeAll<PackageUpdateChecker>())
			window.Repaint();
	}

	static string Short(string hash) => string.IsNullOrEmpty(hash) ? null : hash.Substring(0, Math.Min(7, hash.Length));

	static string FirstLine(string text)
	{
		if (string.IsNullOrEmpty(text)) return "";
		int newline = text.IndexOf('\n');
		return newline < 0 ? text : text.Substring(0, newline).TrimEnd('\r');
	}

	static string FormatDate(string iso) =>
		DateTime.TryParse(iso, out var date) ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "";
}
