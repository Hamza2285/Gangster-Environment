using UnityEngine;

namespace OpenworldCrime.UI.Menu
{
    /// <summary>
    /// The GTA-style artwork used by the splash and loading screens (configuration only, no runtime state).
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/UI/Splash Art Set", fileName = "SplashArt")]
    public class SplashArtSet : ScriptableObject
    {
        [Tooltip("Backgrounds with no people: character cut-outs are shown in front of these.")]
        public Sprite[] stillBackgrounds = new Sprite[0];
        [Tooltip("Character cut-outs (transparent PNGs).")]
        public Sprite[] characters = new Sprite[0];
        [Tooltip("Complete artworks (characters painted in): the whole image moves.")]
        public Sprite[] fullArtworks = new Sprite[0];
    }
}
