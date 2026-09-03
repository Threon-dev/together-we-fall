using System;
using UnityEngine;

namespace TogetherWeFall.Dungeon
{
    /// <summary>
    /// Materials the dungeon geometry is painted with, one per room type.
    ///
    /// Room type is colour-coded on purpose. Until there is real art, the only
    /// way to tell at a glance whether generation put the boss room at the far
    /// end and the treasure rooms in dead ends is to see it on the floor.
    ///
    /// Plain material references rather than Addressables: these are needed for
    /// the whole run, from the first frame, and there is nothing to stream in or
    /// release. The convention aims at heavy assets that may never be used.
    /// </summary>
    [Serializable]
    public sealed class DungeonMaterialSet
    {
        [SerializeField] private Material _wall;
        [SerializeField] private Material _corridorFloor;
        [SerializeField] private Material _entranceFloor;
        [SerializeField] private Material _transitFloor;
        [SerializeField] private Material _combatFloor;
        [SerializeField] private Material _treasureFloor;
        [SerializeField] private Material _bossFloor;

        public Material Wall => _wall;
        public Material CorridorFloor => _corridorFloor;

        public Material FloorFor(DungeonRoomType type)
        {
            switch (type)
            {
                case DungeonRoomType.Entrance: return _entranceFloor;
                case DungeonRoomType.Combat: return _combatFloor;
                case DungeonRoomType.Treasure: return _treasureFloor;
                case DungeonRoomType.Boss: return _bossFloor;
                default: return _transitFloor;
            }
        }

        public bool IsComplete =>
            _wall != null && _corridorFloor != null && _entranceFloor != null &&
            _transitFloor != null && _combatFloor != null && _treasureFloor != null &&
            _bossFloor != null;
    }
}
