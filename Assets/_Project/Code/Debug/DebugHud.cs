using System;
using UnityEngine;

namespace TogetherWeFall.DebugTools
{
    /// <summary>
    /// FPS, enemy count and dungeon readout. Deliberately on OnGUI: zero
    /// dependency on a uGUI canvas and zero influence on the measurements we
    /// care about (its own cost is stable and does not grow with the enemy
    /// count).
    ///
    /// Both the average and the worst frame in the window are shown: an average
    /// FPS hides exactly the spikes that time-sliced pathfinding exists to avoid.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        [SerializeField] private float _sampleWindowSeconds = 0.5f;
        [SerializeField] private int _fontSize = 18;

        private Func<int> _enemyCountProvider;
        private Func<string> _statusProvider;

        private float _windowElapsed;
        private int _windowFrames;
        private float _windowWorstFrameMs;

        private float _displayFps;
        private float _displayAverageMs;
        private float _displayWorstMs;
        private int _displayEnemyCount;
        private string _displayStatus;

        private GUIStyle _style;

        /// <summary>
        /// Both providers stay null until something has data to offer. The HUD
        /// knows nothing about ECS or about dungeons — only about delegates.
        /// </summary>
        public void Initialize(Func<int> enemyCountProvider = null, Func<string> statusProvider = null)
        {
            _enemyCountProvider = enemyCountProvider;
            _statusProvider = statusProvider;
            ResetWindow();
        }

        public void SetEnemyCountProvider(Func<int> provider) => _enemyCountProvider = provider;

        private void Update()
        {
            // Sampled here rather than in OnGUI: OnGUI runs more than once per
            // frame, and an ECS query is not something to pay for twice for the
            // same number.
            _displayEnemyCount = _enemyCountProvider?.Invoke() ?? 0;
            _displayStatus = _statusProvider?.Invoke();

            float frameMs = Time.unscaledDeltaTime * 1000f;

            _windowElapsed += Time.unscaledDeltaTime;
            _windowFrames++;
            if (frameMs > _windowWorstFrameMs)
                _windowWorstFrameMs = frameMs;

            if (_windowElapsed < _sampleWindowSeconds)
                return;

            _displayAverageMs = _windowElapsed / _windowFrames * 1000f;
            _displayFps = _windowFrames / _windowElapsed;
            _displayWorstMs = _windowWorstFrameMs;
            ResetWindow();
        }

        private void ResetWindow()
        {
            _windowElapsed = 0f;
            _windowFrames = 0;
            _windowWorstFrameMs = 0f;
        }

        private void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = _fontSize,
                normal = { textColor = Color.white }
            };

            GUILayout.BeginArea(new Rect(12f, 12f, 480f, 220f));
            GUILayout.Label($"FPS: {_displayFps:F1}   ({_displayAverageMs:F2} ms)", _style);
            GUILayout.Label($"Worst frame: {_displayWorstMs:F2} ms", _style);
            GUILayout.Label($"Enemies alive: {_displayEnemyCount}", _style);

            if (!string.IsNullOrEmpty(_displayStatus))
                GUILayout.Label(_displayStatus, _style);

            GUILayout.EndArea();
        }
    }
}
