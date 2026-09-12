using UnityEngine;

namespace TogetherWeFall.Player
{
    /// <summary>
    /// A camera pointed at the player from the front, drawing into a texture the
    /// inventory panel can hang on a wall.
    ///
    /// A second camera rather than a copy of the character posed in a corner of
    /// the map. A copy would be a second answer to "what is this character
    /// wearing" — it would have to be kept in step with every equip, and the day
    /// it fell behind the panel would be showing somebody else's gear. This
    /// looks at the same body the game does, so it cannot disagree.
    ///
    /// It sees only the character because it is culled to the character's own
    /// layer, which is the whole reason that layer exists. The world behind is
    /// not hidden, it is never rendered.
    ///
    /// Presentation, and nothing but: it reads a transform and writes a texture.
    /// A build without it leaves the panel drawing its placeholder and plays
    /// identically — which is the rule every presenter in this project follows.
    /// </summary>
    public sealed class CharacterPortrait : MonoBehaviour
    {
        [Tooltip("The camera that draws the portrait. A child of the player, so " +
                 "it turns with them and always sees the front.")]
        [SerializeField] private Camera _camera;

        [Tooltip("Size of the texture, in pixels. Taller than it is wide, " +
                 "because a person is.")]
        [SerializeField] private int _width = 220;
        [SerializeField] private int _height = 320;

        private RenderTexture _texture;

        /// <summary>
        /// What to draw. Made on first use rather than on load: a scene where
        /// nobody ever opens the bag should not be paying for a render target.
        /// </summary>
        public Texture Texture
        {
            get
            {
                Ensure();
                return _texture;
            }
        }

        /// <summary>
        /// Whether anybody is looking.
        ///
        /// The camera is off unless the panel is open, so the portrait costs a
        /// pass only while it is being read. Nothing else about the character
        /// changes either way.
        /// </summary>
        public void SetShown(bool shown)
        {
            if (_camera == null)
                return;

            // Order matters: a camera with no target texture renders to the
            // screen, which would put a close-up of the player over the game.
            if (shown)
                Ensure();

            _camera.enabled = shown && _texture != null;
        }

        /// <summary>
        /// The shape of the hole it is being drawn into.
        ///
        /// The texture keeps its own size — that is resolution, not shape — and
        /// the camera is told what proportions to project for. Without this the
        /// character is stretched by however much the frame differs from the
        /// texture, which on a person is the one kind of wrong everybody sees.
        /// </summary>
        public void SetAspect(float aspect)
        {
            if (_camera != null && aspect > 0.01f)
                _camera.aspect = aspect;
        }

        private void Awake()
        {
            if (_camera != null)
                _camera.enabled = false;
        }

        private void Ensure()
        {
            if (_texture != null || _camera == null)
                return;

            _texture = new RenderTexture(_width, _height, 24)
            {
                name = "CharacterPortrait",
                antiAliasing = 2
            };

            _camera.targetTexture = _texture;
        }

        private void OnDestroy()
        {
            if (_camera != null)
                _camera.targetTexture = null;

            if (_texture == null)
                return;

            _texture.Release();
            Destroy(_texture);
        }
    }
}
