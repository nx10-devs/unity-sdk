using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace NX10
{
    public class DynamicEventSystemHandler : MonoBehaviour
    {
        private EventSystem myEventSystem;

        void Awake()
        {
            myEventSystem = GetComponent<EventSystem>();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            CheckForDuplicateEventSystems();
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            CheckForDuplicateEventSystems();
        }

        void CheckForDuplicateEventSystems()
        {
            EventSystem[] eventSystems = FindObjectsByType<EventSystem>();

            if (eventSystems.Length > 1)
            {
                foreach (EventSystem es in eventSystems)
                {
                    if (es != myEventSystem)
                    { 
                        myEventSystem.enabled = false; 

                        return;
                    }
                }
            }
            myEventSystem.enabled = true;
        }
    }
}
