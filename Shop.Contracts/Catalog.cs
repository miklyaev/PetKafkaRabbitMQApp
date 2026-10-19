namespace Shop.Contracts;

/// <summary>
/// Статический каталог товаров — единый источник цен для витрины (Shop.Web)
/// и сервиса заказов (Shop.Orders). Пет-упрощение: полноценный каталог-сервис не заводим.
/// </summary>
public static class Catalog
{
    public static readonly IReadOnlyList<Product> Items =
    [
        new("Кофе в зёрнах, 1 кг", 1490m, "☕"),
        new("Наушники беспроводные", 6990m, "🎧"),
        new("Клавиатура механическая", 4990m, "⌨️"),
        new("Мышь беспроводная", 1990m, "🖱️"),
        new("SSD 1 ТБ", 7490m, "💾"),
        new("Умная колонка", 8990m, "🔊"),
        new("Монитор 27\" 4K", 24990m, "🖥️"),
        new("Эргономичное кресло", 18990m, "🪑"),
        new("Коврик для мыши XXL", 1290m, "⬛"),
        new("Внешний аккумулятор", 2490m, "🔋"),
    ];

    /// <summary>Ищет товар по названию (без учёта регистра).</summary>
    public static Product? Find(string name) =>
        Items.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Товар каталога; Icon — эмодза для витрины.</summary>
public record Product(string Name, decimal Price, string Icon);
