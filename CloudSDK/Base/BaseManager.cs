using System;
using System.Collections.Generic;
using UnityEngine;
using PM.horizOn.Cloud.Service;
using PM.horizOn.Cloud.Enums;

namespace PM.horizOn.Cloud.Base
{
    /// <summary>
    /// Base class for all managers in the horizOn SDK.
    /// Provides singleton pattern and event registration system with lazy initialization.
    /// </summary>
    /// <typeparam name="T">The manager type</typeparam>
    public abstract class BaseManager<T> : MonoBehaviour, IManager where T : BaseManager<T>
    {
        // Singleton instance with lazy initialization
        private static T _instance;
        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    // Try to find existing instance in scene
                    _instance = FindFirstObjectByType<T>();

                    // If not found, create a new GameObject with the manager component
                    if (_instance == null)
                    {
                        GameObject managerObj = new GameObject($"[{typeof(T).Name}]");
                        _instance = managerObj.AddComponent<T>();
                        DontDestroyOnLoad(managerObj);

                        // Initialize the manager
                        _instance.Init();
                    }
                }
                return _instance;
            }
        }

        // Event registration tracking
        private List<EventRegistration> _eventRegistrations = new List<EventRegistration>();

        /// <summary>
        /// Initialize the manager instance.
        /// </summary>
        /// <returns>True if initialization succeeded, false otherwise</returns>
        public virtual bool Init()
        {
            try
            {
                // Singleton enforcement
                if (_instance != null && _instance != this)
                {
                    Destroy(this.gameObject);
                    return true;
                }

                _instance = (T)this;

                // Call virtual initialization method
                OnInit();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not initialize {GetType().Name}: {e.Message}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Virtual method for subclasses to override for custom initialization.
        /// </summary>
        protected virtual void OnInit()
        {
            // Override in subclasses for custom initialization
        }

        /// <summary>
        /// Register for Unity lifecycle.
        /// </summary>
        protected virtual void OnEnable()
        {
            RegisterEvents();
        }

        /// <summary>
        /// Unregister from Unity lifecycle.
        /// </summary>
        protected virtual void OnDisable()
        {
            UnregisterEvents();
        }

        /// <summary>
        /// Virtual method for subclasses to register events.
        /// </summary>
        protected virtual void RegisterEvents()
        {
            // Override in subclasses to register events
        }

        /// <summary>
        /// Virtual method for subclasses to unregister events.
        /// </summary>
        protected virtual void UnregisterEvents()
        {
            // Automatically unregister all tracked events
            foreach (var registration in _eventRegistrations)
            {
                registration.Unregister();
            }
            _eventRegistrations.Clear();
        }

        /// <summary>
        /// Register an event and track it for automatic cleanup.
        /// </summary>
        /// <typeparam name="TEventData">The event data type</typeparam>
        /// <param name="eventKey">The event key</param>
        /// <param name="handler">The event handler</param>
        protected void RegisterEvent<TEventData>(EventKeys eventKey, Action<TEventData> handler)
        {
            if (EventService.Instance == null)
            {
                Debug.LogWarning($"{GetType().Name}: Cannot register event {eventKey}, EventService not initialized");
                return;
            }

            EventService.Instance.Subscribe(eventKey, handler);

            // Track for automatic cleanup
            _eventRegistrations.Add(new EventRegistration
            {
                EventKey = eventKey,
                Unregister = () => EventService.Instance?.Unsubscribe(eventKey, handler)
            });
        }

        /// <summary>
        /// Helper struct to track event registrations.
        /// </summary>
        private struct EventRegistration
        {
            public EventKeys EventKey;
            public Action Unregister;
        }
    }
}