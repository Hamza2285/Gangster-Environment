using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenworldCrime.Audio
{
    /// <summary>What a line is said for (one clip can be listed for several situations).</summary>
    public enum VoiceLine
    {
        // Street people
        Greeting, AmbientGrumble, Stare, Annoyed, Hurt, Threatened, Panic, Fleeing, Shocked, Witness, CallPolice, CarStolen, Impressed,
        // Player (Jason / Mia)
        ReplyRude, ReplyStare, ReplyThreat, ReplyCalm, ReplyChaos, ReplyImpressed, FightStart, Brawl, StealCar, WantedRaised,
        WantedCleared, CloseCall, RaceStart, RaceWin,
        // Police officers
        PoliceStop, PoliceDropWeapon, PoliceArrest, PoliceFleeing, PoliceBackup
    }

    /// <summary>
    /// The voice-over clips by speaker and situation (built from Assets/Voiceover by Tools > OpenworldCrime > Audio > Build Voice
    /// Bank, which reads each clip's situation from its file name). Two voices each for street men and women (a pedestrian keeps
    /// the one he got), Jason, Mia and the police. Configuration only: nothing here changes at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "OpenworldCrime/Audio/Voice Bank", fileName = "VoiceBank")]
    public class VoiceBank : ScriptableObject
    {
        [Serializable]
        public class Line
        {
            public VoiceLine situation;
            public AudioClip clip;
        }

        [Serializable]
        public class Voice
        {
            public string name;
            public Line[] lines = new Line[0];

            public bool Has(VoiceLine situation)
            {
                foreach (Line l in lines) if (l.situation == situation && l.clip != null) return true;
                return false;
            }

            /// <summary>A random clip for the situation (null when this voice has none), not the one given if it can avoid it.</summary>
            public AudioClip Pick(VoiceLine situation, AudioClip avoid)
            {
                int count = 0;
                foreach (Line l in lines) if (l.situation == situation && l.clip != null) count++;
                if (count == 0) return null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    int pick = UnityEngine.Random.Range(0, count);
                    foreach (Line l in lines)
                    {
                        if (l.situation != situation || l.clip == null) continue;
                        if (pick-- != 0) continue;
                        if (l.clip != avoid || count == 1 || attempt == 2) return l.clip;
                        break;
                    }
                }
                return null;
            }
        }

        [SerializeField] private Voice[] maleNpc = new Voice[0];
        [SerializeField] private Voice[] femaleNpc = new Voice[0];
        [SerializeField] private Voice playerMale = new Voice();
        [SerializeField] private Voice playerFemale = new Voice();
        [SerializeField] private Voice police = new Voice();

        public IReadOnlyList<Voice> MaleNpc => maleNpc;
        public IReadOnlyList<Voice> FemaleNpc => femaleNpc;
        public Voice PlayerMale => playerMale;
        public Voice PlayerFemale => playerFemale;
        public Voice Police => police;

#if UNITY_EDITOR
        public void EditorSet(Voice[] men, Voice[] women, Voice jason, Voice mia, Voice officers)
        {
            maleNpc = men; femaleNpc = women; playerMale = jason; playerFemale = mia; police = officers;
        }
#endif
    }
}
