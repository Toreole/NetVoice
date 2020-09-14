using UnityEngine;

[CreateAssetMenu(menuName = "Temp/ColorPallette")]
public class ColorPallette : ScriptableObject
{
    [SerializeField]
    public Color[] colors;
}