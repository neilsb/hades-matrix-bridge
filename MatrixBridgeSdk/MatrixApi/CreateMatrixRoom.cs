﻿using MatrixBridgeSdk.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MatrixBridgeSdk
{
    public partial class MatrixBridge
    {
        private async Task<string?> CreateMatrixRoomAsync(RemoteRoom remoteRoom, List<string> invite)
        {
            string requestUrl = $"{_matrixServerUrl}/_matrix/client/v3/createRoom";
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var inviteList = new List<string>();
            if (_puppets.TryGetValue(remoteRoom.PuppetId, out var puppet))
            {
                inviteList.Add(puppet.Owner);
            }
            if(invite != null)
            {
                foreach (var item in invite)
                {
                    inviteList.Add($"@{Constants.UserPrefix}{remoteRoom.PuppetId}_{item.ToLower().Trim()}:{Domain}");

                }
            }

            request.Content = JsonContent.Create(new
            {
                name = remoteRoom.Name,
                topic = remoteRoom.Topic ?? String.Empty,
                visibility = "private",
                preset = "private_chat",
                invite = inviteList // Invite owner and person sending message
            });

            _logger.LogInformation($"Creating Room: RoomName: '{remoteRoom.Name}' with Topic '{remoteRoom.Topic}'");

            try
            {
                var response = await _httpClient.SendAsync(request);

                var body = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"Create Room Response: {response.StatusCode} - {body}");

                if (response.IsSuccessStatusCode)
                {
                    using JsonDocument doc = JsonDocument.Parse(body);
                    string roomId = doc.RootElement.GetProperty("room_id").GetString();


                    _database.GetCollection<MatrixRoom>().Insert(new MatrixRoom()
                    {
                        Name = remoteRoom.Name,
                        Topic = remoteRoom.Topic,
                        isDirect = false,
                        PuppetId = remoteRoom.PuppetId,
                        RemoteRoomId = remoteRoom.RoomId,
                        RoomId = roomId
                    });

                    _logger.LogInformation($"Room created successfully! Room ID: {roomId}");

                    // Set the Bot's display name
                    await SetUserDisplayName($"@{Constants.BotUsername}:{Domain}", roomId, Constants.BotDisplayName);

                    // Set the puppet owner to Admin
                    await SetUserDisplayName( new RemoteUser() { PuppetId = remoteRoom.PuppetId, UserId = remoteRoom.RoomId, Name = remoteRoom.Name }, new MatrixRoom() { RoomId = roomId });

                    // Set the puppet owner to Admin
                    if (_puppets.TryGetValue(remoteRoom.PuppetId, out puppet))
                    {
                        await SetUserPowerLevelAsync(roomId, puppet.Owner);
                    }

                    return null;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Failed to create room. Status: {response.StatusCode}, Response: {errorContent}");
                    return null;

                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to create room. {ex.Message}");
                return null;
            }

        }
    }
}
