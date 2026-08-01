using Microsoft.AspNetCore.SignalR;
using MovieSync.Web.Services;

namespace MovieSync.Web.Hubs
{
    public class MovieSyncHub : Hub
    {
        private readonly RoomStateManager _roomStateManager;

        public MovieSyncHub(RoomStateManager roomStateManager)
        {
            _roomStateManager = roomStateManager;
        }

        public async Task JoinRoom(string roomId)
        {
            string username = Context.User?.Identity?.Name ?? "Guest";
            // If the user's name is an email, let's prettify it by taking the part before the @ sign
            if (username.Contains("@"))
            {
                username = username.Split('@')[0];
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            
            var roomState = _roomStateManager.JoinRoom(roomId, Context.ConnectionId, username);
            
            if (roomState != null)
            {
                await Clients.Group(roomId).SendAsync("UserJoined", $"{username} joined the room.", roomState.Participants);
                await Clients.Group(roomId).SendAsync("ReceiveRoomState", roomState);
            }
        }

        public async Task LeaveRoom(string roomId)
        {
            string username = Context.User?.Identity?.Name ?? "Guest";
            if (username.Contains("@")) username = username.Split('@')[0];

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
            
            var roomState = _roomStateManager.LeaveRoom(roomId, Context.ConnectionId);
            
            if (roomState != null)
            {
                await Clients.Group(roomId).SendAsync("UserLeft", $"{username} left the room.", roomState.Participants);
                await Clients.Group(roomId).SendAsync("ReceiveRoomState", roomState);
            }
            else
            {
                await Clients.Group(roomId).SendAsync("UserLeft", $"{username} left the room.", null);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var roomState = _roomStateManager.RemoveParticipantFromAllRooms(Context.ConnectionId, out var leftRoomId);
            
            if (!string.IsNullOrEmpty(leftRoomId))
            {
                string username = Context.User?.Identity?.Name ?? "Guest";
                if (username.Contains("@")) username = username.Split('@')[0];

                if (roomState != null)
                {
                    await Clients.Group(leftRoomId).SendAsync("UserLeft", $"{username} disconnected.", roomState.Participants);
                    await Clients.Group(leftRoomId).SendAsync("ReceiveRoomState", roomState);
                }
                else
                {
                    await Clients.Group(leftRoomId).SendAsync("UserLeft", $"{username} disconnected.", null);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendMessage(string roomId, string user, string message)
        {
            await Clients.Group(roomId).SendAsync("ReceiveMessage", user, message, DateTime.UtcNow);
        }

        public async Task SendReaction(string roomId, string emoji)
        {
            string username = Context.User?.Identity?.Name ?? "Guest";
            if (username.Contains("@")) username = username.Split('@')[0];

            await Clients.Group(roomId).SendAsync("ReceiveReaction", username, emoji);
        }

        public async Task PlayVideo(string roomId, double time)
        {
            var room = _roomStateManager.GetRoom(roomId);
            if (room != null && room.IsLocked && room.HostConnectionId != Context.ConnectionId)
            {
                return; // Only host is allowed
            }

            if (room != null)
            {
                _roomStateManager.UpdateVideoState(roomId, room.CurrentVideoUrl, room.CurrentVideoTitle, true, time);
                await Clients.OthersInGroup(roomId).SendAsync("VideoPlayed", time);
            }
        }

        public async Task PauseVideo(string roomId, double time)
        {
            var room = _roomStateManager.GetRoom(roomId);
            if (room != null && room.IsLocked && room.HostConnectionId != Context.ConnectionId)
            {
                return; // Only host is allowed
            }

            if (room != null)
            {
                _roomStateManager.UpdateVideoState(roomId, room.CurrentVideoUrl, room.CurrentVideoTitle, false, time);
                await Clients.OthersInGroup(roomId).SendAsync("VideoPaused", time);
            }
        }

        public async Task SeekVideo(string roomId, double time)
        {
            var room = _roomStateManager.GetRoom(roomId);
            if (room != null && room.IsLocked && room.HostConnectionId != Context.ConnectionId)
            {
                return; // Only host is allowed
            }

            if (room != null)
            {
                _roomStateManager.UpdateVideoState(roomId, room.CurrentVideoUrl, room.CurrentVideoTitle, room.IsPlaying, time);
                await Clients.OthersInGroup(roomId).SendAsync("VideoSought", time);
            }
        }

        public async Task ChangeVideo(string roomId, string url, string title, double time)
        {
            var room = _roomStateManager.GetRoom(roomId);
            if (room != null && room.IsLocked && room.HostConnectionId != Context.ConnectionId)
            {
                return; // Only host is allowed
            }

            if (room != null)
            {
                // Ingest and rewrite third-party stream URLs to go through our backend proxy
                string resolvedUrl = url;
                if (!IsYouTubeUrl(url) && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    resolvedUrl = $"/api/proxy?url={Uri.EscapeDataString(url)}";
                }

                _roomStateManager.UpdateVideoState(roomId, resolvedUrl, title, false, time);
                _roomStateManager.AddToHistory(roomId, resolvedUrl, title);
                await Clients.Group(roomId).SendAsync("VideoChanged", resolvedUrl, title, time);
                await Clients.Group(roomId).SendAsync("ReceiveRoomState", room);
            }
        }

        public async Task ToggleFavorite(string roomId, string url, string title)
        {
            var room = _roomStateManager.GetRoom(roomId);
            if (room != null)
            {
                _roomStateManager.ToggleFavorite(roomId, url, title);
                await Clients.Group(roomId).SendAsync("FavoritesUpdated", room.Favorites);
                await Clients.Group(roomId).SendAsync("ReceiveRoomState", room);
            }
        }

        public async Task ToggleLock(string roomId, bool isLocked)
        {
            var room = _roomStateManager.GetRoom(roomId);
            if (room != null && room.HostConnectionId == Context.ConnectionId)
            {
                _roomStateManager.ToggleLock(roomId, isLocked);
                await Clients.Group(roomId).SendAsync("LockToggled", isLocked);
                await Clients.Group(roomId).SendAsync("ReceiveRoomState", room);
            }
        }

        public async Task SetHost(string roomId, string newHostConnectionId)
        {
            var room = _roomStateManager.GetRoom(roomId);
            if (room != null && room.HostConnectionId == Context.ConnectionId)
            {
                if (_roomStateManager.SetHost(roomId, newHostConnectionId))
                {
                    await Clients.Group(roomId).SendAsync("ReceiveRoomState", room);
                }
            }
        }

        public async Task SendWebRtcSignal(string roomId, string targetConnectionId, string signalType, string signalData)
        {
            await Clients.Client(targetConnectionId).SendAsync("ReceiveWebRtcSignal", Context.ConnectionId, signalType, signalData);
        }

        public async Task UpdateMediaState(string roomId, bool isMuted, bool isCameraOff)
        {
            var room = _roomStateManager.GetRoom(roomId);
            if (room != null)
            {
                var participant = room.Participants.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (participant != null)
                {
                    participant.IsMuted = isMuted;
                    participant.IsCameraOff = isCameraOff;
                    await Clients.Group(roomId).SendAsync("ReceiveRoomState", room);
                }
            }
        }

        private bool IsYouTubeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || 
                   url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
        }
    }
}