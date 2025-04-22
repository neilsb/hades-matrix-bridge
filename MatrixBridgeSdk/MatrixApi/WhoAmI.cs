using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MatrixBridgeSdk;

public partial class MatrixBridge
{
    public async Task<string>  WhoAmI()
    {
        string requestUrl = $"{_matrixServerUrl}/_matrix/client/r0/account/whoami";
        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        ;

        if (response.IsSuccessStatusCode)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("user_id", out var userId))
                {
                    return userId.GetString();
                }
                else
                {
                    _logger.LogWarning("Response does not contain 'user_id'.");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to parse 'user_id' from response: {ex.Message}");
                return string.Empty;
            }
        }
        else
        {
            _logger.LogWarning($"Error calling WhoAmI :: {response.StatusCode} :: {body} ");
            return String.Empty;
        }
    }
}


