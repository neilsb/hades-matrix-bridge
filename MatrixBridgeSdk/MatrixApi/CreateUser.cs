using MatrixBridgeSdk.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace MatrixBridgeSdk
{
    public partial class MatrixBridge
    {
        public async Task<bool> CreateUser(RemoteUser remoteUser)
        {
            if(await UserExists(remoteUser))
            {
                _database.GetCollection<MatrixUser>().Insert(new MatrixUser()
                {
                    UserId = $"@{Constants.UserPrefix}{remoteUser.PuppetId}_{remoteUser.UserId}:{Domain}",
                    PuppetId = remoteUser.PuppetId
                });
                return true;
            }

            // Construct the registration URL with the AS token.
            var registrationUrl = $"{_matrixServerUrl}/_matrix/client/r0/register?access_token={_accessToken}";

            // Prepare the registration payload.
            var registrationPayload = new
            {
                // Specify that this registration comes from an Application Service.
                type = "m.login.application_service",
                username = $"{Constants.UserPrefix}{remoteUser.PuppetId}_{remoteUser.UserId}",
                displayname = remoteUser.Name,
                inhibit_login = true
            };

            // Send the registration request.
            var response = await _httpClient.PostAsJsonAsync(registrationUrl, registrationPayload);

            if (response.IsSuccessStatusCode)
            {
                // Handle a successful registration.
                _logger.LogInformation("User {Username} successfully created!", $"@{Constants.UserPrefix}{remoteUser.PuppetId}");

                var newuser = new MatrixUser()
                {
                    UserId = $"@{Constants.UserPrefix}{remoteUser.PuppetId}_{remoteUser.UserId}:{Domain}",
                    PuppetId = remoteUser.PuppetId
                };

                // TODO - Extract Username
                _database.GetCollection<MatrixUser>().Insert(newuser);

                return true;
            }
            else
            {
                // Handle error responses.
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Registration failed: {ErrorContent}", errorContent);
                return false;
            }
        }

        private async Task<bool> UserExists(RemoteUser user)
        {
            string requestUrl = $"{_matrixServerUrl}/_matrix/client/v3/profile/@{Constants.UserPrefix}{user.PuppetId}_{user.UserId}:{Domain}";
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.SendAsync(request);

            _logger.LogInformation("Check if user '{UserUserId}' exists: {ResponseStatusCode}", user.UserId, response.StatusCode);

            return response.IsSuccessStatusCode;
        }
    }
}
