using System;
using UnityEngine;

namespace TogetherWeFall.DebugTools
{
    /// <summary>
    /// FPS and enemy-count readout. Deliberately on OnGUI: zero dependency on a
    /// uGUI canvas and zero influence on the measurements we care about (OnGUI's
    /// own cost is stable and does not grow with the enemy count).
    ///
    /// Both the average and the worst frame in the window are shown: an average
    /// FPS hides exactly the spikes that time-sliced pathfinding exists to avoid.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        [SerializeField] private float _sampleWindowSeconds = 0.5f;
        [SerializeField] private int _fontSize = 18;

        private Func<int> _enemyCountProvider;

        private float _windowElapsed;
        private int _windowFrames;
        private float _windowWorstFrameMs;

        private float _displayFps;
        private float _displayAverageMs;
        private float _displayWorstMs;

        private GUIStyle _style;

        /// <summary>
        /// enemyCountProvider stays null until stage 2, where an ECS query is
        /// passed in. The HUD knows nothing about ECS — only about the delegate.
        /// </summary>
        public void Initialize(Func<int> enemyCountProvider = null)
        {
            _enemyCountProvider = enemyCountProvider;
            ResetWindow();
        }

        public void SetEnemyCountProvider(Func<int> provider) => _enemyCountProvider = provider;

        private void Update()
        {
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

            int enemyCount = _enemyCountProvider?.Invoke() ?? 0;

            GUILayout.BeginArea(new Rect(12f, 12f, 340f, 130f));
            GUILayout.Label($"FPS: {_displayFps:F1}   ({_displayAverageMs:F2} ms)", _style);
            GUILayout.Label($"Worst frame: {_displayWorstMs:F2} ms", _style);
            GUILayout.Label($"Enemies alive: {enemyCount}", _style);
            GUILayout.EndArea();
        }
    }
}
