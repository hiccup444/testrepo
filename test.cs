using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Core;
using static RoomScannerMod.RoomScannerPlugin;

namespace RoomScannerMod
{
    [BepInPlugin("com.hiccup.roomscanner", "Room Scanner Mod", "1.0.9")]
    public class RoomScannerPlugin : BaseUnityPlugin, IConnectionCallbacks
    {
        public static RoomScannerPlugin Instance;
        public static List<RoomInfo> CachedRoomList = new List<RoomInfo>();
        public static Dictionary<string, StoredRoomInfo> AllRoomsEverSeen = new Dictionary<string, StoredRoomInfo>();
        public static RoomInfo LastJoinedRoom;
        public static string LastAttemptedRoomName;
        private RoomJoinerUI _roomJoinerUI;

        public class StoredRoomInfo
        {
            public string Name { get; set; }
            public int LastSeenPlayerCount { get; set; }
            public int MaxPlayers { get; set; }
            public ExitGames.Client.Photon.Hashtable CustomProperties { get; set; }
            public bool IsCurrentlyActive { get; set; }
            public System.DateTime LastSeen { get; set; }
        }

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo("[Room Scanner] Initializing...");

            var go = new GameObject("RoomLogger");
            go.AddComponent<RoomLogger>();
            DontDestroyOnLoad(go);

            var uiObj = new GameObject("RoomJoinerUI");
            var ui = uiObj.AddComponent<RoomJoinerUI>();
            _roomJoinerUI = ui;
            DontDestroyOnLoad(uiObj);

            PhotonNetwork.AddCallbackTarget(this);
            Logger.LogInfo("[Room Scanner] Initialization complete. Waiting for Photon connection.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                _roomJoinerUI?.ToggleUI();
            }
        }

        private void OnDestroy()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        public class RoomJoinerUI : MonoBehaviour
        {
            private GameObject canvasObj;
            private bool isVisible = true;
            private InputField inputField;
            private Button joinButton;
            private Button refreshButton;
            private Text refreshButtonText;

            public void ToggleUI()
            {
                isVisible = !isVisible;
                if (canvasObj != null)
                    canvasObj.SetActive(isVisible);

                Debug.Log($"[Room Scanner] UI toggled {(isVisible ? "on" : "off")}");
            }

            private void Awake()
            {
                CreateUI();
                DontDestroyOnLoad(canvasObj);
                canvasObj.SetActive(isVisible);
            }

            private void CreateUI()
            {
                canvasObj = new GameObject("RoomJoinerCanvas");
                var canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();

                // Panel - increased height for additional button
                var panelObj = new GameObject("Panel");
                panelObj.transform.SetParent(canvasObj.transform);
                var panel = panelObj.AddComponent<Image>();
                panel.color = new Color(0, 0, 0, 0.5f);
                var panelRect = panel.GetComponent<RectTransform>();
                panelRect.sizeDelta = new Vector2(300, 200);
                panelRect.localPosition = Vector3.zero;

                // InputField
                var inputObj = new GameObject("InputField");
                inputObj.transform.SetParent(panelObj.transform);
                var inputImage = inputObj.AddComponent<Image>();
                inputImage.color = Color.white;
                inputField = inputObj.AddComponent<InputField>();

                var inputTextObj = new GameObject("Text");
                inputTextObj.transform.SetParent(inputObj.transform);
                var inputText = inputTextObj.AddComponent<Text>();
                inputText.text = "";
                inputText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                inputText.color = Color.black;
                inputText.alignment = TextAnchor.MiddleLeft;
                inputField.textComponent = inputText;

                var placeholderObj = new GameObject("Placeholder");
                placeholderObj.transform.SetParent(inputObj.transform);
                var placeholder = placeholderObj.AddComponent<Text>();
                placeholder.text = "Enter room name";
                placeholder.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                placeholder.color = Color.gray;
                inputField.placeholder = placeholder;

                inputText.rectTransform.sizeDelta = placeholder.rectTransform.sizeDelta = new Vector2(200, 30);
                inputObj.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 30);
                inputObj.GetComponent<RectTransform>().localPosition = new Vector3(0, 50, 0);

                // Join Button
                var buttonObj = new GameObject("JoinButton");
                buttonObj.transform.SetParent(panelObj.transform);
                var buttonImage = buttonObj.AddComponent<Image>();
                buttonImage.color = Color.gray;
                joinButton = buttonObj.AddComponent<Button>();

                var buttonTextObj = new GameObject("Text");
                buttonTextObj.transform.SetParent(buttonObj.transform);
                var buttonText = buttonTextObj.AddComponent<Text>();
                buttonText.text = "Join Room";
                buttonText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                buttonText.color = Color.white;
                buttonText.alignment = TextAnchor.MiddleCenter;

                buttonText.rectTransform.sizeDelta = new Vector2(150, 30);
                buttonObj.GetComponent<RectTransform>().sizeDelta = new Vector2(150, 30);
                buttonObj.GetComponent<RectTransform>().localPosition = new Vector3(0, 0, 0);

                joinButton.onClick.AddListener(OnJoinClicked);

                // Refresh Lobby Button
                var refreshButtonObj = new GameObject("RefreshButton");
                refreshButtonObj.transform.SetParent(panelObj.transform);
                var refreshButtonImage = refreshButtonObj.AddComponent<Image>();
                refreshButtonImage.color = new Color(0.2f, 0.5f, 0.2f);
                refreshButton = refreshButtonObj.AddComponent<Button>();

                var refreshButtonTextObj = new GameObject("Text");
                refreshButtonTextObj.transform.SetParent(refreshButtonObj.transform);
                refreshButtonText = refreshButtonTextObj.AddComponent<Text>();
                refreshButtonText.text = "Refresh Lobby";
                refreshButtonText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                refreshButtonText.color = Color.white;
                refreshButtonText.alignment = TextAnchor.MiddleCenter;

                refreshButtonText.rectTransform.sizeDelta = new Vector2(150, 30);
                refreshButtonObj.GetComponent<RectTransform>().sizeDelta = new Vector2(150, 30);
                refreshButtonObj.GetComponent<RectTransform>().localPosition = new Vector3(0, -50, 0);

                refreshButton.onClick.AddListener(OnRefreshClicked);
            }

            private void OnRefreshClicked()
            {
                if (PhotonNetwork.InLobby)
                {
                    Debug.Log("[Room Scanner] Leaving lobby to refresh room list...");
                    refreshButtonText.text = "Reconnecting...";
                    refreshButton.interactable = false;
                    PhotonNetwork.LeaveLobby();
                    StartCoroutine(RejoinLobbyAfterDelay());
                }
                else
                {
                    Debug.Log("[Room Scanner] Not in lobby, attempting to join...");
                    PhotonNetwork.JoinLobby();
                }
            }

            private IEnumerator RejoinLobbyAfterDelay()
            {
                yield return new WaitForSeconds(1f);
                Debug.Log("[Room Scanner] Rejoining lobby...");
                PhotonNetwork.JoinLobby();
                yield return new WaitForSeconds(2f);
                refreshButtonText.text = "Refresh Lobby";
                refreshButton.interactable = true;
            }

            private void OnJoinClicked()
            {
                string roomName = inputField.text.Trim();
                if (!string.IsNullOrEmpty(roomName))
                {
                    Debug.Log($"[Room Scanner] Attempting to join room by name: {roomName}");
                    RoomScannerPlugin.LastAttemptedRoomName = roomName;

                    // Leave current room first if we're in one
                    if (PhotonNetwork.InRoom)
                    {
                        Debug.Log("[Room Scanner] Leaving current room before joining new one...");
                        PhotonNetwork.LeaveRoom();
                        StartCoroutine(JoinRoomAfterLeave(roomName));
                    }
                    else
                    {
                        PhotonNetwork.JoinRoom(roomName);
                    }
                }
                else
                {
                    Debug.LogWarning("[Room Scanner] Room name is empty.");
                }
            }

            private IEnumerator JoinRoomAfterLeave(string roomName)
            {
                while (PhotonNetwork.InRoom)
                {
                    yield return null;
                }
                yield return new WaitForSeconds(0.5f);

                Debug.Log($"[Room Scanner] Now joining room: {roomName}");
                PhotonNetwork.JoinRoom(roomName);
            }
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
            Debug.Log($"[Room Scanner] Room list update received. New count: {roomList.Count}");
            RoomScannerPlugin.CachedRoomList = roomList;

            // First, mark all rooms as inactive
            foreach (var room in RoomScannerPlugin.AllRoomsEverSeen.Values)
            {
                room.IsCurrentlyActive = false;
            }

            // Update or add rooms from current list
            foreach (var room in roomList)
            {
                if (!room.RemovedFromList)
                {
                    if (RoomScannerPlugin.AllRoomsEverSeen.TryGetValue(room.Name, out var storedRoom))
                    {
                        // Update existing room
                        storedRoom.LastSeenPlayerCount = room.PlayerCount;
                        storedRoom.MaxPlayers = room.MaxPlayers;
                        storedRoom.CustomProperties = room.CustomProperties;
                        storedRoom.IsCurrentlyActive = true;
                        storedRoom.LastSeen = System.DateTime.Now;
                    }
                    else
                    {
                        // Add new room
                        RoomScannerPlugin.AllRoomsEverSeen[room.Name] = new StoredRoomInfo
                        {
                            Name = room.Name,
                            LastSeenPlayerCount = room.PlayerCount,
                            MaxPlayers = room.MaxPlayers,
                            CustomProperties = room.CustomProperties,
                            IsCurrentlyActive = true,
                            LastSeen = System.DateTime.Now
                        };
                    }
                }
            }

            // Print all rooms with their status
            Debug.Log($"[Room Scanner] === ALL ROOMS HISTORY ({RoomScannerPlugin.AllRoomsEverSeen.Count} total) ===");
            foreach (var kvp in RoomScannerPlugin.AllRoomsEverSeen)
            {
                var room = kvp.Value;
                string status = room.IsCurrentlyActive ? "[ACTIVE]" : "[INACTIVE]";
                string timeSince = (System.DateTime.Now - room.LastSeen).TotalSeconds < 60
                    ? $"{(int)(System.DateTime.Now - room.LastSeen).TotalSeconds}s ago"
                    : $"{(int)(System.DateTime.Now - room.LastSeen).TotalMinutes}m ago";

                Debug.Log($"[Room Scanner] {status} Room: {room.Name}, Players: {room.LastSeenPlayerCount}/{room.MaxPlayers}, Last seen: {timeSince}");

                if (room.CustomProperties != null)
                {
                    foreach (var prop in room.CustomProperties)
                    {
                        string key = prop.Key?.ToString() ?? "(null key)";
                        string value = prop.Value != null ? prop.Value.ToString() : "null";
                        Debug.Log($"[Room Scanner]   - Property: {key} = {value}");
                    }
                }
            }
            Debug.Log($"[Room Scanner] === Active: {roomList.Count}, Total seen: {RoomScannerPlugin.AllRoomsEverSeen.Count} ===");
        }

        public void OnJoinedLobby()
        {
            Debug.Log("[Room Scanner] Joined a Photon lobby.");
            Debug.Log("[Room Scanner] Note: Photon automatically sends room list updates periodically while in lobby.");
            Debug.Log("[Room Scanner] Rooms will accumulate over time. Check console for [ACTIVE] vs [INACTIVE] status.");
        }
        public void OnLeftLobby() => Debug.Log("[Room Scanner] Left the Photon lobby.");
        public void OnLobbyStatisticsUpdate(List<TypedLobbyInfo> lobbyStatistics) { }

        public void OnJoinedRoom()
        {
            Debug.Log("[Room Scanner] Successfully joined a room.");

            var sceneName = "Airport";

            // Get scene from current room properties
            if (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("CurrentScene", out var currentSceneProp) && currentSceneProp is string currentScene)
            {
                sceneName = currentScene;
                Debug.Log($"[Room Scanner] Found scene in room properties: {sceneName}");
            }

            // Update the connection service state to match our room
            var connectionService = GameHandler.GetService<ConnectionService>();
            if (connectionService != null)
            {
                // Try to switch to InRoomState if not already there
                var currentState = connectionService.StateMachine.CurrentState;
                if (!(currentState is InRoomState))
                {
                    Debug.Log("[Room Scanner] Switching to InRoomState...");
                    var inRoomState = connectionService.StateMachine.SwitchState<InRoomState>(false);
                    if (inRoomState != null)
                    {
                        // Reset the customization flag to ensure it loads
                        inRoomState.hasLoadedCustomization = false;
                    }
                }
            }

            // Check if we're already in the correct scene
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == sceneName)
            {
                Debug.Log($"[Room Scanner] Already in scene {sceneName}.");
                StartCoroutine(ForceCustomizationRefresh());
                return;
            }

            RetrievableResourceSingleton<LoadingScreenHandler>.Instance.Load(
                LoadingScreen.LoadingScreenType.Basic,
                null,
                new IEnumerator[] { LoadSceneCoroutine(sceneName) }
            );

            Debug.Log("[Room Scanner] Triggered scene loading.");
        }

        private IEnumerator ForceCustomizationRefresh()
        {
            yield return new WaitForSeconds(1f);

            // First, ensure InRoomState hasn't marked customization as loaded
            var connectionService = GameHandler.GetService<ConnectionService>();
            if (connectionService != null)
            {
                var inRoomState = connectionService.StateMachine.CurrentState as InRoomState;
                if (inRoomState != null)
                {
                    Debug.Log($"[Room Scanner] InRoomState.hasLoadedCustomization = {inRoomState.hasLoadedCustomization}");
                    if (inRoomState.hasLoadedCustomization)
                    {
                        Debug.Log("[Room Scanner] Resetting hasLoadedCustomization to force Steam cosmetics reload.");
                        inRoomState.hasLoadedCustomization = false;
                    }
                }
            }

            // Find all characters and force their customization to refresh
            var characters = UnityEngine.Object.FindObjectsOfType<CharacterCustomization>();
            Debug.Log($"[Room Scanner] Found {characters.Length} characters to refresh customization.");

            foreach (var customization in characters)
            {
                var character = customization.GetComponent<Character>();
                if (character != null && character.photonView != null)
                {
                    // For local character, force Steam cosmetics reload
                    if (character.photonView.IsMine && character.IsLocal)
                    {
                        Debug.Log("[Room Scanner] Forcing Steam cosmetics reload for local character.");

                        // Call the private method using reflection
                        var tryGetMethod = customization.GetType().GetMethod("TryGetCosmeticsFromSteam",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        if (tryGetMethod != null)
                        {
                            tryGetMethod.Invoke(customization, null);
                        }
                    }

                    // Trigger the OnPlayerDataChange for all characters
                    var playerDataService = GameHandler.GetService<PersistentPlayerDataService>();
                    if (playerDataService != null)
                    {
                        var playerData = playerDataService.GetPlayerData(character.photonView.Owner);
                        playerDataService.SetPlayerData(character.photonView.Owner, playerData);
                    }
                }
            }
        }

        private IEnumerator LoadSceneCoroutine(string sceneName)
        {
            PhotonNetwork.LoadLevel(sceneName);
            while (PhotonNetwork.LevelLoadingProgress < 1f) yield return null;
            yield return new WaitForSecondsRealtime(3f);
        }

        public void OnJoinRoomFailed(short returnCode, string message)
        {
            Debug.LogError($"[Room Scanner] Failed to join room: {message} (Error code: {returnCode})");

            // Remove the room from our history if it doesn't exist
            if (!string.IsNullOrEmpty(RoomScannerPlugin.LastAttemptedRoomName))
            {
                string failedRoomName = RoomScannerPlugin.LastAttemptedRoomName;

                // Error code 32758 typically means room doesn't exist
                if (returnCode == 32758 || message.Contains("not exist") || message.Contains("not found"))
                {
                    if (RoomScannerPlugin.AllRoomsEverSeen.ContainsKey(failedRoomName))
                    {
                        Debug.Log($"[Room Scanner] Room '{failedRoomName}' no longer exists. Removing from history.");
                        RoomScannerPlugin.AllRoomsEverSeen.Remove(failedRoomName);

                        // Also remove from cached list if present
                        RoomScannerPlugin.CachedRoomList.RemoveAll(r => r.Name == failedRoomName);
                    }
                }

                // Clear the attempted room name
                RoomScannerPlugin.LastAttemptedRoomName = null;
            }
        }
        public void OnCreateRoomFailed(short returnCode, string message) { }
        public void OnCreatedRoom() { }
        public void OnJoinRandomFailed(short returnCode, string message) { }
        public void OnLeftRoom() => Debug.Log("[Room Scanner] Left the room.");
        public void OnFriendListUpdate(List<FriendInfo> friendList) { }
    }
}
