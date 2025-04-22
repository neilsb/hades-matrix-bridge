using System;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MatrixBridgeSdk.Helpers;

namespace MatrixBridgeSdk
{
    public partial class MatrixBridge
    {
        public async Task SetUserPowerLevelAsync(string roomId, string matrixUserId, int powerLevel = 100)
        {
            
            if (!MatrixIdValidator.IsValidMatrixId(matrixUserId))
            {
                throw new ArgumentException("Invalid Matrix ID format.");
            }

            string requestUrl = $"{_matrixServerUrl}/_matrix/client/v3/rooms/{roomId}/state/m.room.power_levels";
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            // Step 1: Get current power levels
            var response = await _httpClient.SendAsync(request);

            var body = await response.Content.ReadAsStringAsync();

            JsonObject powerLevels = new JsonObject();

            if (response.IsSuccessStatusCode)
            {
                powerLevels = JsonNode.Parse(body)?.AsObject() ?? new JsonObject();
            }
            else
            {
                _logger.LogInformation($"Failed to get current power levels. Status: {response.StatusCode}");
                return;
            }

            // Step 2: Update the target user's power level
            if (!powerLevels.ContainsKey("users"))
            {
                powerLevels["users"] = new JsonObject();
            }

            if (powerLevels["users"] is JsonObject users)
            {
                users[matrixUserId] = powerLevel;
            }

            // Step 3: Send the updated power levels back to Matrix
            request = new HttpRequestMessage(HttpMethod.Put, requestUrl);
            request.Content = JsonContent.Create(powerLevels);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully set power level of {MatrixUserId} to {PowerLevel} in room {RoomId}", matrixUserId, powerLevel, roomId);
            }
            else
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to update power level. Status: {ResponseStatusCode}, Response: {ErrorContent}", response.StatusCode, errorContent);
            }
        }
    }
}
