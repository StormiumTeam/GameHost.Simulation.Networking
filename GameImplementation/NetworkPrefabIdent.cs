using System.Collections.Generic;
using UnityEngine;

namespace package.stormiumteam.networking.game
{
    public class NetworkPrefabIdent : MonoBehaviour
    {
        private static Dictionary<string, GameObject> s_AllRegistredPrefabs;
        
        public GameObject Original;
        public string PrefabId;

        private void Awake()
        {
            if (string.IsNullOrEmpty(PrefabId) || Original == null)
            {
                var extMsg = "'PrefabId' and 'Original' are";
                if (string.IsNullOrEmpty(PrefabId) && Original != null) extMsg = "'PrefabId' is";
                else if (Original == null) extMsg = "'Original' is";
                
                Debug.LogError
                (
                    $"GameObject '{gameObject.name}' {nameof(NetworkPrefabIdent)} component field {extMsg} empty"
                );

                return;
            }
            
            Register(Original, PrefabId);
        }

        public static void Register(GameObject prefab, string id)
        {
            if (s_AllRegistredPrefabs.ContainsKey(id) && s_AllRegistredPrefabs[id] != prefab)
            {
                Debug.LogError
                (
                    $"Wanted prefab to register {prefab.name} is conflicting with {s_AllRegistredPrefabs[id].name}"
                );
                return;
            }
            
            s_AllRegistredPrefabs[id] = prefab;
        }

        public static void ForceRegister(GameObject prefab, string id)
        {
            s_AllRegistredPrefabs[id] = prefab;
        }
    }
}