using System;
using UnityEngine;

// Everything the clothing store sells for one character model. Each item names the model's own parts (child objects)
// that make it up, so dressing is just switching parts on and off. Built by Game/Set Up Clothing Store.
[CreateAssetMenu(menuName = "Game/Clothing Catalog")]
public class ClothingCatalog : ScriptableObject
{
    public enum Focus { Body, Torso, Legs, Feet, Head, Neck }

    [Serializable]
    public class Item
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        public int price;
        [Tooltip("Child objects of the character model shown for this item. Empty means wearing nothing in this category.")]
        public string[] parts = new string[0];
        [Tooltip("Tops: whether the bare arms show with it.")]
        public bool showsArms;
        [Tooltip("Bottoms: whether the bare lower legs show with it.")]
        public bool showsLegs;
        [Tooltip("Free and owned from the start.")]
        public bool starter;
        public Sprite icon;
    }

    [Serializable]
    public class Category
    {
        public string id;
        public string displayName;
        public Focus focus;
        [Tooltip("Worn when nothing has been saved yet and the model shows none of this category's items.")]
        public string defaultItem;
        public Item[] items = new Item[0];
    }

    [Tooltip("Whose clothes these are (PlayableCharacter id).")]
    public string characterId = "jason";
    public string storeName = "Miami Clothing Store";
    [Tooltip("Skin shown when the top leaves the arms bare.")]
    public string armsPart = "Object001";
    [Tooltip("Skin shown when the bottoms leave the lower legs bare.")]
    public string legsPart = "legs";
    public Category[] categories = new Category[0];

    public Category FindCategory(string id)
    {
        foreach (var category in categories) if (category.id == id) return category;
        return null;
    }

    public Item FindItem(Category category, string id)
    {
        if (category == null) return null;
        foreach (var item in category.items) if (item.id == id) return item;
        return null;
    }
}
