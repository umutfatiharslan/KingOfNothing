using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KingOfNothing.Core
{
    /// <summary>Holds the current GameMode. Other systems ask GameModeManager.IsDeveloperMode.</summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class GameModeManager : MonoBehaviour
    {
        public static GameModeManager Instance { get; private set; }

        [SerializeField] GameMode startMode = GameMode.Developer;
        [Tooltip("Allow switching the mode while playing (editor and development builds only).")]
        [SerializeField] bool allowRuntimeToggle = true;
        [SerializeField] Key toggleKey = Key.F1;

        GameMode mode;

        public static event Action<GameMode> ModeChanged;

        public GameMode Mode => mode;

        /// <summary>True when developer tools are active. False if there is no manager in the scene.</summary>
        public static bool IsDeveloperMode => Instance != null && Instance.mode == GameMode.Developer;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameModeManager] More than one manager in the scene; this one is disabled.", this);
                enabled = false;
                return;
            }
            Instance = this;
            mode = startMode;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!allowRuntimeToggle || !(Application.isEditor || Debug.isDebugBuild)) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                SetMode(mode == GameMode.Developer ? GameMode.Default : GameMode.Developer);
        }

        public void SetMode(GameMode newMode)
        {
            if (mode == newMode) return;
            mode = newMode;
            Debug.Log($"[GameModeManager] Mode: {mode}");
            ModeChanged?.Invoke(mode);
        }
    }
}
