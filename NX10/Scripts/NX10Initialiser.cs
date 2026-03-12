using UnityEngine;

namespace NX10
{
    public class NX10Initialiser
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void OnBeforeSceneLoad()
        {
            if (Object.FindAnyObjectByType<NX10Manager>() == null)
            {
                GameObject prefab = Resources.Load<GameObject>("NX10_Manager");
                GameObject instance = Object.Instantiate(prefab);

                instance.hideFlags = HideFlags.HideInHierarchy; 
            }
        }
    }
}

