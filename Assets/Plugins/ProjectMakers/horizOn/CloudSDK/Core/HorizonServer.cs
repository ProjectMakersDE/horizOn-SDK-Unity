using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using PM.horizOn.Cloud.Enums;
using PM.horizOn.Cloud.Service;

namespace PM.horizOn.Cloud.Core
{
    /// <summary>
    /// Main server connection class for horizOn SDK.
    /// For single-host configs (loadbalancer), connects directly without ping.
    /// For multi-host configs, selects the fastest host via ping.
    /// </summary>
    public class HorizonServer
    {
        private HorizonConfig _config;
        private string _activeHost;
        private ConnectionStatus _status = ConnectionStatus.Disconnected;
        private Dictionary<string, float> _hostPingResults = new Dictionary<string, float>();

        /// <summary>
        /// Get the current connection status.
        /// </summary>
        public ConnectionStatus Status => _status;

        /// <summary>
        /// Get the active host URL.
        /// </summary>
        public string ActiveHost => _activeHost;

        /// <summary>
        /// Get whether the server is currently connected.
        /// </summary>
        public bool IsConnected => _status == ConnectionStatus.Connected;

        /// <summary>
        /// Connect to the horizOn backend.
        /// Single host: connects directly (loadbalancer mode).
        /// Multiple hosts: pings all and selects the one with the lowest latency.
        /// </summary>
        /// <returns>True if connection succeeded, false otherwise</returns>
        public async Task<bool> Connect()
        {
            try
            {
                _status = ConnectionStatus.Connecting;
                EventService.Instance?.Publish(EventKeys.ConnectionStarted, new ConnectionEventData
                {
                    Status = _status
                });

                HorizonApp.Log?.Info("Starting connection to horizOn servers...");

                // Load configuration
                _config = HorizonConfig.Load();
                if (_config == null || !_config.IsValid())
                {
                    _status = ConnectionStatus.Failed;
                    HorizonApp.Log?.Error("Failed to load or validate configuration");
                    EventService.Instance?.Publish(EventKeys.ConnectionFailed, new ConnectionEventData
                    {
                        Status = _status,
                        Error = "Invalid configuration"
                    });
                    return false;
                }

                // Initialize network service
                NetworkService.Instance?.Initialize(_config);

                // Single host: skip ping, connect directly (loadbalancer handles routing)
                if (_config.Hosts.Length == 1)
                {
                    _activeHost = _config.Hosts[0];
                    NetworkService.Instance?.SetActiveHost(_activeHost);

                    _status = ConnectionStatus.Connected;
                    HorizonApp.Log?.Info($"Connected to {_activeHost} (single host, no ping)");

                    EventService.Instance?.Publish(EventKeys.ConnectionSuccess, new ConnectionEventData
                    {
                        Status = _status,
                        ActiveHost = _activeHost
                    });

                    return true;
                }

                // Ping all hosts
                await PingAllHosts();

                // Select best host
                if (_hostPingResults.Count == 0)
                {
                    _status = ConnectionStatus.Failed;
                    HorizonApp.Log?.Error("No hosts responded to ping");
                    EventService.Instance?.Publish(EventKeys.ConnectionFailed, new ConnectionEventData
                    {
                        Status = _status,
                        Error = "No hosts available"
                    });
                    return false;
                }

                // Sort by ping time and select the best
                var bestHost = _hostPingResults.OrderBy(kvp => kvp.Value).First();
                _activeHost = bestHost.Key;

                NetworkService.Instance?.SetActiveHost(_activeHost);

                _status = ConnectionStatus.Connected;
                HorizonApp.Log?.Info($"Connected to {_activeHost} (ping: {bestHost.Value:F0}ms)");

                EventService.Instance?.Publish(EventKeys.HostSelected, new HostSelectedData
                {
                    Host = _activeHost,
                    PingMs = bestHost.Value
                });

                EventService.Instance?.Publish(EventKeys.ConnectionSuccess, new ConnectionEventData
                {
                    Status = _status,
                    ActiveHost = _activeHost
                });

                return true;
            }
            catch (Exception e)
            {
                _status = ConnectionStatus.Failed;
                HorizonApp.Log?.Error($"Connection failed: {e.Message}");
                EventService.Instance?.Publish(EventKeys.ConnectionFailed, new ConnectionEventData
                {
                    Status = _status,
                    Error = e.Message
                });
                return false;
            }
        }

        /// <summary>
        /// Reconnect to the best available host.
        /// Called automatically on request failure (fallback strategy).
        /// </summary>
        public async Task<bool> Reconnect()
        {
            HorizonApp.Log.Warning("Connection lost. Attempting to reconnect...");
            _status = ConnectionStatus.Reconnecting;

            EventService.Instance?.Publish(EventKeys.ConnectionLost, new ConnectionEventData
            {
                Status = _status,
                ActiveHost = _activeHost
            });

            // Re-ping and reconnect
            return await Connect();
        }

        /// <summary>
        /// Ping all configured hosts and record their response times.
        /// Pings each host 3 times and stores the minimum (best) ping time.
        /// </summary>
        private async Task PingAllHosts()
        {
            _hostPingResults.Clear();

            // Ping each host 3 times
            const int pingAttempts = 3;
            var hostMinPings = new Dictionary<string, float>();

            foreach (string host in _config.Hosts)
            {
                for (int i = 0; i < pingAttempts; i++)
                {
                    var result = await PingHost(host);

                    if (result.Success)
                    {
                        // Keep the minimum (best) ping time
                        if (!hostMinPings.ContainsKey(result.Host) || result.PingMs < hostMinPings[result.Host]) 
                            hostMinPings[result.Host] = result.PingMs;
                        
                        HorizonApp.Log.Info($"Host {result.Host}: {result.PingMs:F0}ms");
                    }
                    else
                    {
                        HorizonApp.Log.Warning($"Host {result.Host}: Failed ({result.Error})");
                    }
                }
            }

            // Store the minimum ping results
            _hostPingResults = hostMinPings;

            EventService.Instance?.Publish(EventKeys.HostPingComplete, new HostPingCompleteData
            {
                Results = _hostPingResults
            });
        }

        /// <summary>
        /// Ping a single host and measure response time.
        /// Uses the /actuator/health endpoint and checks for "UP" status.
        /// </summary>
        private async Task<PingResult> PingHost(string host)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                // Use the actuator health endpoint for ping
                string pingUrl = $"{host}/actuator/health";

                using UnityWebRequest request = UnityWebRequest.Get(pingUrl);
                request.timeout = _config.ConnectionTimeoutSeconds;

                // Send request
                var operation = request.SendWebRequest();

                // Wait for completion
                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                stopwatch.Stop();

                // Check if successful
                if (request.result == UnityWebRequest.Result.Success)
                {
                    // Parse the response to check for "UP" status
                    string responseText = request.downloadHandler.text;

                    // Check if response contains "status":"UP"
                    if (responseText.Contains("\"status\":\"UP\""))
                    {
                        return new PingResult
                        {
                            Host = host,
                            Success = true,
                            PingMs = (float)stopwatch.Elapsed.TotalMilliseconds
                        };
                    }
                    else
                    {
                        return new PingResult
                        {
                            Host = host,
                            Success = false,
                            Error = "Health check returned non-UP status"
                        };
                    }
                }
                else
                {
                    return new PingResult
                    {
                        Host = host,
                        Success = false,
                        Error = request.error
                    };
                }
            }
            catch (Exception e)
            {
                stopwatch.Stop();
                return new PingResult
                {
                    Host = host,
                    Success = false,
                    Error = e.Message
                };
            }
        }

        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public void Disconnect()
        {
            _activeHost = null;
            _status = ConnectionStatus.Disconnected;
            _hostPingResults.Clear();

            NetworkService.Instance.SetActiveHost(null);
            NetworkService.Instance.ClearSessionToken();

            HorizonApp.Log.Info("Disconnected from horizOn server");
        }

        /// <summary>
        /// Get ping results for all hosts.
        /// </summary>
        public IReadOnlyDictionary<string, float> GetHostPingResults()
        {
            return _hostPingResults;
        }
    }

    /// <summary>
    /// Result of pinging a host.
    /// </summary>
    internal class PingResult
    {
        public string Host;
        public bool Success;
        public float PingMs;
        public string Error;
    }

    /// <summary>
    /// Connection event data.
    /// </summary>
    public class ConnectionEventData
    {
        public ConnectionStatus Status;
        public string ActiveHost;
        public string Error;
    }

    /// <summary>
    /// Host selected event data.
    /// </summary>
    public class HostSelectedData
    {
        public string Host;
        public float PingMs;
    }

    /// <summary>
    /// Host ping complete event data.
    /// </summary>
    public class HostPingCompleteData
    {
        public Dictionary<string, float> Results;
    }
}
