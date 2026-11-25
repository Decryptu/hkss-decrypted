using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HKSS.HollowKnight.ModApi
{
    /// <summary>
    /// Additively loads scenes requested by ItemChanger / HK client preloaders on startup,
    /// detaches the requested prefab GameObjects as inactive DontDestroyOnLoad objects,
    /// and unloads the additive scenes.
    /// </summary>
    public static class ScenePreloader
    {
        public static IEnumerator PreloadAsync(
            IEnumerable<(string sceneName, string objectPath)> requests,
            ManualLogSource? log,
            Action<Dictionary<string, Dictionary<string, GameObject>>> onComplete)
        {
            var byScene = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var (sceneName, objectPath) in requests)
            {
                if (string.IsNullOrEmpty(sceneName) || string.IsNullOrEmpty(objectPath)) continue;
                if (!byScene.TryGetValue(sceneName, out var paths))
                {
                    paths = new HashSet<string>(StringComparer.Ordinal);
                    byScene[sceneName] = paths;
                }
                paths.Add(objectPath);
            }

            var result = new Dictionary<string, Dictionary<string, GameObject>>(StringComparer.Ordinal);
            foreach (var kv in byScene)
            {
                string sceneName = kv.Key;
                var found = new Dictionary<string, GameObject>(StringComparer.Ordinal);
                result[sceneName] = found;

                AsyncOperation? loadOp = null;
                try
                {
                    loadOp = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                }
                catch (Exception ex)
                {
                    log?.LogError($"[Preloader] Failed to start loading scene '{sceneName}': {ex.Message}");
                }

                if (loadOp == null) continue;
                while (!loadOp.isDone) yield return null;

                Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName);
                if (scene.IsValid() && scene.isLoaded)
                {
                    GameObject[] roots = scene.GetRootGameObjects();
                    // Deactivate roots immediately so scene scripts/audio don't run while preloading.
                    foreach (GameObject root in roots)
                    {
                        if (root != null) root.SetActive(false);
                    }

                    foreach (string path in kv.Value)
                    {
                        GameObject? target = FindInRoots(roots, path);
                        if (target == null)
                        {
                            log?.LogWarning($"[Preloader] Could not find '{path}' in scene '{sceneName}'.");
                            continue;
                        }

                        target.SetActive(false);
                        GameObject clone = UnityEngine.Object.Instantiate(target);
                        clone.name = target.name;
                        clone.SetActive(false);
                        UnityEngine.Object.DontDestroyOnLoad(clone);
                        found[path] = clone;
                    }

                    AsyncOperation? unloadOp = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
                    if (unloadOp != null)
                    {
                        while (!unloadOp.isDone) yield return null;
                    }
                }
            }

            log?.LogInfo($"[Preloader] Preloaded {result.Count} scenes.");
            onComplete(result);
        }

        private static GameObject? FindInRoots(GameObject[] roots, string path)
        {
            int slash = path.IndexOf('/');
            string rootName = slash < 0 ? path : path.Substring(0, slash);
            string? childPath = slash < 0 ? null : path.Substring(slash + 1);

            foreach (GameObject root in roots)
            {
                if (root == null || root.name != rootName) continue;
                if (childPath == null) return root;
                Transform? child = root.transform.Find(childPath);
                if (child != null) return child.gameObject;
            }
            return null;
        }
    }
}
