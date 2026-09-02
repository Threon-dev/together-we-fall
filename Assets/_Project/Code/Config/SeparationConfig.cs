using UnityEngine;

namespace TogetherWeFall.Config
{
    /// <summary>
    /// Crowd separation tuning — how hard enemies push each other apart so a
    /// wave flows around the player instead of collapsing into one point.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SeparationConfig",
        menuName = "Together We Fall/Separation Config")]
    public sealed class SeparationConfig : ScriptableObject
    {
        [Header("Push")]
        [Tooltip("Distance at which enemies start pushing each other away. " +
                 "Roughly two body radii is a good starting point.")]
        [SerializeField] private float _radius = 1.2f;

        [Tooltip("Strength of the push relative to move speed. Values above ~1 " +
                 "make the crowd springy and enemies bounce off each other " +
                 "instead of flowing.")]
        [SerializeField, Range(0f, 3f)] private float _strength = 0.8f;

        [Header("Spatial hash")]
        [Tooltip("Grid cell size. Values below Radius break neighbour search: " +
                 "the 3x3 cell block around an enemy would no longer cover the " +
                 "whole radius, so it is clamped up to Radius at bake time. " +
                 "Much larger values put too many enemies in one cell and the " +
                 "search degenerates towards brute force.")]
        [SerializeField] private float _cellSize = 1.2f;

        [Tooltip("Cap on neighbours considered per enemy. This is the knob that " +
                 "keeps cost bounded when a crowd piles up: past a dozen or so " +
                 "neighbours the extra ones barely change the push direction.")]
        [SerializeField, Range(1, 64)] private int _maxNeighbors = 12;

        public float Radius => _radius;
        public float Strength => _strength;
        public float CellSize => _cellSize;
        public int MaxNeighbors => _maxNeighbors;
    }
}
