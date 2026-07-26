using Fusion;
using UnityEngine;

namespace _Project.Code.Player
{
    public struct NetworkInputData : INetworkInput
    {
        public Vector3 Direction;
        public bool MouseButton0;
    }
}