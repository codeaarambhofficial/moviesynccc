using System.Collections.Concurrent;

namespace MovieSync.Web.Services
{
    public class RoomState
    {
        public string RoomId { get; set; } = string.Empty;
        public string HostConnectionId { get; set; } = string.Empty;
        public string HostUsername { get; set; } = string.Empty;
        public bool IsLocked { get; set; } // When true, only host can seek/play/pause/change videos
        public string CurrentVideoUrl { get; set; } = "local://Movie"; // Default local movie
        public string CurrentVideoTitle { get; set; } = "Local Movie";
        public bool IsPlaying { get; set; }
        public double CurrentTime { get; set; }
        public DateTime LastStateUpdate { get; set; } = DateTime.UtcNow;
        public List<UserSession> Participants { get; set; } = new();
        public List<VideoHistoryItem> WatchHistory { get; set; } = new();
        public List<FavoriteVideoItem> Favorites { get; set; } = new();
    }

    public class UserSession
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string AvatarSeed { get; set; } = string.Empty;
        public bool IsMuted { get; set; }
        public bool IsCameraOff { get; set; }
    }

    public class VideoHistoryItem
    {
        public string VideoUrl { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    }

    public class FavoriteVideoItem
    {
        public string VideoUrl { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    public class RoomStateManager
    {
        private readonly ConcurrentDictionary<string, RoomState> _rooms = new();

        public RoomState GetOrCreateRoom(string roomId, string connectionId, string username)
        {
            return _rooms.GetOrAdd(roomId, id => new RoomState
            {
                RoomId = id,
                HostConnectionId = connectionId,
                HostUsername = username,
                LastStateUpdate = DateTime.UtcNow
            });
        }

        public RoomState? GetRoom(string roomId)
        {
            return _rooms.TryGetValue(roomId, out var room) ? room : null;
        }

        public RoomState? JoinRoom(string roomId, string connectionId, string username)
        {
            var room = GetOrCreateRoom(roomId, connectionId, username);
            
            lock (room)
            {
                // Remove if existing session exists
                room.Participants.RemoveAll(p => p.ConnectionId == connectionId);

                // Add new participant
                // Create a random avatar seed using connectionId
                string seed = username + "_" + (connectionId.Length > 4 ? connectionId.Substring(connectionId.Length - 4) : connectionId);
                room.Participants.Add(new UserSession
                {
                    ConnectionId = connectionId,
                    Username = username,
                    AvatarSeed = seed
                });

                // If host is empty or no longer in participants, make this user the host
                if (string.IsNullOrEmpty(room.HostConnectionId) || !room.Participants.Any(p => p.ConnectionId == room.HostConnectionId))
                {
                    room.HostConnectionId = connectionId;
                    room.HostUsername = username;
                }
            }

            return room;
        }

        public RoomState? LeaveRoom(string roomId, string connectionId)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return null;

            lock (room)
            {
                room.Participants.RemoveAll(p => p.ConnectionId == connectionId);

                if (room.Participants.Count == 0)
                {
                    // Clean up room if no users left
                    _rooms.TryRemove(roomId, out _);
                    return null;
                }

                // If host left, transfer host role
                if (room.HostConnectionId == connectionId)
                {
                    var nextHost = room.Participants.FirstOrDefault();
                    if (nextHost != null)
                    {
                        room.HostConnectionId = nextHost.ConnectionId;
                        room.HostUsername = nextHost.Username;
                    }
                }
            }

            return room;
        }

        public void UpdateVideoState(string roomId, string url, string title, bool isPlaying, double currentTime)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return;

            lock (room)
            {
                room.CurrentVideoUrl = url;
                room.CurrentVideoTitle = title;
                room.IsPlaying = isPlaying;
                room.CurrentTime = currentTime;
                room.LastStateUpdate = DateTime.UtcNow;
            }
        }

        public void AddToHistory(string roomId, string url, string title)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return;

            lock (room)
            {
                // Remove if video already exists in history to bring it to top
                room.WatchHistory.RemoveAll(h => h.VideoUrl == url);
                room.WatchHistory.Insert(0, new VideoHistoryItem
                {
                    VideoUrl = url,
                    Title = string.IsNullOrEmpty(title) ? url : title,
                    PlayedAt = DateTime.UtcNow
                });

                // Keep only top 10 watch history items
                if (room.WatchHistory.Count > 10)
                {
                    room.WatchHistory.RemoveAt(room.WatchHistory.Count - 1);
                }
            }
        }

        public bool ToggleFavorite(string roomId, string url, string title)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return false;

            lock (room)
            {
                var existing = room.Favorites.FirstOrDefault(f => f.VideoUrl == url);
                if (existing != null)
                {
                    room.Favorites.Remove(existing);
                    return false; // Removed
                }
                else
                {
                    room.Favorites.Add(new FavoriteVideoItem
                    {
                        VideoUrl = url,
                        Title = string.IsNullOrEmpty(title) ? url : title
                    });
                    return true; // Added
                }
            }
        }

        public void ToggleLock(string roomId, bool isLocked)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return;

            lock (room)
            {
                room.IsLocked = isLocked;
            }
        }

        public bool SetHost(string roomId, string hostConnectionId)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return false;

            lock (room)
            {
                var user = room.Participants.FirstOrDefault(p => p.ConnectionId == hostConnectionId);
                if (user != null)
                {
                    room.HostConnectionId = user.ConnectionId;
                    room.HostUsername = user.Username;
                    return true;
                }
            }
            return false;
        }

        public RoomState? RemoveParticipantFromAllRooms(string connectionId, out string? leftRoomId)
        {
            leftRoomId = null;
            foreach (var kvp in _rooms)
            {
                var room = kvp.Value;
                lock (room)
                {
                    var user = room.Participants.FirstOrDefault(p => p.ConnectionId == connectionId);
                    if (user != null)
                    {
                        leftRoomId = kvp.Key;
                        room.Participants.Remove(user);

                        if (room.Participants.Count == 0)
                        {
                            _rooms.TryRemove(kvp.Key, out _);
                            return null; // Room is destroyed
                        }

                        if (room.HostConnectionId == connectionId)
                        {
                            var nextHost = room.Participants.FirstOrDefault();
                            if (nextHost != null)
                            {
                                room.HostConnectionId = nextHost.ConnectionId;
                                room.HostUsername = nextHost.Username;
                            }
                        }

                        return room;
                    }
                }
            }
            return null;
        }
    }
}
