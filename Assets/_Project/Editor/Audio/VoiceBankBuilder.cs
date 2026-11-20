using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenworldCrime.Audio;
using UnityEditor;
using UnityEngine;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Builds the VoiceBank from the imported voice-over (Assets/Voiceover): every clip's situation comes from the words in its
    /// file name (table below), "Different" / "Similer" are the two voices of street men and women. Also sets mobile-friendly
    /// import settings on the clips (mono, Vorbis, decompressed on load: short lines that play often).
    /// Menu: Tools > OpenworldCrime > Audio > Build Voice Bank.
    /// </summary>
    public static class VoiceBankBuilder
    {
        private const string Root = "Assets/Voiceover";
        private const string BankPath = "Assets/_Project/Data/Audio/VoiceBank.asset";

        // Words in the file name -> situations the line is used for.
        private static readonly (string words, VoiceLine[] situations)[] Npc =
        {
            // women
            ("That's my car", new[] { VoiceLine.CarStolen }),
            ("What's wrong with you", new[] { VoiceLine.Annoyed, VoiceLine.Hurt }),
            ("Stay away from me", new[] { VoiceLine.Threatened }),
            ("no manners", new[] { VoiceLine.Annoyed, VoiceLine.AmbientGrumble }),
            ("lovely day", new[] { VoiceLine.Greeting }),
            ("car looks amazing", new[] { VoiceLine.Impressed }),
            ("Somebody help", new[] { VoiceLine.Panic, VoiceLine.Hurt }),
            ("Run! Get out of here", new[] { VoiceLine.Panic, VoiceLine.Fleeing }),
            ("leave me alone", new[] { VoiceLine.Threatened, VoiceLine.Hurt }),
            ("paid enough", new[] { VoiceLine.AmbientGrumble }),
            ("out of your mind", new[] { VoiceLine.Shocked }),
            ("calling the police", new[] { VoiceLine.CallPolice, VoiceLine.Witness }),
            // men
            ("You looking at me", new[] { VoiceLine.Stare }),
            ("Mind your own damn business", new[] { VoiceLine.Stare, VoiceLine.Hurt }),
            ("watch where you're going", new[] { VoiceLine.Annoyed }),
            ("I'm outta here", new[] { VoiceLine.Fleeing }),
            ("Get away from me, crazy", new[] { VoiceLine.Threatened, VoiceLine.Hurt }),
            ("getting crazier", new[] { VoiceLine.AmbientGrumble, VoiceLine.Witness }),
            ("ain't what it used to be", new[] { VoiceLine.AmbientGrumble }),
            ("nice ride", new[] { VoiceLine.Impressed }),
            ("ain't getting involved", new[] { VoiceLine.Witness }),
            ("Somebody call the cops", new[] { VoiceLine.Panic, VoiceLine.CallPolice, VoiceLine.CarStolen }),
            ("What the hell was that", new[] { VoiceLine.Shocked }),
            ("Beautiful day", new[] { VoiceLine.Greeting }),
        };

        private static readonly (string words, VoiceLine[] situations)[] Player =
        {
            // Jason
            ("You got a problem with me", new[] { VoiceLine.ReplyStare, VoiceLine.FightStart }),
            ("I ain't got time for this", new[] { VoiceLine.ReplyRude }),
            ("let's get this over with", new[] { VoiceLine.RaceStart }),
            ("You really think I'm scared", new[] { VoiceLine.FightStart, VoiceLine.ReplyThreat }),
            ("one hell of a day", new[] { VoiceLine.WantedRaised }),
            ("Get in the car", new[] { VoiceLine.StealCar }),
            ("this city is crazy", new[] { VoiceLine.ReplyChaos, VoiceLine.ReplyCalm }),
            ("Nobody messes with me", new[] { VoiceLine.Brawl }),
            ("Stay outta my business", new[] { VoiceLine.ReplyThreat, VoiceLine.ReplyStare }),
            ("that was close", new[] { VoiceLine.CloseCall }),
            ("let's move", new[] { VoiceLine.WantedRaised, VoiceLine.RaceStart }),
            ("need a damn break", new[] { VoiceLine.WantedCleared }),
            // Mia
            ("wanna test me", new[] { VoiceLine.ReplyStare, VoiceLine.FightStart }),
            ("time for your nonsense", new[] { VoiceLine.ReplyRude }),
            ("We're leaving", new[] { VoiceLine.StealCar }),
            ("We've got work to do", new[] { VoiceLine.RaceStart }),
            ("I've seen worse", new[] { VoiceLine.RaceWin, VoiceLine.ReplyImpressed }),
            ("gotta be kidding me", new[] { VoiceLine.WantedRaised, VoiceLine.ReplyChaos }),
            ("wrong woman", new[] { VoiceLine.Brawl }),
            ("minute to breathe", new[] { VoiceLine.WantedCleared }),
            ("See what happens", new[] { VoiceLine.ReplyThreat }),
            ("never sleeps", new[] { VoiceLine.ReplyCalm }),
            ("That was close", new[] { VoiceLine.CloseCall }),
            ("Move it", new[] { VoiceLine.WantedRaised, VoiceLine.RaceStart }),
        };

        private static readonly (string words, VoiceLine[] situations)[] Police =
        {
            ("Drop your weapon", new[] { VoiceLine.PoliceDropWeapon }),
            ("suspect fleeing", new[] { VoiceLine.PoliceFleeing }),
            ("Stop right there", new[] { VoiceLine.PoliceStop }),
            ("under arrest", new[] { VoiceLine.PoliceArrest }),
            ("Get on the ground", new[] { VoiceLine.PoliceArrest }),
            ("need backup", new[] { VoiceLine.PoliceBackup }),
        };

        [MenuItem("Tools/OpenworldCrime/Audio/Build Voice Bank")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("[VoiceBankBuilder]\n");
            var men = new List<VoiceBank.Voice>();
            var women = new List<VoiceBank.Voice>();
            VoiceBank.Voice jason = null, mia = null, police = null;
            foreach (string dir in Directory.GetDirectories(Root, "*", SearchOption.AllDirectories))
            {
                string[] files = Directory.GetFiles(dir, "*.mp3");
                if (files.Length == 0) continue;
                string path = dir.Replace('\\', '/');
                string upper = path.ToUpperInvariant();
                (string, VoiceLine[])[] table = upper.Contains("POLICE") ? Police : upper.Contains("PLAYER") ? Player : Npc;
                var voice = new VoiceBank.Voice { name = path.Substring(Root.Length + 1) };
                var lines = new List<VoiceBank.Line>();
                foreach (string file in files)
                {
                    string asset = file.Replace('\\', '/');
                    SetImport(asset);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(asset);
                    string name = Path.GetFileNameWithoutExtension(asset);
                    bool found = false;
                    foreach ((string words, VoiceLine[] situations) in table)
                    {
                        if (name.IndexOf(words, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                        foreach (VoiceLine s in situations) lines.Add(new VoiceBank.Line { situation = s, clip = clip });
                        log.AppendLine($"  {voice.name}: '{name}' -> {string.Join(", ", situations)}");
                        found = true;
                        break;
                    }
                    if (!found) log.AppendLine($"  {voice.name}: '{name}' -> NOT CLASSIFIED");
                }
                voice.lines = lines.ToArray();
                if (upper.Contains("POLICE")) police = voice;
                else if (upper.Contains("PLAYER") && upper.Contains("FEMALE")) mia = voice;
                else if (upper.Contains("PLAYER")) jason = voice;
                else if (upper.Contains("FEMALE")) women.Add(voice);
                else men.Add(voice);
            }
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/Audio")) AssetDatabase.CreateFolder("Assets/_Project/Data", "Audio");
            var bank = AssetDatabase.LoadAssetAtPath<VoiceBank>(BankPath);
            if (bank == null) { bank = ScriptableObject.CreateInstance<VoiceBank>(); AssetDatabase.CreateAsset(bank, BankPath); }
            bank.EditorSet(men.ToArray(), women.ToArray(), jason, mia, police);
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            log.AppendLine($"  bank: {men.Count} male and {women.Count} female street voices, Jason {(jason != null)}, Mia {(mia != null)}, police {(police != null)}");
            return log.ToString();
        }

        private static void SetImport(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;
            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            if (importer.forceToMono && s.loadType == AudioClipLoadType.DecompressOnLoad && s.compressionFormat == AudioCompressionFormat.Vorbis) return;
            importer.forceToMono = true;
            importer.loadInBackground = true;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.55f;
            s.preloadAudioData = true;
            importer.defaultSampleSettings = s;
            importer.SaveAndReimport();
        }
    }
}
