using System;
using UnityEngine;

namespace OpenworldCrime.Vehicles
{
    /// <summary>
    /// Editor bookkeeping for CarMaterialSetup: each car material slot whose model material has no texture is given a shared
    /// look-alike material (glass, rubber, trim, lamp lens, interior, paint). The original is recorded here so the slot is
    /// restored automatically as soon as its real texture is imported (Tools > OpenworldCrime > Vehicles > Link Car Textures).
    /// No runtime logic.
    /// </summary>
    public class CarMaterialFallback : MonoBehaviour
    {
        [Serializable]
        public struct Record
        {
            public Renderer renderer;
            public int materialIndex;
            public Material original;
            public Material fallback;
        }

        [SerializeField] private Record[] records = new Record[0];

        public Record[] Records => records;

#if UNITY_EDITOR
        public void EditorSetRecords(Record[] newRecords) => records = newRecords;
#endif
    }
}
