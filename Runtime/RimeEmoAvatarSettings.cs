using UnityEngine;

namespace RimeEmo
{
    // Stored on a dedicated EditorOnly child; never tag the avatar root EditorOnly.
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class RimeEmoAvatarSettings : MonoBehaviour
    {
        // The project type belongs to the editor assembly. Keep the runtime assembly independent.
        [HideInInspector] public ScriptableObject project;
    }
}
