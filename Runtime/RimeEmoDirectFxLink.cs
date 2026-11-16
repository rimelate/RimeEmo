using UnityEngine;

namespace RimeEmo
{
    // This record lives on an EditorOnly child and is stripped from the uploaded avatar.
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class RimeEmoDirectFxLink : MonoBehaviour
    {
        [HideInInspector] public ScriptableObject project;
        [HideInInspector] public int mode;
        [HideInInspector] public RuntimeAnimatorController previousFx, appliedFx, baseFx;
        [HideInInspector] public Object previousMask, appliedMask;
        [HideInInspector] public bool previousCustomLayers, previousDefaultFx;
        [HideInInspector] public Object previousMenu, previousParameters, appliedMenu, appliedParameters;
        [HideInInspector] public bool previousCustomExpressions;
        [HideInInspector] public string fingerprint;
        [HideInInspector] public string fxHash, menuHash, parametersHash;
        [HideInInspector] public Object[] submenus;
        [HideInInspector] public string[] submenuHashes;
    }
}
