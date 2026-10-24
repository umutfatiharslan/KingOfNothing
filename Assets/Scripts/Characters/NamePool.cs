using System.Collections.Generic;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>Pool of names for new villagers.</summary>
    [CreateAssetMenu(fileName = "NamePool", menuName = "King of Nothing/Characters/Name Pool")]
    public class NamePool : ScriptableObject
    {
        [SerializeField] List<string> names = new List<string>
        {
            "Aldric", "Bram", "Cedric", "Edda", "Fenna", "Gareth", "Hilda", "Ivo", "Jorund", "Kara",
            "Leofric", "Mabel", "Nils", "Odette", "Percival", "Quill", "Rowena", "Sten", "Tilda", "Ulric",
        };

        public string PickRandom() => names.Count > 0 ? names[UnityEngine.Random.Range(0, names.Count)] : "Villager";

#if UNITY_EDITOR
        public void EditorSetNames(List<string> newNames) => names = newNames;
#endif
    }
}
