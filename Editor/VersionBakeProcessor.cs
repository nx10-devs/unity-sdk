using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

namespace NX10
{
    public class VersionBakeProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            const string packageId = "com.nx10.sdk";
            const string assetName = "NX10PackageVersion.asset";

            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{packageId}");
            if (packageInfo == null)
            {
                Debug.LogError($"[NX10] Could not find package {packageId} to bake version.");
                return;
            }

            string resourcesPath = $"Packages/{packageId}/Runtime/Resources";
            string absoluteResourcesPath = Path.GetFullPath(resourcesPath);

            if (!Directory.Exists(absoluteResourcesPath))
            {
                Directory.CreateDirectory(absoluteResourcesPath);
                AssetDatabase.Refresh();
            }

            string assetPath = $"{resourcesPath}/{assetName}";
            var data = AssetDatabase.LoadAssetAtPath<PackageRuntimeData>(assetPath);

            if (data == null)
            {
                data = ScriptableObject.CreateInstance<PackageRuntimeData>();
                AssetDatabase.CreateAsset(data, assetPath);
            }

            data.version = packageInfo.version;

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            Debug.Log($"[NX10] Successfully baked version {data.version} into {assetPath}");
        }
    }
}
