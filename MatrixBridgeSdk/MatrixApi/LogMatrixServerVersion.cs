using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MatrixBridgeSdk
{
    public partial class MatrixBridge
    {
        // Fetch and log the Matrix server version
        private async Task LogMatrixServerVersion()
        {
            try
            {
                var requestUrl = $"{_matrixServerUrl}/_matrix/client/versions";
                var response = await _httpClient.GetAsync(requestUrl);
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(body);
                    if (document.RootElement.TryGetProperty("server", out var serverInfo))
                    {
                        _logger.LogInformation($"Matrix server info: {serverInfo.ToString()}");
                    }
                    if (document.RootElement.TryGetProperty("versions", out var versions))
                    {
                        _logger.LogInformation($"Matrix server supported versions: {versions.ToString()}");
                    }
                    else
                    {
                        _logger.LogInformation($"Matrix server versions response: {body}");
                    }
                }
                else
                {
                    _logger.LogWarning($"Failed to get Matrix server version: {response.StatusCode} :: {body}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Exception while fetching Matrix server version: {ex.Message}");
            }
        }
    }

}