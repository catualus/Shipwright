namespace Shipwright.Ui
{
    /// <summary>
    /// One row in the list of published items.
    ///
    /// gmpublish knows the id and the title; whether it is a map is a Workshop tag, which comes from
    /// the public details endpoint. The two are joined here rather than in either source, because
    /// either can be unavailable on its own and the list still has to render.
    /// </summary>
    /// <param name="IsBound">
    /// Whether this is the item the map currently publishes to.
    ///
    /// Recomputed every time the list is shown rather than stored with the row: binding changes
    /// which row is the answer, and a list that still offered "Bind" on the row you had just bound
    /// gave no sign that anything had happened.
    /// </param>
    public sealed record ListedItem(ulong Id, string Title, bool IsMap, string Detail, bool IsBound = false);
}
