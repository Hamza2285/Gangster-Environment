using UnityEngine;

// Holds the button art and font for GameButtonStyle (Resources/UIButtons/ButtonStyle.asset).
public class GameButtonStyleAsset : ScriptableObject
{
    public Sprite pink;
    public Sprite green;
    public Sprite panel;
    public Font font;
    [Tooltip("Mobile get-in button icon (Game_Play_buttons sheet), set by Game/Mobile Controls Skin.")]
    public Sprite enterCar;
    [Tooltip("Weapon selector panel and its arrows (Game_Play_buttons sheet), set by Game/Mobile Controls Skin.")]
    public Sprite weaponPanel;
    public Sprite arrowLeft;
    public Sprite arrowRight;
}
