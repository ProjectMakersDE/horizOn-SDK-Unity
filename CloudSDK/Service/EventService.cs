using System;
using System.Collections.Generic;
using UnityEngine;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Enums;

namespace PM.horizOn.Cloud.Service
{
    /// <summary>
    /// Event service for SDK-wide event management.
    /// Provides type-safe event subscription and publication with automatic replay for late subscribers.
    /// </summary>
    public class EventService : BaseService<EventService>, IService
    {
        // Event storage: EventKey -> List of delegates
        private readonly Dictionary<EventKeys, Delegate> _events = new Dictionary<EventKeys, Delegate>();

        // Event data cache for late subscribers (using WeakReference to prevent memory leaks)
        private readonly Dictionary<EventKeys, WeakReference> _eventDataCache = new Dictionary<EventKeys, WeakReference>();

        // Lock for thread safety
        private readonly object _lock = new object();

        /// <summary>
        /// Subscribe to an event with a typed handler.
        /// If event data has been published before, the handler will be invoked immediately with cached data.
        /// </summary>
        /// <typeparam name="T">The event data type</typeparam>
        /// <param name="eventKey">The event key to subscribe to</param>
        /// <param name="handler">The event handler</param>
        public void Subscribe<T>(EventKeys eventKey, Action<T> handler)
        {
            if (handler == null)
            {
                Debug.LogWarning($"[EventService] Attempted to subscribe to {eventKey} with null handler");
                return;
            }

            lock (_lock)
            {
                // Add or combine delegate
                if (!_events.TryAdd(eventKey, handler))
                {
                    _events[eventKey] = Delegate.Combine(_events[eventKey], handler);
                }

                // Replay cached data for late subscribers
                if (_eventDataCache.TryGetValue(eventKey, out var weakRef))
                {
                    if (weakRef.IsAlive && weakRef.Target is T cachedData)
                    {
                        try
                        {
                            handler.Invoke(cachedData);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[EventService] Error replaying event {eventKey}: {e.Message}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Unsubscribe from an event.
        /// </summary>
        /// <typeparam name="T">The event data type</typeparam>
        /// <param name="eventKey">The event key to unsubscribe from</param>
        /// <param name="handler">The event handler to remove</param>
        public void Unsubscribe<T>(EventKeys eventKey, Action<T> handler)
        {
            if (handler == null)
                return;

            lock (_lock)
            {
                if (_events.ContainsKey(eventKey))
                {
                    _events[eventKey] = Delegate.Remove(_events[eventKey], handler);

                    // Remove entry if no more subscribers
                    if (_events[eventKey] == null) 
                        _events.Remove(eventKey);
                }
            }
        }

        /// <summary>
        /// Publish an event to all subscribers.
        /// Event data is cached using WeakReference for late subscribers.
        /// </summary>
        /// <typeparam name="T">The event data type</typeparam>
        /// <param name="eventKey">The event key to publish</param>
        /// <param name="data">The event data</param>
        public void Publish<T>(EventKeys eventKey, T data)
        {
            lock (_lock)
            {
                // Cache the event data for late subscribers
                _eventDataCache[eventKey] = new WeakReference(data);

                // Invoke all subscribers
                if (_events.TryGetValue(eventKey, out var @event))
                {
                    var delegates = @event.GetInvocationList();
                    foreach (var del in delegates)
                    {
                        try
                        {
                            if (del is Action<T> typedHandler)
                                typedHandler.Invoke(data);
                            else
                                Debug.LogWarning($"[EventService] Type mismatch for event {eventKey}. Expected {typeof(T)}, got {del.GetType()}");
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[EventService] Error invoking event {eventKey}: {e.Message}\n{e.StackTrace}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Publish an event without data (for simple notifications).
        /// </summary>
        /// <param name="eventKey">The event key to publish</param>
        public void Publish(EventKeys eventKey)
        {
            Publish<object>(eventKey, null);
        }

        /// <summary>
        /// Clear all event subscriptions and cached data.
        /// Use with caution, primarily for testing or SDK shutdown.
        /// </summary>
        public void ClearAll()
        {
            lock (_lock)
            {
                _events.Clear();
                _eventDataCache.Clear();
            }
        }

        /// <summary>
        /// Clear all subscribers for a specific event.
        /// </summary>
        /// <param name="eventKey">The event key to clear</param>
        public void ClearEvent(EventKeys eventKey)
        {
            lock (_lock)
            {
                _events.Remove(eventKey);
                _eventDataCache.Remove(eventKey);
            }
        }

        /// <summary>
        /// Check if an event has any subscribers.
        /// </summary>
        /// <param name="eventKey">The event key to check</param>
        /// <returns>True if the event has subscribers, false otherwise</returns>
        public bool HasSubscribers(EventKeys eventKey)
        {
            lock (_lock)
                return _events.ContainsKey(eventKey) && _events[eventKey] != null;
        }

        /// <summary>
        /// Get the number of subscribers for an event.
        /// </summary>
        /// <param name="eventKey">The event key</param>
        /// <returns>The number of subscribers</returns>
        public int GetSubscriberCount(EventKeys eventKey)
        {
            lock (_lock)
            {
                if (_events.ContainsKey(eventKey) && _events[eventKey] != null)
                    return _events[eventKey].GetInvocationList().Length;
                
                return 0;
            }
        }
    }
}
