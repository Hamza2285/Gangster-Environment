using UnityEngine;

namespace OpenworldCrime.Audio
{
    /// <summary>
    /// One car radio station (configuration only, never changed at runtime): name and logo for the HUD, its songs, and the short
    /// station idents / jingles played now and then between songs. Built from the Assets/GTARadio folders by
    /// Tools > OpenworldCrime > Audio > Set Up Car Radio; edit freely afterwards.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Audio/Radio Station", fileName = "RadioStation")]
    public class RadioStation : ScriptableObject
    {
        [SerializeField] private string displayName = "Radio";
        [SerializeField] private Sprite logo;
        [Tooltip("Songs (stream from disk: set their import Load Type to Streaming).")]
        [SerializeField] private AudioClip[] songs = new AudioClip[0];
        [Tooltip("Short station idents / jingles, played between some songs.")]
        [SerializeField] private AudioClip[] idents = new AudioClip[0];
        [Tooltip("Chance of an ident after a song.")]
        [SerializeField, Range(0f, 1f)] private float identChance = 0.35f;

        public string DisplayName => displayName;
        public Sprite Logo => logo;
        public AudioClip[] Songs => songs;
        public AudioClip[] Idents => idents;
        public float IdentChance => identChance;
        public bool HasSongs => songs != null && songs.Length > 0;

#if UNITY_EDITOR
        public void EditorSet(string name, Sprite icon, AudioClip[] songClips, AudioClip[] identClips)
        {
            displayName = name;
            logo = icon;
            songs = songClips;
            idents = identClips;
        }
#endif
    }
}
