using System;
using UnityEngine;

namespace PM.horizOn.Cloud.Base
{
    /// <summary>
    /// Base class for all services in the horizOn SDK.
    /// Provides singleton pattern for non-MonoBehaviour services.
    /// </summary>
    /// <typeparam name="T">The service type</typeparam>
    public abstract class BaseService<T> where T : BaseService<T>, new()
    {
        // Singleton instance
        private static T _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the singleton instance of the service.
        /// </summary>
        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new T();
                            _instance.OnInit();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Virtual method for subclasses to override for custom initialization.
        /// Called automatically when the instance is first created.
        /// </summary>
        protected virtual void OnInit()
        {
            // Override in subclasses for custom initialization
        }

        /// <summary>
        /// Resets the singleton instance. Use with caution, primarily for testing.
        /// </summary>
        public static void ResetInstance()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }
    }
}
