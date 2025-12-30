namespace NationsCities.Models;

/// <summary>
/// Kategoria gry.
/// </summary>
public class Category
{
    /// <summary>
    /// Nazwa kategorii (np. "Państwa").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Ikona Material Symbols (np. "flag").
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// Czy to kategoria niestandardowa (dodana przez gracza).
    /// </summary>
    public bool IsCustom { get; set; }

    public Category() { }

    public Category(string name, string icon, bool isCustom = false)
    {
        Name = name;
        Icon = icon;
        IsCustom = isCustom;
    }

    /// <summary>
    /// Standardowe kategorie gry.
    /// </summary>
    public static List<Category> StandardCategories =>
    [
        new("Państwa", "flag"),
        new("Miasta", "apartment"),
        new("Zwierzęta", "pets"),
        new("Rośliny", "eco"),
        new("Imiona", "person"),
        new("Zawody", "engineering"),
        new("Rzeczy", "inventory_2"),
        new("Jedzenie", "lunch_dining"),
        new("Filmy", "theaters"),
        new("Kolory", "palette"),
    ];
}
