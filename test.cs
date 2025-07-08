using System.Collections;
using System.Collections.Generic;
using BepInEx;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Zorro.Core;

namespace RoomScannerMod
{
    [BepInPlugin("com.hiccup.roomscanner", "Room Scanner Mod", "1.0.8")]
    public class RoomScannerPlugin : BaseUnityPlugin, IConnectionCallbacks
    {
        public static RoomScannerPlugin Instance;
        public static List<RoomInfo> CachedRoomList = new List<RoomInfo>();
        public static RoomInfo LastJoinedRoom;

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo("[Room Scanner] Initializing...");

            var go = new GameObject("RoomLogger");
            go.AddComponent<RoomLogger>();
            DontDestroyOnLoad(go);

            PhotonNetwork.AddCallbackTarget(this);
            Logger.LogInfo("[Room Scanner] Initialization complete. Waiting for Photon connection.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                if (CachedRoomList.Count == 0)
                {
                    Logger.LogWarning("[Room Scanner] No rooms available to join.");
                    return;
                }

                var firstRoom = CachedRoomList[0];
                Logger.LogInfo($"[Room Scanner] Attempting to join room: {firstRoom.Name}");
                LastJoinedRoom = firstRoom;
                PhotonNetwork.JoinRoom(firstRoom.Name);
            }
        }

        private void OnDestroy()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        public void OnConnectedToMaster()
        {
            Logger.LogInfo("[Room Scanner] Connected to Photon Master. Delaying lobby join by 3 seconds...");
            StartCoroutine(DelayedJoinLobby());
        }

        private IEnumerator DelayedJoinLobby()
        {
            yield return new WaitForSeconds(3f);
            Logger.LogInfo("[Room Scanner] Joining Photon lobby...");
            PhotonNetwork.JoinLobby();
        }

        public void OnConnected() { }
        public void OnDisconnected(DisconnectCause cause) { }
        public void OnRegionListReceived(RegionHandler regionHandler) { }
        public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
        public void OnCustomAuthenticationFailed(string debugMessage) { }
    }

    public class RoomLogger : MonoBehaviour, ILobbyCallbacks, IMatchmakingCallbacks
    {
        private void OnEnable()
        {
            PhotonNetwork.AddCallbackTarget(this);
            Debug.Log("[Room Scanner] RoomLogger enabled.");
        }

        private void OnDisable()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
            Debug.Log("[Room Scanner] RoomLogger disabled.");
        }

        public void OnRoomListUpdate(List<RoomInfo> roomList)
        {
            Debug.Log($"[Room Scanner] Found {roomList.Count} rooms:");
            RoomScannerPlugin.CachedRoomList = roomList;

            foreach (var room in roomList)
            {
                Debug.Log($"[Room Scanner] Room: {room.Name}, Players: {room.PlayerCount}/{room.MaxPlayers}");

                if (room.CustomProperties.Count == 0)
                {
                    Debug.Log("[Room Scanner] - No custom properties found.");
                    continue;
                }

                foreach (var kvp in room.CustomProperties)
                {
                    string key = kvp.Key?.ToString() ?? "(null key)";
                    string value = kvp.Value != null ? kvp.Value.ToString() : "null";
                    Debug.Log($"[Room Scanner] - Property: {key} = {value}");
                }
            }
        }

        public void OnJoinedLobby() => Debug.Log("[Room Scanner] Joined a Photon lobby.");
        public void OnLeftLobby() => Debug.Log("[Room Scanner] Left the Photon lobby.");
        public void OnLobbyStatisticsUpdate(List<TypedLobbyInfo> lobbyStatistics) { }

        public void OnJoinedRoom()
        {
            Debug.Log("[Room Scanner] Successfully joined a room.");

            var sceneName = "Airport"; // fallback
            var room = RoomScannerPlugin.LastJoinedRoom;
            if (room != null && room.CustomProperties.TryGetValue("CurrentScene", out var sceneProp) && sceneProp is string scene)
            {
                sceneName = scene;
                Debug.Log($"[Room Scanner] Found scene in room properties: {sceneName}");
            }
            else
            {
                Debug.LogWarning("[Room Scanner] No scene found in room properties, defaulting to Airport");
            }

            var connectionService = GameHandler.GetService<ConnectionService>();
            var joinState = connectionService.StateMachine.SwitchState<JoinSpecificRoomState>(false);
            joinState.RoomName = PhotonNetwork.CurrentRoom.Name;
            joinState.RegionToJoin = PhotonNetwork.CloudRegion;

            RetrievableResourceSingleton<LoadingScreenHandler>.Instance.Load(
                LoadingScreen.LoadingScreenType.Basic,
                null,
                new IEnumerator[] { LoadSceneCoroutine(sceneName) }
            );

            Debug.Log("[Room Scanner] Triggered scene loading and initialization.");
        }

        private IEnumerator LoadSceneCoroutine(string sceneName)
        {
            Debug.Log($"[Room Scanner] Loading scene: {sceneName}");
            PhotonNetwork.LoadLevel(sceneName);

            while (PhotonNetwork.LevelLoadingProgress < 1f)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(3f);
        }


        public void OnJoinRoomFailed(short returnCode, string message) => Debug.LogError($"[Room Scanner] Failed to join room: {message}");
        public void OnCreateRoomFailed(short returnCode, string message) { }
        public void OnCreatedRoom() { }
        public void OnJoinRandomFailed(short returnCode, string message) { }

        public void OnLeftRoom() => Debug.Log("[Room Scanner] Left the room.");
        public void OnFriendListUpdate(List<FriendInfo> friendList) { }
    }
}
