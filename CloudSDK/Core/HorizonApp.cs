using System;
using System.Collections.Generic;
using UnityEngine;
using PM.horizOn.Cloud.Service;
using PM.horizOn.Cloud.Enums;

namespace PM.horizOn.Cloud.Core
{
    /// <summary>
    /// Main application bootstrap class for horizOn SDK.
    /// Manages SDK initialization, service lifecycle, and manager registration.
    /// </summary>
    public class HorizonApp : MonoBehaviour
    {
        private static GameObject _sdkGameObject;

        private List<IManager> _managers = new List<IManager>();

        /// <summary>
        /// Get the singleton instance.
        /// </summary>
        public static HorizonApp Instance { get; private set; }

        /// <summary>
        /// Get whether the SDK is initialized.
        /// </summary>
        public static bool IsInitialized { get; private set; } = false;

        /// <summary>
        /// Access to the Log service.
        /// </summary>
        public static LogService Log => LogService.Instance;

        /// <summary>
        /// Access to the Event service.
        /// </summary>
        public static EventService Events => EventService.Instance;

        /// <summary>
        /// Access to the Network service.
        /// </summary>
        public static NetworkService Network => NetworkService.Instance;

        /// <summary>
        /// Initialize the horizOn SDK.
        /// This should be called at the start of your application.
        /// </summary>
        /// <returns>True if initialization succeeded, false otherwise</returns>
        public static bool Initialize()
        {
            if (IsInitialized)
            {
                Log.Warning("HorizonApp already initialized");
                return true;
            }

            try
            {
                // Create SDK GameObject
                _sdkGameObject = new GameObject("[horizOn SDK]");
                DontDestroyOnLoad(_sdkGameObject);

                // Add HorizonApp component
                Instance = _sdkGameObject.AddComponent<HorizonApp>();

                var log = LogService.Instance;
                log.Initialize(HorizonConfig.Load());
                
                var events = EventService.Instance;
                var network = NetworkService.Instance;
                
                log.Info("=== horizOn SDK Initialization Started ===");

                // Services are now ready
                events.Publish(EventKeys.ServiceInitialized, "EventService");
                events.Publish(EventKeys.ServiceInitialized, "LogService");
                events.Publish(EventKeys.ServiceInitialized, "NetworkService");

                IsInitialized = true;

                log.Info("=== horizOn SDK Initialization Complete ===");
                events.Publish(EventKeys.SDKInitialized, DateTime.UtcNow);

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[horizOn] SDK initialization failed: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Register a manager with the SDK.
        /// Managers will be automatically initialized.
        /// </summary>
        /// <param name="manager">The manager to register</param>
        /// <returns>True if registration succeeded, false otherwise</returns>
        public static bool RegisterManager(IManager manager)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[horizOn] Cannot register manager before SDK initialization. Call HorizonApp.Initialize() first.");
                return false;
            }

            if (Instance._managers.Contains(manager))
            {
                Log.Warning($"Manager {manager.GetType().Name} already registered");
                return true;
            }

            try
            {
                // Initialize the manager
                bool success = manager.Init();

                if (success)
                {
                    Instance._managers.Add(manager);
                    Log.Info($"Manager registered: {manager.GetType().Name}");
                    Events.Publish(EventKeys.ManagerInitialized, manager.GetType().Name);
                }
                else
                {
                    Log.Error($"Manager initialization failed: {manager.GetType().Name}");
                }

                return success;
            }
            catch (Exception e)
            {
                Log.Error($"Failed to register manager {manager.GetType().Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get a registered manager by type.
        /// </summary>
        /// <typeparam name="T">The manager type</typeparam>
        /// <returns>The manager instance, or null if not found</returns>
        public static T GetManager<T>() where T : class, IManager
        {
            if (!IsInitialized)
            {
                Debug.LogError("[horizOn] Cannot get manager before SDK initialization");
                return null;
            }

            foreach (var manager in Instance._managers)
            {
                if (manager is T typedManager)
                {
                    return typedManager;
                }
            }

            return null;
        }

        /// <summary>
        /// Shutdown the SDK and cleanup resources.
        /// </summary>
        public static void Shutdown()
        {
            if (!IsInitialized)
            {
                return;
            }

            Log.Info("=== horizOn SDK Shutdown Started ===");
            Events.Publish(EventKeys.SDKShutdown, DateTime.UtcNow);

            // Clear managers
            Instance._managers.Clear();

            // Clear services
            EventService.Instance.ClearAll();

            // Destroy SDK GameObject
            if (_sdkGameObject != null)
            {
                Destroy(_sdkGameObject);
                _sdkGameObject = null;
            }

            Instance = null;
            IsInitialized = false;

            Debug.Log("[horizOn] SDK shutdown complete");
        }

        /// <summary>
        /// Unity lifecycle: Ensure singleton pattern.
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Unity lifecycle: Cleanup on application quit.
        /// </summary>
        private void OnApplicationQuit()
        {
            Shutdown();
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Editor-only: Create HorizonApp manager instances for testing.
        /// </summary>
        [ContextMenu("Create All Managers")]
        private void CreateAllManagers()
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("Initialize SDK first before creating managers");
                return;
            }

            // This will be expanded when managers are implemented
            Debug.Log("Manager creation will be implemented with specific manager classes");
        }
        #endif
    }
}
