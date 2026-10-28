#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Menu: Gra -> Uzupełnij ID zapisu. Wpisuje nazwę prefabu do każdego SaveableObject.
public static class SaveIdTool
{
    [MenuItem("Gra/Uzupełnij ID zapisu")]
    static void FillIds()
    {
        int changed = 0;
        Dictionary<string, string> seen = new Dictionary<string, string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            SaveableObject save = prefab != null ? prefab.GetComponent<SaveableObject>() : null;
            if (save == null) continue;

            if (seen.TryGetValue(prefab.name, out string otherPath))
                Debug.LogError($"Dwa zapisywane prefaby o tej samej nazwie '{prefab.name}': {path} i {otherPath}. Zmień nazwę jednego z nich.");
            seen[prefab.name] = path;

            if (save.prefabId == prefab.name) continue;
            save.prefabId = prefab.name;
            EditorUtility.SetDirty(prefab);
            changed++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"ID zapisu: uzupełniono {changed} prefabów, wszystkich zapisywanych: {seen.Count}");
    }
}
#endif