using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using MatrixBridgeSdk.Models;
using Microsoft.Extensions.Logging;

namespace MatrixBridgeSdk
{
    public enum PresenceState
    {
        Online,
        Offline,
        Unavailable
    }

    public partial class MatrixBridge
    {
        public async Task SetPresenceAsync(RemoteUser user, PresenceState presence, string? statusMessage = null)
        {
            try
            {

                _logger.LogInformation($"Setting presence for {user.UserId} to {presence}. (Msg: {statusMessage})");

                var requestBody = new
                {
                    presence = presence.ToString().ToLower(),
                    status_msg = statusMessage
                };

                string requestUrl = $"{_matrixServerUrl}/_matrix/client/v3/presence/{user.UserId}/status";
                var request = new HttpRequestMessage(HttpMethod.Put, requestUrl);
                request.Content = JsonContent.Create(requestBody);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                var content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PutAsync($"/_matrix/client/v3/presence/{user.UserId}/status", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning($"Set presence failed for {user.UserId} :: {response.StatusCode} : {errorContent}");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error setting presence for {user.UserId}");
                throw;
            }
        }
    }
}