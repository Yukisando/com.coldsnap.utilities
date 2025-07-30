using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ColdSnap.Utilities.Editor
{
    public class PlatformBuilder : EditorWindow
    {
        [System.Serializable]
        public class SceneInfo
        {
            public string path;
            public string name;
            public bool selected;
            public bool enabled;

            public SceneInfo(string path, string name, bool enabled = true)
            {
                this.path = path;
                this.name = name;
                this.enabled = enabled;
                this.selected = false;
            }
        }

        private List<SceneInfo> allScenes = new List<SceneInfo>();
        private Vector2 scrollPosition;
        private bool selectAll = false;

        [MenuItem("Tools/Platform Builder")]
        public static void ShowWindow()
        {
            GetWindow<PlatformBuilder>("Platform Builder");
        }

        void OnEnable()
        {
            RefreshSceneList();
            LoadScenePreferences();
        }

        void OnDisable()
        {
            SaveScenePreferences();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Scene Build Settings", EditorStyles.boldLabel);
            
            EditorGUILayout.Space();
            
            // Selection controls
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
            {
                SelectAllScenes(true);
            }
            if (GUILayout.Button("Select None"))
            {
                SelectAllScenes(false);
            }
            if (GUILayout.Button("Refresh"))
            {
                RefreshSceneList();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Scene list with sorting
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            for (int i = 0; i < allScenes.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                
                // Up/Down arrows for sorting
                EditorGUI.BeginDisabledGroup(i == 0);
                if (GUILayout.Button("↑", GUILayout.Width(25)))
                {
                    SwapScenes(i, i - 1);
                    SaveScenePreferences();
                }
                EditorGUI.EndDisabledGroup();
                
                EditorGUI.BeginDisabledGroup(i == allScenes.Count - 1);
                if (GUILayout.Button("↓", GUILayout.Width(25)))
                {
                    SwapScenes(i, i + 1);
                    SaveScenePreferences();
                }
                EditorGUI.EndDisabledGroup();
                
                // Scene selection checkbox
                bool newSelected = EditorGUILayout.Toggle(allScenes[i].selected, GUILayout.Width(20));
                if (newSelected != allScenes[i].selected)
                {
                    allScenes[i].selected = newSelected;
                    SaveScenePreferences();
                }
                
                // Scene name and path
                EditorGUILayout.LabelField($"{i}: {allScenes[i].name}", GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField(allScenes[i].path, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            // Build buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Build Selected Scenes"))
            {
                BuildSelectedScenes();
            }
            if (GUILayout.Button("Add Selected to Build Settings"))
            {
                AddSelectedToBuildSettings();
            }
            EditorGUILayout.EndHorizontal();
        }

        void RefreshSceneList()
        {
            // Store current selections before refreshing
            var currentSelections = allScenes.ToDictionary(s => s.path, s => s.selected);
            var currentOrder = allScenes.Select(s => s.path).ToList();
            
            allScenes.Clear();

            // Get all scenes from Assets
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
            var newScenes = new List<SceneInfo>();
            
            foreach (string guid in sceneGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                var sceneInfo = new SceneInfo(path, name);
                
                // Restore previous selection if it existed
                if (currentSelections.ContainsKey(path))
                {
                    sceneInfo.selected = currentSelections[path];
                }
                
                newScenes.Add(sceneInfo);
            }

            // Restore order if possible
            if (currentOrder.Count > 0)
            {
                var orderedScenes = new List<SceneInfo>();
                
                // Add scenes in the previous order
                foreach (string path in currentOrder)
                {
                    var scene = newScenes.FirstOrDefault(s => s.path == path);
                    if (scene != null)
                    {
                        orderedScenes.Add(scene);
                        newScenes.Remove(scene);
                    }
                }
                
                // Add any new scenes at the end
                orderedScenes.AddRange(newScenes);
                allScenes = orderedScenes;
            }
            else
            {
                allScenes = newScenes;
            }

            // Select the currently active scene if no previous selections
            if (!currentSelections.Any())
            {
                string activeScenePath = SceneManager.GetActiveScene().path;
                var activeScene = allScenes.FirstOrDefault(scene => scene.path == activeScenePath);
                if (activeScene != null)
                {
                    activeScene.selected = true;
                }
            }
        }

        void SwapScenes(int indexA, int indexB)
        {
            if (indexA < 0 || indexA >= allScenes.Count || indexB < 0 || indexB >= allScenes.Count)
                return;

            // Swap the scenes
            var temp = allScenes[indexA];
            allScenes[indexA] = allScenes[indexB];
            allScenes[indexB] = temp;

            // Repaint to update the UI
            Repaint();
        }

        void SelectAllScenes(bool select)
        {
            foreach (var scene in allScenes)
            {
                scene.selected = select;
            }
            SaveScenePreferences();
            Repaint();
        }

        void LoadScenePreferences()
        {
            string sceneData = EditorPrefs.GetString("PlatformBuilder_SceneData", "");
            if (string.IsNullOrEmpty(sceneData)) return;

            var sceneEntries = sceneData.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var loadedScenes = new List<SceneInfo>();
            
            foreach (var entry in sceneEntries)
            {
                var parts = entry.Split('|');
                if (parts.Length != 3) continue;

                string path = parts[0];
                string name = parts[1];
                bool isSelected = bool.Parse(parts[2]);

                // Only add if the scene still exists
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                {
                    var sceneInfo = new SceneInfo(path, name);
                    sceneInfo.selected = isSelected;
                    loadedScenes.Add(sceneInfo);
                }
            }

            if (loadedScenes.Count > 0)
            {
                allScenes = loadedScenes;
            }
        }

        void SaveScenePreferences()
        {
            var sceneData = string.Join(";", allScenes.Select(s => $"{s.path}|{s.name}|{s.selected}"));
            EditorPrefs.SetString("PlatformBuilder_SceneData", sceneData);
        }

        void BuildSelectedScenes()
        {
            var selectedScenes = allScenes.Where(s => s.selected).ToArray();
            if (selectedScenes.Length == 0)
            {
                EditorUtility.DisplayDialog("No Scenes Selected", "Please select at least one scene to build.", "OK");
                return;
            }

            // Implementation for building selected scenes would go here
            Debug.Log($"Building {selectedScenes.Length} selected scenes...");
            foreach (var scene in selectedScenes)
            {
                Debug.Log($"Would build: {scene.name} ({scene.path})");
            }
        }

        void AddSelectedToBuildSettings()
        {
            var selectedScenes = allScenes.Where(s => s.selected).ToArray();
            if (selectedScenes.Length == 0)
            {
                EditorUtility.DisplayDialog("No Scenes Selected", "Please select at least one scene to add to build settings.", "OK");
                return;
            }

            var buildScenes = new List<EditorBuildSettingsScene>();
            
            // Add selected scenes in the order they appear in our list
            foreach (var scene in selectedScenes)
            {
                buildScenes.Add(new EditorBuildSettingsScene(scene.path, true));
            }

            EditorBuildSettings.scenes = buildScenes.ToArray();
            Debug.Log($"Added {selectedScenes.Length} scenes to build settings in the specified order.");
        }
    }
}
