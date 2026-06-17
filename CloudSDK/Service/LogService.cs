using System;
using UnityEngine;
using PM.horizOn.Cloud.Base;
using PM.horizOn.Cloud.Core;
using PM.horizOn.Cloud.Enums;
using LogType = PM.horizOn.Cloud.Enums.LogType;

namespace PM.horizOn.Cloud.Service
{
    /// <summary>
    /// Centralized logging service for the horizOn SDK.
    /// Provides consistent logging with optional event publishing.
    /// </summary>
    public class LogService : BaseService<LogService>, IService
    {
        private HorizonConfig _config;
        
        private bool _enableEventPublishing = true;
        private bool _enableUnityLogging = true;

        /// <summary>
        /// Enable or disable event publishing for log messages.
        /// When enabled, log messages will publish events that can be subscribed to.
        /// </summary>
        public bool EnableEventPublishing
        {
            get => _enableEventPublishing;
            set => _enableEventPublishing = value;
        }

        /// <summary>
        /// Enable or disable Unity console logging.
        /// When disabled, log messages will only publish events without console output.
        /// </summary>
        public bool EnableUnityLogging
        {
            get => _enableUnityLogging;
            set => _enableUnityLogging = value;
        }

        /// <summary>
        /// Initialize the network service with configuration.
        /// </summary>
        public void Initialize(HorizonConfig config)
        {
            _config = config;
            
            if (config == null || !config.IsValid()) 
                Error("NetworkService initialized with invalid configuration");
        }

        /// <summary>
        /// Log an informational message.
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="context">Optional Unity object context</param>
        public void Info(string message, UnityEngine.Object context = null)
        {
            if(_config.LogLevel > LogType.INFO) return;
            
            if (_enableUnityLogging)
            {
                Debug.Log($"[horizOn] {message}", context);
            }

            if (_enableEventPublishing && EventService.Instance != null)
            {
                EventService.Instance.Publish(EventKeys.ErrorOccurred, new LogEventData
                {
                    Level = LogType.INFO,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Log a warning message.
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="context">Optional Unity object context</param>
        public void Warning(string message, UnityEngine.Object context = null)
        {
            if(_config.LogLevel > LogType.WARN) return;
            
            if (_enableUnityLogging)
            {
                Debug.LogWarning($"[horizOn] {message}", context);
            }

            if (_enableEventPublishing && EventService.Instance != null)
            {
                EventService.Instance.Publish(EventKeys.WarningOccurred, new LogEventData
                {
                    Level = LogType.WARN,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Log an error message.
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="context">Optional Unity object context</param>
        public void Error(string message, UnityEngine.Object context = null)
        {
            if(_config.LogLevel > LogType.ERROR) return;
            
            if (_enableUnityLogging)
            {
                Debug.LogError($"[horizOn] {message}", context);
            }

            if (_enableEventPublishing && EventService.Instance != null)
            {
                EventService.Instance.Publish(EventKeys.ErrorOccurred, new LogEventData
                {
                    Level = LogType.ERROR,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Log an exception.
        /// </summary>
        /// <param name="exception">The exception to log</param>
        /// <param name="context">Optional Unity object context</param>
        public void Exception(Exception exception, UnityEngine.Object context = null)
        {
            if (_enableUnityLogging)
            {
                Debug.LogException(exception, context);
            }

            if (_enableEventPublishing && EventService.Instance != null)
            {
                EventService.Instance.Publish(EventKeys.ErrorOccurred, new LogEventData
                {
                    Level = LogType.ERROR,
                    Message = $"{exception.GetType().Name}: {exception.Message}",
                    Exception = exception,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Log a message with a specific log level.
        /// </summary>
        /// <param name="level">The log level</param>
        /// <param name="message">The message to log</param>
        /// <param name="context">Optional Unity object context</param>
        public void Log(LogType level, string message, UnityEngine.Object context = null)
        {
            switch (level)
            {
                case LogType.INFO:
                    Info(message, context);
                    break;
                case LogType.WARN:
                    Warning(message, context);
                    break;
                case LogType.ERROR:
                    Error(message, context);
                    break;
                default:
                    Info(message, context);
                    break;
            }
        }
    }

    /// <summary>
    /// Event data for log messages.
    /// </summary>
    public class LogEventData
    {
        public LogType Level { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
