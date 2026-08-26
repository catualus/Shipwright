namespace Shipwright.Ui
{
    /// <summary>
    /// One row in the list of published items.
    ///
    /// gmpublish knows the id and the title; whether it is a map is a Workshop tag, which comes from
    /// the public details endpoint. The two are joined here rather than in either source, because
    /// either can be unavailable on its own and the list still has to render.
    /// </summary>
    public sealed record ListedItem(ulong Id, string Title, bool IsMap, string Detail);
}
